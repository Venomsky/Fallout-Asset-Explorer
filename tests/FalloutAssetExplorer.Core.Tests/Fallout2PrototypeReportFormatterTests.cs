using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class Fallout2PrototypeReportFormatterTests
{
    [Fact]
    public void Format_IncludesInspectionMetadataFieldsDiagnosticsAndCompleteHexDump()
    {
        var bytes = Convert.FromHexString(
            "020002F5000127B4020002FF00000000000000002000001008002000FFFFFFFF000000050000000730CCCCCCCC");
        var inspection = new Fallout2StandalonePrototypeInspection(
            "proto/SCENERY/00000758.pro",
            "Prototype PID type and subtype recognized.",
            FalloutGame.Fallout2,
            ArchiveFormat.Dat2,
            unchecked((int)0x020002F5),
            2,
            5,
            46,
            bytes)
        {
            ArchivePath = @"D:\Fallout2\master.dat",
            ArchiveLocation = "proto/SCENERY/00000758.pro",
            CompressedSize = 31,
            ActualSize = 45,
            IsReadComplete = false,
            Outcome = PrototypeInspectionOutcome.Partial,
            PidTypeName = "Scenery",
            SubtypeName = "Generic",
            SceneryFields =
            [
                new Fallout2PrototypeField(
                    "field_34",
                    0x28,
                    1,
                    0x30,
                    Interpretation: "nieinterpretowane",
                    StorageType: "uint8"),
                new Fallout2PrototypeField(
                    "generic.field_0",
                    0x29,
                    4,
                    unchecked((int)0xCCCCCCCC),
                    Interpretation: "znaczenie nieustalone",
                    StorageType: "int32 BE")
            ]
        };

        var report = Fallout2PrototypeReportFormatter.Format(inspection);

        Assert.Contains("Wynik parsowania: Częściowy sukces", report);
        Assert.Contains(@"Archiwum: D:\Fallout2\master.dat", report);
        Assert.Contains("Rozmiar deklarowany: 46 B", report);
        Assert.Contains("Rozmiar faktycznie odczytany: 45 B", report);
        Assert.Contains("Odczyt kompletny: Nie", report);
        Assert.Contains("Weryfikacja przez MAP PID/listę prototypów: Nie.", report);
        Assert.Contains("PID: 0x020002F5", report);
        Assert.Contains("Typ PID: Scenery (2)", report);
        Assert.Contains("Subtype: Generic (5)", report);
        Assert.Contains("offset 0x0028 | field_34 | wartość: 48 (0x30) | typ: uint8 | rozmiar: 1 B", report);
        Assert.Contains("offset 0x0029 | generic.field_0 | wartość: -858993460 (0xCCCCCCCC) | typ: int32 BE | rozmiar: 4 B", report);
        Assert.Contains("interpretacja: znaczenie nieustalone", report);
        Assert.Contains("Komunikat parsera (zachowany bez zmian): Prototype PID type and subtype recognized.", report);
        Assert.Contains("00000000  02 00 02 F5 00 01 27 B4 02 00 02 FF 00 00 00 00", report);
        Assert.Contains("00000010  00 00 00 00 20 00 00 10 08 00 20 00 FF FF FF FF", report);
        Assert.Contains("00000020  00 00 00 05 00 00 00 07 30 CC CC CC CC", report);
        Assert.Equal(3, report.Split("000000", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void Format_ReportsUnavailableDataWithoutInventingValues()
    {
        var inspection = new Fallout2StandalonePrototypeInspection(
            "proto/unknown.pro",
            "The selected archive is unavailable.",
            null,
            null,
            null,
            null,
            null,
            12,
            ReadOnlyMemory<byte>.Empty);

        var report = Fallout2PrototypeReportFormatter.Format(inspection);

        Assert.Contains("Wynik parsowania: Błąd", report);
        Assert.Contains("PID: niedostępny", report);
        Assert.Contains("Typ PID: niedostępny", report);
        Assert.Contains("Subtype: niedostępny", report);
        Assert.Contains("(brak sparsowanych pól)", report);
        Assert.Contains("(brak dostępnych bajtów)", report);
    }
}
