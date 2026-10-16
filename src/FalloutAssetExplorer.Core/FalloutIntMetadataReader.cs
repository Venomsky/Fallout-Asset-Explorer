using System.Buffers.Binary;

namespace FalloutAssetExplorer.Core;

public static class FalloutIntMetadataReader
{
    public const int MaximumFileSizeBytes = 64 * 1024 * 1024;
    public const int MaximumProcedureCount = 100_000;

    private const int PrefixSize = 42;
    private const int ProcedureCountSize = sizeof(int);
    private const int ProcedureRecordSize = 24;
    private const int TableLengthPrefixSize = sizeof(int);
    private const int ProcedureImportedFlag = 0x04;
    private const int ProcedureConditionalFlag = 0x02;

    /// <summary>
    /// Reads bounded structural metadata only. It does not validate bytecode or execute the script.
    /// Procedure fields are retained as raw values; in particular, field 20 is not named as an
    /// argument count because that meaning is not established for Fallout 1. The static-string
    /// prefix is also retained raw; available static-string bytes are bounded only by end of file.
    /// </summary>
    public static FalloutIntMetadataReadResult Parse(
        FalloutGame game,
        ReadOnlyMemory<byte> data)
    {
        if (game is not (FalloutGame.Fallout1 or FalloutGame.Fallout2))
        {
            return Failure(
                FalloutIntMetadataDiagnosticCode.UnsupportedGame,
                0,
                $"The game value '{game}' is not supported.");
        }

        if (data.Length > MaximumFileSizeBytes)
        {
            return Failure(
                FalloutIntMetadataDiagnosticCode.FileTooLarge,
                0,
                $"The INT file exceeds the {MaximumFileSizeBytes}-byte size limit.");
        }

        var bytes = data.Span;
        var procedureCountOffset = PrefixSize;
        if (bytes.Length - procedureCountOffset < ProcedureCountSize)
        {
            return Failure(
                FalloutIntMetadataDiagnosticCode.TruncatedProcedureCount,
                procedureCountOffset,
                "The INT file ended before the procedure count.");
        }

        var procedureCount = ReadInt32BigEndian(bytes, procedureCountOffset);
        if (procedureCount < 0)
        {
            return Failure(
                FalloutIntMetadataDiagnosticCode.InvalidProcedureCount,
                procedureCountOffset,
                "The procedure count cannot be negative.");
        }

        if (procedureCount > MaximumProcedureCount)
        {
            return Failure(
                FalloutIntMetadataDiagnosticCode.TooManyProcedures,
                procedureCountOffset,
                $"The procedure count exceeds the {MaximumProcedureCount}-procedure limit.");
        }

        long procedureTableEnd;
        try
        {
            procedureTableEnd = checked(
                PrefixSize
                + (long)ProcedureCountSize
                + (long)procedureCount * ProcedureRecordSize);
        }
        catch (OverflowException)
        {
            return Failure(
                FalloutIntMetadataDiagnosticCode.ArithmeticOverflow,
                procedureCountOffset,
                "The procedure table end offset overflowed.");
        }

        if (procedureTableEnd > bytes.Length)
        {
            return Failure(
                FalloutIntMetadataDiagnosticCode.TruncatedProcedureTable,
                bytes.Length,
                "The INT file ended before all procedure records were present.");
        }

        var identifierTableOffset = procedureTableEnd;
        if (!TryReadLengthPrefixedTable(
                bytes,
                identifierTableOffset,
                FalloutIntMetadataDiagnosticCode.TruncatedIdentifierTableHeader,
                FalloutIntMetadataDiagnosticCode.InvalidIdentifierTableSize,
                FalloutIntMetadataDiagnosticCode.TruncatedIdentifierTable,
                "identifier",
                out var identifierPayloadLength,
                out var identifierTableEnd,
                out var diagnostic))
        {
            return FalloutIntMetadataReadResult.Failure(diagnostic!);
        }

        var staticStringTableOffset = identifierTableEnd;
        if (!TryReadStaticStringPrefix(
                bytes,
                staticStringTableOffset,
                out var rawStaticStringTablePrefix,
                out var staticStringDataOffset,
                out diagnostic))
        {
            return FalloutIntMetadataReadResult.Failure(diagnostic!);
        }

        var identifierTableLength = checked((int)(identifierTableEnd - identifierTableOffset));
        var identifierTable = bytes.Slice((int)identifierTableOffset, identifierTableLength).ToArray();
        var procedures = new List<FalloutIntProcedureMetadata>(procedureCount);

        for (var index = 0; index < procedureCount; index++)
        {
            var recordOffset = checked(
                PrefixSize
                + (long)ProcedureCountSize
                + (long)index * ProcedureRecordSize);
            var record = bytes.Slice((int)recordOffset, ProcedureRecordSize);
            var field0 = ReadInt32BigEndian(record, 0);
            var field4 = ReadInt32BigEndian(record, 4);
            var field8 = ReadInt32BigEndian(record, 8);
            var field12 = ReadInt32BigEndian(record, 12);
            var field16 = ReadInt32BigEndian(record, 16);
            var field20 = ReadInt32BigEndian(record, 20);

            if (field0 < 0 || field0 >= identifierTable.Length)
            {
                return Failure(
                    FalloutIntMetadataDiagnosticCode.InvalidNameOffset,
                    recordOffset,
                    $"Procedure {index} has a name offset outside the identifier table.");
            }

            var nameStart = field0;
            var nameTerminator = identifierTable.AsSpan(nameStart).IndexOf((byte)0);
            if (nameTerminator < 0)
            {
                return Failure(
                    FalloutIntMetadataDiagnosticCode.UnterminatedName,
                    identifierTableOffset + nameStart,
                    $"Procedure {index} has no NUL terminator before the end of the identifier table.");
            }

            if ((field4 & ProcedureImportedFlag) == 0
                && !IsFileOffset(field16, bytes.Length))
            {
                return Failure(
                    FalloutIntMetadataDiagnosticCode.InvalidProcedureBodyOffset,
                    recordOffset + 16,
                    $"Procedure {index} has a body offset outside the INT file.");
            }

            if ((field4 & ProcedureConditionalFlag) != 0
                && !IsFileOffset(field12, bytes.Length))
            {
                return Failure(
                    FalloutIntMetadataDiagnosticCode.InvalidConditionOffset,
                    recordOffset + 12,
                    $"Conditional procedure {index} has a condition offset outside the INT file.");
            }

            procedures.Add(new FalloutIntProcedureMetadata(
                recordOffset,
                field0,
                field4,
                field8,
                field12,
                field16,
                field20,
                identifierTable.AsMemory(nameStart, nameTerminator)));
        }

        var metadata = new FalloutIntMetadata(
            game,
            procedureCount,
            identifierTableOffset,
            identifierPayloadLength,
            staticStringTableOffset,
            rawStaticStringTablePrefix,
            staticStringDataOffset,
            checked((int)(bytes.Length - staticStringDataOffset)),
            Array.AsReadOnly(procedures.ToArray()));
        return FalloutIntMetadataReadResult.Success(metadata);
    }

