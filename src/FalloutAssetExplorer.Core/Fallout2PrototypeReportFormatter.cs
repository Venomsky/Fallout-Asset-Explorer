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
        report.AppendLine("Fallout Asset Explorer — raport inspekcji PRO");
        report.AppendLine($"Wynik parsowania: {GetOutcomeText(inspection.Outcome)}");
        report.AppendLine();

        report.AppendLine("[Wpis archiwum]");
        AppendValue(report, "Archiwum", inspection.ArchivePath);
        AppendValue(report, "Typ gry", inspection.Game?.ToString());
        AppendValue(report, "Format archiwum", inspection.Format?.ToString());
        AppendValue(report, "Ścieżka wpisu", inspection.EntryPath);
        AppendValue(report, "Lokalizacja wpisu", inspection.ArchiveLocation);
        AppendValue(report, "Rozmiar skompresowany", FormatSize(inspection.CompressedSize));
        AppendValue(report, "Rozmiar deklarowany", FormatSize(inspection.FileSize));
        AppendValue(report, "Rozmiar faktycznie odczytany", FormatSize(inspection.ActualSize));
        AppendValue(report, "Odczyt kompletny", inspection.IsReadComplete ? "Tak" : "Nie");
        AppendValue(report, "Bajty zachowane", inspection.RawBytes.Length.ToString(CultureInfo.InvariantCulture));
        report.AppendLine();

        report.AppendLine("[Pochodzenie i weryfikacja]");
        report.AppendLine("Pochodzenie: wpis PRO wybrany bezpośrednio z archiwum.");
        report.AppendLine("Weryfikacja przez MAP PID/listę prototypów: Nie.");
        report.AppendLine();

        report.AppendLine("[Identyfikacja prototypu]");
        AppendValue(report, "PID", inspection.PrototypePid is int pid
            ? $"0x{unchecked((uint)pid):X8}"
            : "niedostępny");
        AppendValue(report, "Typ PID", FormatIdentifier(inspection.PidTypeName, inspection.PidType));
        AppendValue(report, "Subtype", FormatIdentifier(inspection.SubtypeName, inspection.Subtype));
        report.AppendLine();

        report.AppendLine("[Pola sparsowane]");
        var fields = inspection.PidType switch
        {
            0 => inspection.ItemFields,
            2 => inspection.SceneryFields,
            _ => []
        };
        if (fields.Count == 0)
        {
            report.AppendLine("(brak sparsowanych pól)");
        }
        else
        {
            foreach (var field in fields.OrderBy(field => field.Offset).ThenBy(field => field.Name, StringComparer.Ordinal))
            {
                var value = FormatFieldValue(field);
                var interpretation = string.IsNullOrWhiteSpace(field.Interpretation)
                    ? string.Empty
                    : $" | interpretacja: {field.Interpretation}";
                report.AppendLine(
                    $"offset 0x{field.Offset:X4} | {field.Name} | wartość: {value} "
                    + $"| typ: {field.StorageType} | rozmiar: {field.SizeInBytes} B{interpretation}");
            }
        }
        report.AppendLine();

        report.AppendLine("[Diagnostyka parsera]");
        report.AppendLine($"Wynik: {GetOutcomeText(inspection.Outcome)}");
        report.AppendLine($"Komunikat parsera (zachowany bez zmian): {inspection.Status}");
        report.AppendLine();

        report.AppendLine("[Pełny zrzut hex dostępnych bajtów]");
        AppendHexDump(report, inspection.RawBytes.Span);

        return report.ToString();
    }

    private static string GetOutcomeText(PrototypeInspectionOutcome outcome) =>
        outcome switch
        {
            PrototypeInspectionOutcome.Success => "Sukces",
            PrototypeInspectionOutcome.Partial => "Częściowy sukces",
            PrototypeInspectionOutcome.Error => "Błąd",
            _ => "Nieznany"
        };

    private static string FormatSize(long? size) =>
        size is long value
            ? $"{value.ToString("N0", CultureInfo.InvariantCulture)} B"
            : "niedostępny";

    private static string FormatIdentifier(string? name, int? value) =>
        value is int id
            ? name is null
                ? $"{id.ToString(CultureInfo.InvariantCulture)} (nieznany)"
                : $"{name} ({id.ToString(CultureInfo.InvariantCulture)})"
            : "niedostępny";

    private static string FormatFieldValue(Fallout2PrototypeField field)
    {
        var width = Math.Clamp(field.SizeInBytes * 2, 2, 8);
        var hexValue = unchecked((uint)field.Value).ToString($"X{width}", CultureInfo.InvariantCulture);
        return $"{field.Value.ToString(CultureInfo.InvariantCulture)} (0x{hexValue})";
    }

    private static void AppendValue(StringBuilder report, string label, string? value) =>
        report.AppendLine($"{label}: {value ?? "niedostępny"}");

    private static void AppendHexDump(StringBuilder report, ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            report.AppendLine("(brak dostępnych bajtów)");
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
