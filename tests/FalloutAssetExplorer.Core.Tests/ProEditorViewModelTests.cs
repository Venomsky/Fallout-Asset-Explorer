using System.Buffers.Binary;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using FalloutAssetExplorer.App;
using FalloutAssetExplorer.App.ViewModels;
using FalloutAssetExplorer.App.Views;
using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class ProEditorViewModelTests
{
    [Fact]
    public void EmptyWorkspace_ShowsGuidanceAndNoSessionActions()
    {
        var viewModel = new ProEditorViewModel();

        Assert.True(viewModel.HasNoInspection);
        Assert.Contains("Archive Browser", viewModel.EmptyStateMessage);
        Assert.Empty(viewModel.Fields);
        Assert.False(viewModel.IsDirty);
        Assert.False(viewModel.CanUndo);
        Assert.False(viewModel.CanRedo);
        Assert.Contains("No prototype", viewModel.SessionStatus);
    }

    [Fact]
    public void StandaloneSampleVerification_PresentsObservationsAndReportedMetadataSeparately()
    {
        var bytes = new byte[0x28];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0, sizeof(uint)), 0x00000002);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(0x20, sizeof(int)), 0);
        bytes[0x24] = 0x00;
        bytes[0x25] = 0x00;
        bytes[0x26] = 0x00;
        bytes[0x27] = 0x07;
        var viewModel = new ProEditorViewModel
        {
            SampleSourceReference = "reported source",
            SampleGameEditionVersion = "reported edition",
            SampleRecordIdentity = "proto/ITEMS/00000001.PRO",
            SampleAcquisitionMethod = "reported extraction",
            SampleExpectedPidHex = "00000002",
            SampleExpectedPidReference = "reported PID reference"
        };

        viewModel.VerifyStandaloneSample("00000002.PRO", bytes);

        Assert.True(viewModel.HasSampleVerification);
        Assert.Contains("PlausibleHeader", viewModel.SampleStructureDisplay);
        Assert.Contains("0x00000002", viewModel.SampleObservedDataDisplay);
        Assert.Contains("Raw bytes at 0x24 (candidate field; uninterpreted): 00000007", viewModel.SampleObservedDataDisplay);
        Assert.Contains("Big-endian numeric display at 0x24 (not a signedness claim): 7 (0x00000007)", viewModel.SampleObservedDataDisplay);
        Assert.Contains("MatchesReportedExpectedPid", viewModel.SampleObservedDataDisplay);
        Assert.Contains("Reported: Source URL/repository and immutable revision = reported source", viewModel.SampleProvenanceDisplay);
        Assert.Contains("Reported: Reported expected PID = 00000002", viewModel.SampleProvenanceDisplay);
        Assert.Contains("complete value domain", viewModel.SampleUnresolvedClaimsDisplay);
        Assert.Contains("round-trip behavior", viewModel.SampleUnresolvedClaimsDisplay);
        Assert.Contains("source file was not changed", viewModel.SampleStatusMessage);
    }

    [Theory]
    [InlineData(0x27, "short.PRO")]
    [InlineData(0x28, "rejected.DAT")]
    public void StandaloneSampleVerification_UsesNeutralMessageWhenCandidateBytesAreNotReported(
        int length,
        string fileName)
    {
        var bytes = new byte[length];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0, sizeof(uint)), 0x00000002);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(0x20, sizeof(int)), 0);
        var viewModel = new ProEditorViewModel();

        viewModel.VerifyStandaloneSample(fileName, bytes);

        Assert.Null(viewModel.SampleVerification?.MaterialCandidate);
        Assert.Contains(
            "Candidate bytes at 0x24 were not reported for this result.",
            viewModel.SampleObservedDataDisplay);
        Assert.DoesNotContain("fewer than 0x28 bytes", viewModel.SampleObservedDataDisplay);
    }

    [Fact]
    public void StandaloneSampleVerification_ClearsPreviousReportWhenInputsChangeOrSelectionIsCancelled()
    {
        var viewModel = new ProEditorViewModel
        {
            SampleSourceReference = "original source"
        };
        var bytes = new byte[0x28];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0, sizeof(uint)), 0x00000002);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(0x20, sizeof(int)), 0);
        viewModel.VerifyStandaloneSample("sample.PRO", bytes);

        Assert.True(viewModel.HasSampleVerification);
        Assert.Contains("Reported: Source URL/repository and immutable revision = original source", viewModel.SampleProvenanceDisplay);

        viewModel.SampleSourceReference = "updated source";

        Assert.False(viewModel.HasSampleVerification);
        Assert.Contains("cleared because provenance inputs changed", viewModel.SampleStatusMessage);
        Assert.DoesNotContain("original source", viewModel.SampleProvenanceDisplay);

        viewModel.VerifyStandaloneSample("sample.PRO", bytes);
        viewModel.ReportSampleVerificationCancelled();

        Assert.False(viewModel.HasSampleVerification);
        Assert.Contains("selection was cancelled", viewModel.SampleStatusMessage);
    }

    [Fact]
    public void CompleteInspection_PresentsSourceFieldsAndReadOnlySession()
    {
        var rawBytes = EncodeInt32(42);
        var inspection = CreateInspection(rawBytes);
        var viewModel = new ProEditorViewModel();

        viewModel.SetInspection(inspection);

        Assert.True(viewModel.HasInspection);
        Assert.Equal("master.dat", viewModel.ArchiveSourceDisplay);
        Assert.Equal("proto/items/synthetic.pro", viewModel.EntryPathDisplay);
        Assert.Equal("0x0000002A", viewModel.PrototypePidDisplay);
        Assert.Equal("Item (0)", viewModel.PidTypeDisplay);
        Assert.Equal("Weapon (3)", viewModel.SubtypeDisplay);
        Assert.Contains("not verified through a MAP PID or prototype list", viewModel.VerificationDisplay);
        Assert.Equal("Success", viewModel.OutcomeDisplay);
        Assert.Equal("Complete", viewModel.ReadCompletionDisplay);

        var field = Assert.Single(viewModel.Fields);
        Assert.Equal("synthetic_field", field.Name);
        Assert.Equal("ReadOnly", field.EditabilityDisplay);
        Assert.Equal("42", field.OriginalValueDisplay);
        Assert.Equal(field.OriginalValueDisplay, field.CurrentValueDisplay);
        Assert.Equal("Int32BigEndian", field.ValueKindDisplay);
        Assert.Equal("0x0 (4 bytes)", field.OffsetDisplay);
        Assert.Contains("Unverified", field.EvidenceDisplay);

        Assert.Contains("snapshot attached", viewModel.SessionStatus);
        Assert.Contains("The PRO Editor cannot save modified PRO records", viewModel.SessionStatus);
        Assert.Contains("Archive Browser raw-byte export is separate and preserves inspected source bytes", viewModel.SessionStatus);
        Assert.False(viewModel.IsDirty);
        Assert.Equal("No in-memory changes.", viewModel.IsDirtyDisplay);
        Assert.False(viewModel.CanUndo);
        Assert.False(viewModel.CanRedo);
        Assert.Equal(EncodeInt32(42), rawBytes);
    }

    [Fact]
    public void FieldPresentationUsesInterpretationHexAndFlagsMetadata()
    {
        const int fidValue = unchecked((int)0xF1234567);
        const int flagsValue = unchecked((int)0x80000001);
        var rawBytes = new byte[2 * sizeof(int) + sizeof(byte)];
        BinaryPrimitives.WriteInt32BigEndian(rawBytes.AsSpan(0, sizeof(int)), fidValue);
        BinaryPrimitives.WriteInt32BigEndian(rawBytes.AsSpan(sizeof(int), sizeof(int)), flagsValue);
        var inspection = CreateInspection(rawBytes) with
        {
            ItemFields =
            [
                new Fallout2PrototypeField(
                    "fid",
                    0,
                    sizeof(int),
                    fidValue,
                    DisplayAsHex: true,
                    Interpretation: "Synthetic interpretation; not a game-format assertion."),
                new Fallout2PrototypeField(
                    "flags",
                    sizeof(int),
                    sizeof(int),
                    flagsValue,
                    IsFlags: true),
                new Fallout2PrototypeField(
                    "zero_byte",
                    2 * sizeof(int),
                    sizeof(byte),
                    0,
                    DisplayAsHex: true,
                    StorageType: "uint8")
            ]
        };
        var viewModel = new ProEditorViewModel();

        viewModel.SetInspection(inspection);

        var fid = viewModel.Fields[0];
        Assert.Equal($"0xF1234567 (int32 BE: {fidValue})", fid.OriginalValueDisplay);
        Assert.True(fid.HasInterpretation);
        Assert.Equal("Synthetic interpretation; not a game-format assertion.", fid.InterpretationDisplay);
        Assert.Equal("ReadOnly", fid.EditabilityDisplay);

        var flags = viewModel.Fields[1];
        Assert.Equal($"0x80000001 (int32 BE: {flagsValue})", flags.OriginalValueDisplay);
        Assert.Contains("flags; bit meanings not decoded", flags.ValueKindDisplay);
        Assert.False(flags.HasInterpretation);
        Assert.Equal("ReadOnly", flags.EditabilityDisplay);
        Assert.False(viewModel.IsDirty);

        var zeroByte = viewModel.Fields[2];
        Assert.Equal("UInt8", zeroByte.ValueKindDisplay);
        Assert.Equal("0x00 (uint8: 0)", zeroByte.OriginalValueDisplay);
        Assert.Equal("ReadOnly", zeroByte.EditabilityDisplay);

        var refreshedFid = fid.WithCurrentValue(fid.CurrentNumericValue);
        Assert.True(refreshedFid.DisplayAsHex);
        Assert.Equal(fid.InterpretationDisplay, refreshedFid.InterpretationDisplay);
        Assert.Equal(fid.OriginalValueDisplay, refreshedFid.CurrentValueDisplay);

        var refreshedFlags = flags.WithCurrentValue(flags.CurrentNumericValue);
        Assert.True(refreshedFlags.IsFlags);
        Assert.Contains("flags; bit meanings not decoded", refreshedFlags.ValueKindDisplay);

        var refreshedByte = zeroByte.WithCurrentValue(1);
        Assert.True(refreshedByte.DisplayAsHex);
        Assert.Equal("0x01 (uint8: 1)", refreshedByte.CurrentValueDisplay);
    }

    [Fact]
    public void IncompleteInspection_PreservesPartialFieldAndRawBytesWithoutAttachingSession()
    {
        var rawBytes = EncodeInt32(17);
        var inspection = CreateInspection(rawBytes) with
        {
            Status = "Partial prototype data.",
            IsReadComplete = false,
            Outcome = PrototypeInspectionOutcome.Partial
        };
        var viewModel = new ProEditorViewModel();

        viewModel.SetInspection(inspection);

        Assert.Equal("Partial prototype data.", viewModel.StatusMessage);
        Assert.Equal("Partial", viewModel.OutcomeDisplay);
        Assert.Equal("Partial", viewModel.ReadCompletionDisplay);
        Assert.Single(viewModel.Fields);
        Assert.Contains("not read completely", viewModel.SessionStatus);
        Assert.False(viewModel.IsDirty);
        Assert.False(viewModel.CanUndo);
        Assert.False(viewModel.CanRedo);
        Assert.NotEmpty(viewModel.RawByteLines);
        Assert.Equal(EncodeInt32(17), rawBytes);
    }

    [Fact]
    public void UnsupportedInspection_ShowsDiagnosticsAndRawBytesWithoutInventingFields()
    {
        var inspection = new Fallout2StandalonePrototypeInspection(
            "proto/unknown/synthetic.pro",
            "Unsupported PID type.",
            FalloutGame.Fallout2,
            ArchiveFormat.Dat2,
            null,
            9,
            88,
            2,
            new byte[] { 0xA1, 0xB2 })
        {
            ArchivePath = "master.dat",
            PidTypeName = null,
            SubtypeName = null,
            HasSupportedSubtype = false,
            IsReadComplete = true,
            Outcome = PrototypeInspectionOutcome.Partial
        };
        var viewModel = new ProEditorViewModel();

        viewModel.SetInspection(inspection);

        Assert.Equal("Unsupported (9)", viewModel.PidTypeDisplay);
        Assert.Equal("Unsupported (88)", viewModel.SubtypeDisplay);
        Assert.Equal("Partial", viewModel.OutcomeDisplay);
        Assert.Empty(viewModel.Fields);
        Assert.Contains("unsupported", viewModel.SessionStatus);
        Assert.Contains("A1 B2", Assert.Single(viewModel.RawByteLines));
    }

    [Fact]
    public void UnsupportedGame_PreservesDiagnosticsAndRawBytesWithoutAttachingSession()
    {
        var rawBytes = EncodeInt32(17);
        var inspection = CreateInspection(rawBytes) with
        {
            Game = FalloutGame.Fallout1,
            Status = "Unsupported game inspection."
        };
        var viewModel = new ProEditorViewModel();

        viewModel.SetInspection(inspection);

        Assert.Equal("Fallout1", viewModel.GameDisplay);
        Assert.Equal("Unsupported game inspection.", viewModel.StatusMessage);
        Assert.Equal("Complete", viewModel.ReadCompletionDisplay);
        Assert.Contains("Fallout 2 DAT2 inspections", viewModel.SessionStatus);
        Assert.False(viewModel.IsDirty);
        Assert.False(viewModel.CanUndo);
        Assert.False(viewModel.CanRedo);
        Assert.Equal("ReadOnly", Assert.Single(viewModel.Fields).EditabilityDisplay);
        Assert.Contains("00 00 00 11", Assert.Single(viewModel.RawByteLines));
        Assert.Equal(EncodeInt32(17), rawBytes);
    }

    [Fact]
    public void UnsupportedFormat_PreservesDiagnosticsAndRawBytesWithoutAttachingSession()
    {
        var rawBytes = EncodeInt32(23);
        var inspection = CreateInspection(rawBytes) with
        {
            Format = ArchiveFormat.Dat1,
            Status = "Unsupported format inspection."
        };
        var viewModel = new ProEditorViewModel();

        viewModel.SetInspection(inspection);

        Assert.Equal("Dat1", viewModel.FormatDisplay);
        Assert.Equal("Unsupported format inspection.", viewModel.StatusMessage);
        Assert.Equal("Complete", viewModel.ReadCompletionDisplay);
        Assert.Contains("Fallout 2 DAT2 inspections", viewModel.SessionStatus);
        Assert.False(viewModel.IsDirty);
        Assert.False(viewModel.CanUndo);
        Assert.False(viewModel.CanRedo);
        Assert.Equal("ReadOnly", Assert.Single(viewModel.Fields).EditabilityDisplay);
        Assert.Contains("00 00 00 17", Assert.Single(viewModel.RawByteLines));
        Assert.Equal(EncodeInt32(23), rawBytes);
    }

    [Fact]
    public void RawBytePreview_IsBoundedAndStatesWhenBytesAreOmitted()
    {
        var rawBytes = new byte[ProEditorViewModelTestsRawLimit + 16];
        var viewModel = new ProEditorViewModel();

        viewModel.SetInspection(CreateInspection(rawBytes));

        Assert.Equal(256, viewModel.RawByteLines.Count);
        Assert.Contains("16 additional bytes omitted", viewModel.RawByteTruncationNotice);
    }

    [Fact]
    public void MainWindow_RegistersReadOnlyProWorkspaceWithDisabledUndoRedo()
    {
        AvaloniaTestEnvironment.EnsureInitialized();
        AvaloniaTestEnvironment.RunOnUiThread(() =>
        {
            using var archiveBrowser = new ArchiveBrowserViewModel();
            archiveBrowser.ProEditor.SetInspection(CreateInspection(EncodeInt32(42)));
            archiveBrowser.SelectedWorkspaceIndex = ArchiveBrowserViewModel.ProEditorWorkspaceIndex;
            var window = new MainWindow { DataContext = archiveBrowser };
            try
            {
                window.Show();
                var tabs = Assert.IsType<TabControl>(window.FindControl<TabControl>("WorkspaceTabs"));
                var editorTab = Assert.Single(
                    tabs.Items.OfType<TabItem>(),
                    item => Equals(item.Header, "PRO Editor"));
                Assert.Equal(
                    ArchiveBrowserViewModel.ProEditorWorkspaceIndex,
                    tabs.Items.IndexOf(editorTab));

                var editor = Assert.IsType<ProEditorView>(
                    window.FindControl<ProEditorView>("ProEditorControl"));
                Assert.Same(archiveBrowser.ProEditor, editor.DataContext);
                Assert.False(Assert.IsType<Button>(
                    editor.FindControl<Button>("ProEditorUndoButton")).IsEnabled);
                Assert.False(Assert.IsType<Button>(
                    editor.FindControl<Button>("ProEditorRedoButton")).IsEnabled);
                Assert.NotNull(editor.FindControl<ItemsControl>("ProEditorRawBytes"));
                Assert.NotNull(editor.FindControl<Expander>("StandaloneProSampleVerificationExpander"));
                Assert.NotNull(editor.FindControl<Button>("VerifyStandaloneProSampleButton"));
                Assert.Single(archiveBrowser.ProEditor.Fields);
                Assert.Equal("ReadOnly", Assert.Single(archiveBrowser.ProEditor.Fields).EditabilityDisplay);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ProEditorView_ExposesOnlyMetadataInputsAndNoSaveCommand()
    {
        AvaloniaTestEnvironment.EnsureInitialized();
        AvaloniaTestEnvironment.RunOnUiThread(() =>
        {
            var viewModel = new ProEditorViewModel();
            viewModel.SetInspection(CreateInspection(EncodeInt32(42)));
            var editor = new ProEditorView { DataContext = viewModel };
            var window = new Window
            {
                Content = editor,
                Width = 900,
                Height = 700
            };

            try
            {
                window.Show();
                Assert.Single(viewModel.Fields);
                Assert.Equal("ReadOnly", Assert.Single(viewModel.Fields).EditabilityDisplay);
                Assert.IsType<Expander>(
                    editor.FindControl<Expander>("StandaloneProSampleVerificationExpander")).IsExpanded = true;

                var metadataInputNames = new[]
                {
                    "ProSampleSourceReference",
                    "ProSampleGameEdition",
                    "ProSampleRecordIdentity",
                    "ProSampleAcquisitionMethod",
                    "ProSampleExpectedPid",
                    "ProSampleExpectedPidReference"
                };
                var textBoxNames = editor.GetLogicalDescendants()
                    .OfType<TextBox>()
                    .Select(textBox => textBox.Name)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray();
                Assert.Equal(
                    metadataInputNames.OrderBy(name => name, StringComparer.Ordinal),
                    textBoxNames);

                var buttonNames = editor.GetLogicalDescendants()
                    .OfType<Button>()
                    .Select(button => button.Name)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray();
                Assert.Equal(
                    new[]
                    {
                        "ProEditorRedoButton",
                        "ProEditorUndoButton",
                        "VerifyStandaloneProSampleButton"
                    },
                    buttonNames);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private const int ProEditorViewModelTestsRawLimit = 4096;

    private static Fallout2StandalonePrototypeInspection CreateInspection(byte[] rawBytes)
    {
        var value = BinaryPrimitives.ReadInt32BigEndian(rawBytes.AsSpan(0, sizeof(int)));
        return new Fallout2StandalonePrototypeInspection(
            "proto/items/synthetic.pro",
            "Synthetic standalone inspection.",
            FalloutGame.Fallout2,
            ArchiveFormat.Dat2,
            value,
            0,
            3,
            rawBytes.Length,
            rawBytes)
        {
            ArchivePath = "master.dat",
            PidTypeName = "Item",
            SubtypeName = "Weapon",
            HasSupportedSubtype = true,
            IsReadComplete = true,
            Outcome = PrototypeInspectionOutcome.Success,
            ItemFields =
            [
                new Fallout2PrototypeField(
                    "synthetic_field",
                    0,
                    sizeof(int),
                    value,
                    Interpretation: "Synthetic test field; not a game-format assertion.")
            ]
        };
    }

    private static byte[] EncodeInt32(int value)
    {
        var bytes = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        return bytes;
    }
}