    private static bool TryReadStaticStringPrefix(
        ReadOnlySpan<byte> data,
        long tableOffset,
        out int rawPrefix,
        out long dataOffset,
        out FalloutIntMetadataDiagnostic? diagnostic)
    {
        rawPrefix = 0;
        dataOffset = tableOffset;

        if (tableOffset < 0
            || tableOffset > data.Length
            || data.Length - tableOffset < TableLengthPrefixSize)
        {
            diagnostic = new FalloutIntMetadataDiagnostic(
                FalloutIntMetadataDiagnosticCode.TruncatedStaticStringTablePrefix,
                tableOffset,
                "The INT file ended before the static-string table prefix.");
            return false;
        }

        var prefixOffset = checked((int)tableOffset);
        rawPrefix = ReadInt32BigEndian(data, prefixOffset);
        dataOffset = checked(tableOffset + TableLengthPrefixSize);
        diagnostic = null;
        return true;
    }

    private static bool TryReadLengthPrefixedTable(
        ReadOnlySpan<byte> data,
        long tableOffset,
        FalloutIntMetadataDiagnosticCode truncatedHeaderCode,
        FalloutIntMetadataDiagnosticCode invalidSizeCode,
        FalloutIntMetadataDiagnosticCode truncatedTableCode,
        string tableName,
        out int payloadLength,
        out long tableEnd,
        out FalloutIntMetadataDiagnostic? diagnostic)
    {
        payloadLength = 0;
        tableEnd = tableOffset;

        if (tableOffset < 0
            || tableOffset > data.Length
            || data.Length - tableOffset < TableLengthPrefixSize)
        {
            diagnostic = new FalloutIntMetadataDiagnostic(
                truncatedHeaderCode,
                tableOffset,
                $"The INT file ended before the {tableName} table size.");
            return false;
        }

        var payloadLengthOffset = checked((int)tableOffset);
        payloadLength = ReadInt32BigEndian(data, payloadLengthOffset);
        if (payloadLength < 0)
        {
            diagnostic = new FalloutIntMetadataDiagnostic(
                invalidSizeCode,
                tableOffset,
                $"The {tableName} table size cannot be negative.");
            return false;
        }

        try
        {
            tableEnd = checked(tableOffset + TableLengthPrefixSize + (long)payloadLength);
        }
        catch (OverflowException)
        {
            diagnostic = new FalloutIntMetadataDiagnostic(
                FalloutIntMetadataDiagnosticCode.ArithmeticOverflow,
                tableOffset,
                $"The {tableName} table end offset overflowed.");
            return false;
        }

        if (tableEnd > data.Length)
        {
            diagnostic = new FalloutIntMetadataDiagnostic(
                truncatedTableCode,
                data.Length,
                $"The INT file ended before the complete {tableName} table was present.");
            return false;
        }

        diagnostic = null;
        return true;
    }

