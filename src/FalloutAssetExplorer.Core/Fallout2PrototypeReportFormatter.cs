using System.Globalization;
using System.Text;

namespace FalloutAssetExplorer.Core;

public static class Fallout2PrototypeReportFormatter
{
    private const int BytesPerHexLine = 16;

    public static string Format(Fallout2StandalonePrototypeInspection inspection)
    {
        ArgumentNullException.ThrowIfNull(inspection);

        var report = new StringBuilder();
        report.AppendLine("Fallout Asset Explorer — PRO Inspection Report");
        report.AppendLine($"Parsing result: {GetOutcomeText(inspection.Outcome)}");
        report.AppendLine();

        report.AppendLine("[Archive Entry]");
        AppendValue(report, "Archive", inspection.ArchivePath);
        AppendValue(report, "Game", inspection.Game?.ToString());
        AppendValue(report, "Archive format", inspection.Format?.ToString());
        AppendValue(report, "Entry path", inspection.EntryPath);
        AppendValue(report, "Entry location", inspection.ArchiveLocation);
        AppendValue(report, "Compressed size", FormatSize(inspection.CompressedSize));
        AppendValue(report, "Declared size", FormatSize(inspection.FileSize));
        AppendValue(report, "Actual bytes read", FormatSize(inspection.ActualSize));
        AppendValue(report, "Read complete", inspection.IsReadComplete ? "Yes" : "No");
        AppendValue(report, "Bytes retained", inspection.RawBytes.Length.ToString(CultureInfo.InvariantCulture));
        report.AppendLine();

        report.AppendLine("[Provenance and Verification]");
        report.AppendLine("Provenance: PRO entry selected directly from the archive.");
        report.AppendLine("MAP PID/prototype-list verification: No.");
        report.AppendLine();

        report.AppendLine("[Prototype Identification]");
        AppendValue(report, "PID", inspection.PrototypePid is int pid
            ? $"0x{unchecked((uint)pid):X8}"
            : "unavailable");
        AppendValue(report, "PID type", FormatIdentifier(inspection.PidTypeName, inspection.PidType));
        AppendValue(report, "Subtype", FormatIdentifier(inspection.SubtypeName, inspection.Subtype));
        report.AppendLine();

        report.AppendLine("[Parsed Fields]");
        var fields = inspection.PidType switch
        {
            0 => inspection.ItemFields,
            2 => inspection.SceneryFields,
            _ => []
        };
        if (fields.Count == 0)
        {
            report.AppendLine("(no parsed fields)");
        }
        else
        {
            foreach (var field in fields.OrderBy(field => field.Offset).ThenBy(field => field.Name, StringComparer.Ordinal))
            {
                var value = FormatFieldValue(field);
                var interpretation = string.IsNullOrWhiteSpace(field.Interpretation)
                    ? string.Empty
                    : $" | interpretation: {field.Interpretation}";
                report.AppendLine(
                    $"offset 0x{field.Offset:X4} | {field.Name} | value: {value} "
                    + $"| type: {field.StorageType} | size: {field.SizeInBytes} B{interpretation}");
            }
        }
        report.AppendLine();

        report.AppendLine("[Parser Diagnostics]");
        report.AppendLine($"Result: {GetOutcomeText(inspection.Outcome)}");
        report.AppendLine($"Parser message (preserved verbatim): {inspection.Status}");
        report.AppendLine();

        report.AppendLine("[Hex Dump of Available Bytes]");
        AppendHexDump(report, inspection.RawBytes.Span);

        return report.ToString();
    }

    private static string GetOutcomeText(PrototypeInspectionOutcome outcome) =>
        outcome switch
        {
            PrototypeInspectionOutcome.Success => "Success",
            PrototypeInspectionOutcome.Partial => "Partial success",
            PrototypeInspectionOutcome.Error => "Error",
            _ => "Unknown"
        };

    private static string FormatSize(long? size) =>
        size is long value
            ? $"{value.ToString("N0", CultureInfo.InvariantCulture)} B"
            : "unavailable";

    private static string FormatIdentifier(string? name, int? value) =>
        value is int id
            ? name is null
                ? $"{id.ToString(CultureInfo.InvariantCulture)} (unknown)"
                : $"{name} ({id.ToString(CultureInfo.InvariantCulture)})"
            : "unavailable";

    private static string FormatFieldValue(Fallout2PrototypeField field)
    {
        var width = Math.Clamp(field.SizeInBytes * 2, 2, 8);
        var hexValue = unchecked((uint)field.Value).ToString($"X{width}", CultureInfo.InvariantCulture);
        return $"{field.Value.ToString(CultureInfo.InvariantCulture)} (0x{hexValue})";
    }

    private static void AppendValue(StringBuilder report, string label, string? value) =>
        report.AppendLine($"{label}: {value ?? "unavailable"}");

    private static void AppendHexDump(StringBuilder report, ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            report.AppendLine("(no bytes available)");
            return;
        }

        for (var offset = 0; offset < bytes.Length; offset += BytesPerHexLine)
        {
            var count = Math.Min(BytesPerHexLine, bytes.Length - offset);
            report.Append(offset.ToString("X8", CultureInfo.InvariantCulture));
            report.Append("  ");
            for (var index = 0; index < count; index++)
            {
                if (index > 0)
                {
                    report.Append(' ');
                }

                report.Append(bytes[offset + index].ToString("X2", CultureInfo.InvariantCulture));
            }

            report.AppendLine();
        }
    }
}
