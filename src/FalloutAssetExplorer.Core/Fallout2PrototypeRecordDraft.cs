using System.Buffers.Binary;

namespace FalloutAssetExplorer.Core;

public enum Fallout2PrototypeRecordDraftStatus
{
    Success,
    InspectionMismatch,
    UnauthorizedField,
    UnsupportedRepresentation,
    InvalidRange,
    InvalidValue,
    OverlappingRanges
}

public sealed class Fallout2PrototypeRecordDraftResult
{
    private readonly byte[] _recordBytes;

    internal Fallout2PrototypeRecordDraftResult(
        Fallout2PrototypeRecordDraftStatus status,
        string diagnostic,
        ReadOnlyMemory<byte> recordBytes,
        int changedFieldCount)
    {
        Status = status;
        Diagnostic = diagnostic;
        _recordBytes = recordBytes.ToArray();
        ChangedFieldCount = changedFieldCount;
    }

    public Fallout2PrototypeRecordDraftStatus Status { get; }

    public bool IsSuccess => Status == Fallout2PrototypeRecordDraftStatus.Success;

    public string Diagnostic { get; }

    public ReadOnlyMemory<byte> RecordBytes => _recordBytes.ToArray();

    public int ChangedFieldCount { get; }
}

public static class Fallout2PrototypeRecordDraftBuilder
{
    private sealed record AuthorizedChange(
        string FieldName,
        Fallout2PrototypeFieldValueKind ValueKind,
        int Offset,
        int Width,
        long Value);

    public static Fallout2PrototypeRecordDraftResult Build(
        Fallout2PrototypeInspectionContext inspection,
        Fallout2PrototypeEditSession session)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        ArgumentNullException.ThrowIfNull(session);

        lock (session.SyncRoot)
        {
            return BuildLocked(inspection, session);
        }
    }

    private static Fallout2PrototypeRecordDraftResult BuildLocked(
        Fallout2PrototypeInspectionContext inspection,
        Fallout2PrototypeEditSession session)
    {
        if (!session.IsBoundTo(inspection))
        {
            return Failure(
                Fallout2PrototypeRecordDraftStatus.InspectionMismatch,
                "The inspection context does not match the edit session source snapshot.");
        }

        var originalBytes = inspection.OriginalRecordBytes;
        var changes = new List<AuthorizedChange>();
        foreach (var field in session.Fields)
        {
            if (field.CurrentValue == field.OriginalValue)
            {
                continue;
            }

            var definition = field.Definition;
            if (definition.ValueKind is not (Fallout2PrototypeFieldValueKind.Int32BigEndian
                    or Fallout2PrototypeFieldValueKind.UInt8))
            {
                return Failure(
                    Fallout2PrototypeRecordDraftStatus.UnsupportedRepresentation,
                    $"Field '{field.Name}' uses unsupported draft representation {definition.ValueKind}.");
            }

            if (definition.Offset is not int offset
                || definition.Length is not int width
                || offset < 0
                || width <= 0
                || offset > originalBytes.Length
                || width > originalBytes.Length - offset)
            {
                return Failure(
                    Fallout2PrototypeRecordDraftStatus.InvalidRange,
                    $"Field '{field.Name}' has a byte range outside the source record.");
            }

            var expectedWidth = definition.ValueKind == Fallout2PrototypeFieldValueKind.Int32BigEndian
                ? sizeof(int)
                : sizeof(byte);
            if (width != expectedWidth)
            {
                return Failure(
                    Fallout2PrototypeRecordDraftStatus.UnsupportedRepresentation,
                    $"Field '{field.Name}' width does not match {definition.ValueKind}.");
            }

            if (!session.TryValidateDraftField(field, out var authorized, out var error))
            {
                return Failure(
                    authorized
                        ? Fallout2PrototypeRecordDraftStatus.InvalidValue
                        : Fallout2PrototypeRecordDraftStatus.UnauthorizedField,
                    error ?? $"Field '{field.Name}' failed draft validation.");
            }

            changes.Add(new AuthorizedChange(
                field.Name,
                definition.ValueKind,
                offset,
                width,
                field.CurrentValue!.Value));
        }

        for (var firstIndex = 0; firstIndex < changes.Count; firstIndex++)
        {
            var first = changes[firstIndex];
            var firstEnd = first.Offset + first.Width;
            for (var secondIndex = firstIndex + 1; secondIndex < changes.Count; secondIndex++)
            {
                var second = changes[secondIndex];
                var secondEnd = second.Offset + second.Width;
                if (first.Offset < secondEnd && second.Offset < firstEnd)
                {
                    return Failure(
                        Fallout2PrototypeRecordDraftStatus.OverlappingRanges,
                        $"Fields '{first.FieldName}' and '{second.FieldName}' modify overlapping record bytes.");
                }
            }
        }

        var draftBytes = originalBytes.ToArray();
        foreach (var change in changes)
        {
            var destination = draftBytes.AsSpan(change.Offset, change.Width);
            switch (change.ValueKind)
            {
                case Fallout2PrototypeFieldValueKind.Int32BigEndian:
                    BinaryPrimitives.WriteInt32BigEndian(destination, checked((int)change.Value));
                    break;
                case Fallout2PrototypeFieldValueKind.UInt8:
                    destination[0] = checked((byte)change.Value);
                    break;
                default:
                    return Failure(
                        Fallout2PrototypeRecordDraftStatus.UnsupportedRepresentation,
                        $"Field '{change.FieldName}' uses unsupported draft representation {change.ValueKind}.");
            }
        }

        return new Fallout2PrototypeRecordDraftResult(
            Fallout2PrototypeRecordDraftStatus.Success,
            changes.Count == 0
                ? "Draft matches the original record; no fields have changed."
                : $"In-memory draft created with {changes.Count} authorized field change(s).",
            draftBytes,
            changes.Count);
    }

    private static Fallout2PrototypeRecordDraftResult Failure(
        Fallout2PrototypeRecordDraftStatus status,
        string diagnostic) =>
        new(status, diagnostic, ReadOnlyMemory<byte>.Empty, changedFieldCount: 0);
}