    private static int ReadInt32BigEndian(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadInt32BigEndian(data.Slice(offset, sizeof(int)));

    private static bool IsFileOffset(int offset, int fileLength) =>
        offset >= 0 && offset < fileLength;

    private static FalloutIntMetadataReadResult Failure(
        FalloutIntMetadataDiagnosticCode code,
        long byteOffset,
        string message) =>
        FalloutIntMetadataReadResult.Failure(
            new FalloutIntMetadataDiagnostic(code, byteOffset, message));
}

public sealed record FalloutIntProcedureMetadata(
    long RecordOffset,
    int RawField0,
    int RawField4,
    int RawField8,
    int RawField12,
    int RawField16,
    int RawField20,
    ReadOnlyMemory<byte> NameBytes);

public sealed class FalloutIntMetadata
{
    internal FalloutIntMetadata(
        FalloutGame game,
        int procedureCount,
        long identifierTableOffset,
        int identifierPayloadLength,
        long staticStringTableOffset,
        int rawStaticStringTablePrefix,
        long staticStringDataOffset,
        int staticStringDataAvailableLength,
        IReadOnlyList<FalloutIntProcedureMetadata> procedures)
    {
        Game = game;
        ProcedureCount = procedureCount;
        IdentifierTableOffset = identifierTableOffset;
        IdentifierPayloadLength = identifierPayloadLength;
        StaticStringTableOffset = staticStringTableOffset;
        RawStaticStringTablePrefix = rawStaticStringTablePrefix;
        StaticStringDataOffset = staticStringDataOffset;
        StaticStringDataAvailableLength = staticStringDataAvailableLength;
        Procedures = procedures;
    }

    public FalloutGame Game { get; }

    public int ProcedureCount { get; }

    public long IdentifierTableOffset { get; }

    public int IdentifierPayloadLength { get; }

    public long StaticStringTableOffset { get; }

    /// <summary>
    /// The 32-bit value stored at the start of the static-string table. Its meaning is not
    /// established by the inspected CE readers.
    /// </summary>
    public int RawStaticStringTablePrefix { get; }

    public long StaticStringDataOffset { get; }

    /// <summary>
    /// Bytes available from the static-string data offset to end of file. This is a safe read
    /// boundary, not a confirmed static-string table length.
    /// </summary>
    public int StaticStringDataAvailableLength { get; }

    public IReadOnlyList<FalloutIntProcedureMetadata> Procedures { get; }
}

public enum FalloutIntMetadataDiagnosticCode
{
    UnsupportedGame,
    FileTooLarge,
    TruncatedProcedureCount,
    InvalidProcedureCount,
    TooManyProcedures,
    TruncatedProcedureTable,
    TruncatedIdentifierTableHeader,
    InvalidIdentifierTableSize,
    TruncatedIdentifierTable,
    TruncatedStaticStringTablePrefix,
    InvalidNameOffset,
    UnterminatedName,
    InvalidProcedureBodyOffset,
    InvalidConditionOffset,
    ArithmeticOverflow
}

public sealed record FalloutIntMetadataDiagnostic(
    FalloutIntMetadataDiagnosticCode Code,
    long ByteOffset,
    string Message);

public sealed class FalloutIntMetadataReadResult
{
    private FalloutIntMetadataReadResult(
        bool isSuccess,
        FalloutIntMetadata? metadata,
        FalloutIntMetadataDiagnostic? diagnostic)
    {
        IsSuccess = isSuccess;
        Metadata = metadata;
        Diagnostic = diagnostic;
    }

    public bool IsSuccess { get; }

    public FalloutIntMetadata? Metadata { get; }

    public FalloutIntMetadataDiagnostic? Diagnostic { get; }

    internal static FalloutIntMetadataReadResult Success(FalloutIntMetadata metadata) =>
        new(true, metadata, null);

    internal static FalloutIntMetadataReadResult Failure(FalloutIntMetadataDiagnostic diagnostic) =>
        new(false, null, diagnostic);
}
