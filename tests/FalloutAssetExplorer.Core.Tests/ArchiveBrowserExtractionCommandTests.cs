using System.Buffers.Binary;
using System.ComponentModel;
using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using FalloutAssetExplorer.App.ViewModels;
using FalloutAssetExplorer.App.Views;
using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class ArchiveBrowserExtractionCommandTests
{
    [Fact]
    public async Task ExtractionAvailability_TracksArchiveSelectionAndFiltering()
    {
        using var firstArchive = new TemporaryArchive(CreateDat1Archive("ART/CRITTERS/HAPOWRAG.FRM"));
        using var emptyArchive = new TemporaryArchive(CreateEmptyDat1Archive());
        using var viewModel = new ArchiveBrowserViewModel();
        var notifiedProperties = new List<string?>();
        viewModel.PropertyChanged += OnPropertyChanged;

        Assert.True(viewModel.CanOpenArchive);
        Assert.False(viewModel.CanExtractSelected);
        Assert.False(viewModel.CanExtractVisible);
        Assert.False(viewModel.CanExtractAll);

        await viewModel.LoadArchiveAsync(firstArchive.Path);

        Assert.True(viewModel.CanOpenArchive);
        Assert.True(viewModel.CanExtractAll);
        Assert.True(viewModel.CanExtractVisible);
        Assert.False(viewModel.CanExtractSelected);
        Assert.Contains(nameof(viewModel.CanExtractAll), notifiedProperties);

        notifiedProperties.Clear();
        viewModel.UpdateSelectionCount(1);

        Assert.True(viewModel.CanExtractSelected);
        Assert.Contains(nameof(viewModel.CanExtractSelected), notifiedProperties);

        notifiedProperties.Clear();
        viewModel.SelectedExtension = ".MSG";

        Assert.False(viewModel.CanExtractVisible);
        Assert.True(viewModel.CanExtractAll);
        Assert.Contains(nameof(viewModel.CanExtractVisible), notifiedProperties);

        viewModel.SelectedExtension = ArchiveEntryFilter.AllExtensions;
        Assert.True(viewModel.CanExtractVisible);

        await viewModel.LoadArchiveAsync(emptyArchive.Path);

        Assert.False(viewModel.CanExtractSelected);
        Assert.False(viewModel.CanExtractVisible);
        Assert.False(viewModel.CanExtractAll);
        Assert.True(viewModel.CanOpenArchive);

        void OnPropertyChanged(object? sender, PropertyChangedEventArgs args) =>
            notifiedProperties.Add(args.PropertyName);
    }

    [Fact]
    public async Task CanOpenArchive_IsDisabledDuringExtractionAndRestoredAfterSuccess()
    {
        using var archive = new ControlledArchive();
        var entry = archive.AddEntry("assets/item.bin", [1, 2, 3]);
        var readGate = archive.BlockEntry(entry.Path);
        using var viewModel = new ArchiveBrowserViewModel();
        SetPrivateField(viewModel, "_archive", archive);
        viewModel.SetEntries(archive.BrowserEntries);
        var notifiedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, args) => notifiedProperties.Add(args.PropertyName);
        var destinationDirectory = Path.Combine(
            Path.GetTempPath(),
            $"fae-extraction-state-{Guid.NewGuid():N}");
        Task? extraction = null;

        try
        {
            Assert.True(viewModel.CanOpenArchive);

            extraction = viewModel.ExtractEntriesAsync(
                [entry],
                destinationDirectory,
                "selected entries");
            await readGate.WaitStartedAsync();

            Assert.True(viewModel.IsExtracting);
            Assert.False(viewModel.CanOpenArchive);
            Assert.Contains(nameof(viewModel.CanOpenArchive), notifiedProperties);

            readGate.Release();
            await extraction;

            Assert.False(viewModel.IsExtracting);
            Assert.True(viewModel.CanOpenArchive);
            Assert.True(notifiedProperties.Count(
                propertyName => propertyName == nameof(viewModel.CanOpenArchive)) >= 2);
            Assert.StartsWith("Completed: 1 extracted", viewModel.StatusMessage);
        }
        finally
        {
            readGate.Release();
            if (extraction is not null)
            {
                await extraction;
            }

            if (Directory.Exists(destinationDirectory))
            {
                Directory.Delete(destinationDirectory, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("failure")]
    [InlineData("cancellation")]
    [InlineData("exception")]
    public async Task CanOpenArchive_IsRestoredAfterExtractionFailureCancellationOrException(string outcome)
    {
        using var archive = new ControlledArchive();
        var entry = archive.AddEntry("assets/item.bin", [1, 2, 3]);
        var readGate = outcome == "exception"
            ? null
            : archive.BlockEntry(entry.Path, failWithIo: outcome == "failure");
        using var viewModel = new ArchiveBrowserViewModel();
        using var cancellation = new CancellationTokenSource();
        SetPrivateField(viewModel, "_archive", archive);
        viewModel.SetEntries(archive.BrowserEntries);
        var notifiedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, args) => notifiedProperties.Add(args.PropertyName);
        var destinationDirectory = outcome == "exception"
            ? "invalid\0destination"
            : Path.Combine(
                Path.GetTempPath(),
                $"fae-extraction-state-{Guid.NewGuid():N}");
        Task? extraction = null;

        try
        {
            extraction = viewModel.ExtractEntriesAsync(
                [entry],
                destinationDirectory,
                "selected entries",
                cancellation.Token);

            if (readGate is not null)
            {
                await readGate.WaitStartedAsync();
                Assert.False(viewModel.CanOpenArchive);
                if (outcome == "cancellation")
                {
                    cancellation.Cancel();
                }

                readGate.Release();
            }

            await extraction;

            Assert.False(viewModel.IsExtracting);
            Assert.True(viewModel.CanOpenArchive);
            Assert.True(notifiedProperties.Count(
                propertyName => propertyName == nameof(viewModel.CanOpenArchive)) >= 2);
            Assert.Contains(
                outcome switch
                {
                    "failure" => "Completed with warnings:",
                    "cancellation" => "Cancelled:",
                    _ => "Extraction of selected entries failed:"
                },
                viewModel.StatusMessage);
        }
        finally
        {
            readGate?.Release();
            if (extraction is not null)
            {
                await extraction;
            }

            if (Directory.Exists(destinationDirectory))
            {
                Directory.Delete(destinationDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void IsFilterResultEmpty_TracksFilterMatchesAndRestoredResults()
    {
        using var archive = new ControlledArchive();
        archive.AddEntry("ART/CRITTERS/HAPOWRAG.FRM", [1]);
        archive.AddEntry("TEXT/ENGLISH/DIALOG/GECKO.MSG", [2]);
        using var viewModel = new ArchiveBrowserViewModel();
        SetPrivateField(viewModel, "_archive", archive);
        viewModel.SetEntries(archive.BrowserEntries);
        var notifiedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, args) => notifiedProperties.Add(args.PropertyName);

        Assert.False(viewModel.IsFilterResultEmpty);

        viewModel.SelectedExtension = ".FRM";
        Assert.False(viewModel.IsFilterResultEmpty);
        Assert.Single(viewModel.VisibleEntries);

        viewModel.SearchText = "no-such-resource";
        Assert.True(viewModel.IsFilterResultEmpty);
        Assert.Contains(nameof(viewModel.IsFilterResultEmpty), notifiedProperties);

        viewModel.SearchText = "HAPOWRAG";
        Assert.False(viewModel.IsFilterResultEmpty);
        Assert.Single(viewModel.VisibleEntries);

        viewModel.SearchText = string.Empty;
        viewModel.SelectedExtension = ".MSG";
        Assert.False(viewModel.IsFilterResultEmpty);
        Assert.Single(viewModel.VisibleEntries);
    }

    [Fact]
    public void IsFilterResultEmpty_IsFalseBeforeArchiveLoad()
    {
        using var viewModel = new ArchiveBrowserViewModel();

        Assert.False(viewModel.IsFilterResultEmpty);
    }

    [Fact]
    public void IsFilterResultEmpty_IsFalseForLoadedEmptyArchive()
    {
        using var archive = new ControlledArchive();
        using var viewModel = new ArchiveBrowserViewModel();
        SetPrivateField(viewModel, "_archive", archive);
        viewModel.SetEntries([]);

        Assert.False(viewModel.IsFilterResultEmpty);
    }

    [Fact]
    public async Task PreviewSelectedEntryAsync_LeavesUnsupportedEntriesBrowsableAndExtractable()
    {
        AvaloniaTestEnvironment.EnsureInitialized();
        using var archiveFile = new TemporaryArchive(CreateDat1Archive("ART/PROTO/TEST.LST"));
        using var viewModel = new ArchiveBrowserViewModel();

        await viewModel.LoadArchiveAsync(archiveFile.Path);
        viewModel.FrmPreview.SetPreview(
            new FrmDocument(
                4,
                10,
                0,
                1,
                [new FrmDirection(0, "NE", 0, 0, [new FrmFrame(1, 1, 0, 0, [1])])]),
            FalloutPalette.Parse(new byte[FalloutPalette.MinimalFileSize]),
            "ART/CRITTERS/STALE.FRM");
        Assert.NotNull(viewModel.FrmPreview.PreviewBitmap);
        viewModel.SelectedEntry = Assert.Single(viewModel.Entries);
        await viewModel.PreviewSelectedEntryAsync();

        Assert.Equal(
            $"Only '{FalloutScriptListResolver.ScriptListPath}' is supported by the script-list resolver.",
            viewModel.PreviewMessage);
        Assert.True(viewModel.IsScriptListInspector);
        Assert.Null(viewModel.FrmPreview.PreviewBitmap);
        Assert.False(viewModel.FrmPreview.HasPreview);
        Assert.Single(viewModel.VisibleEntries);
        Assert.True(viewModel.CanExtractAll);
    }

    [Fact]
    public async Task PreviewSelectedEntryAsync_ParsesMsgEntriesFromDat2Archive()
    {
        using var archive = new ControlledArchive();
        var msgBytes = "{5}{}{first}{5}{voice}{second}"u8.ToArray();
        var entry = archive.AddEntry("text/english/dialog/sample.MSG", msgBytes);
        using var viewModel = new ArchiveBrowserViewModel();
        SetPrivateField(viewModel, "_archive", archive);
        viewModel.SetEntries(archive.BrowserEntries);
        viewModel.SelectedExtension = ".MSG";
        viewModel.SelectedEntry = entry;

        await viewModel.PreviewSelectedEntryAsync();

        var result = Assert.IsType<FalloutMsgParseResult>(viewModel.MsgParseResult);
        Assert.Equal(1, archive.CopyCount);
        Assert.Contains(".MSG", viewModel.ExtensionOptions);
        Assert.Single(viewModel.VisibleEntries);
        Assert.Equal("MSG Editor", viewModel.PreviewTitle);
        Assert.Equal(ArchiveBrowserViewModel.MsgEditorWorkspaceIndex, viewModel.SelectedWorkspaceIndex);
        Assert.True(viewModel.IsMsgInspector);
        Assert.False(viewModel.IsProInspector);
        Assert.False(viewModel.IsMapInspector);
        Assert.Equal(entry.Path, viewModel.PreviewMessage);
        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { 5, 5 }, result.Entries.Select(message => message.Id));
        Assert.Equal("first"u8.ToArray(), result.Entries[0].Text.ToArray());
        Assert.Equal("voice"u8.ToArray(), result.Entries[1].Audio.ToArray());
        Assert.Contains("2 entries", viewModel.MsgStatus);
        Assert.Contains("Text preview uses the selected encoding", viewModel.MsgDetails);
    }

    [Fact]
    public void MsgFileExplorer_ListsAndFiltersArchiveRelativePathsWithoutMergingSameNames()
    {
        using var archive = new ControlledArchive();
        archive.AddEntry("dialog/english/greeting.msg", "{5}{}{english}"u8.ToArray());
        archive.AddEntry("dialog/spanish/greeting.MSG", "{6}{}{spanish}"u8.ToArray());
        archive.AddEntry("dialog/spanish/readme.txt", "not a message"u8.ToArray());
        using var viewModel = new ArchiveBrowserViewModel();
        SetPrivateField(viewModel, "_archive", archive);
        SetPrivateField(viewModel, "_archivePath", archive.Path);

        viewModel.SetEntries(archive.BrowserEntries);

        Assert.Equal(2, viewModel.MsgArchiveFiles.Count);
        Assert.Equal(
            new[] { "dialog/english/greeting.msg", "dialog/spanish/greeting.MSG" },
            viewModel.MsgArchiveFiles.Select(file => file.ArchiveRelativePath));
        Assert.Equal(new[] { "greeting.msg", "greeting.MSG" }, viewModel.MsgArchiveFiles.Select(file => file.FileName));
        Assert.Equal(2, viewModel.VisibleMsgFiles.Count);

        viewModel.MsgFileSearchText = "spanish";

        var filtered = Assert.Single(viewModel.VisibleMsgFiles);
        Assert.Equal("dialog/spanish/greeting.MSG", filtered.ArchiveRelativePath);
        Assert.Contains("1 of 2", viewModel.MsgFileBrowserStatus);

        viewModel.MsgFileSearchText = "missing";

        Assert.Empty(viewModel.VisibleMsgFiles);
        Assert.Contains("No MSG files match", viewModel.MsgFileBrowserStatus);
    }

    [Fact]
    public void MsgFileExplorer_ReportsUnavailableAndEmptyArchiveStates()
    {
        using var unavailableViewModel = new ArchiveBrowserViewModel();
        Assert.Contains("Open a supported Fallout DAT archive", unavailableViewModel.MsgFileBrowserStatus);

        using var archive = new ControlledArchive();
        using var emptyViewModel = new ArchiveBrowserViewModel();
        SetPrivateField(emptyViewModel, "_archive", archive);
        emptyViewModel.SetEntries([]);

        Assert.Contains("no entries with a .MSG extension", emptyViewModel.MsgFileBrowserStatus);
    }

    [Fact]
    public async Task MsgFileExplorer_SwitchingFilesRefreshesOccurrencesSnippetsAndPreviewWithoutChangingArchiveBytes()
    {
        using var archive = new ControlledArchive();
        var firstBytes = "{5}{}{first text}{5}{}{second text}"u8.ToArray();
        var secondBytes = "{8}{}{replacement file text}"u8.ToArray();
        var firstEntry = archive.AddEntry("dialog/english/first.msg", firstBytes);
        var secondEntry = archive.AddEntry("dialog/english/second.msg", secondBytes);
        using var viewModel = new ArchiveBrowserViewModel();
        SetPrivateField(viewModel, "_archive", archive);
        SetPrivateField(viewModel, "_archivePath", archive.Path);
        viewModel.SetEntries(archive.BrowserEntries);
        viewModel.SelectedEntry = firstEntry;

        await viewModel.PreviewSelectedEntryAsync();

        Assert.Equal(new[] { 5, 5 }, viewModel.MsgInspector.VisibleMessages.Select(message => message.Id));
        Assert.Equal(
            new[] { "first text", "second text" },
            viewModel.MsgInspector.VisibleMessages.Select(message => message.TextSnippet));
        var firstReadCount = archive.CopyCount;
        await viewModel.PreviewSelectedEntryAsync();
        Assert.Equal(firstReadCount, archive.CopyCount);
        viewModel.MsgInspector.SelectedMessage = viewModel.MsgInspector.VisibleMessages[1];
        Assert.Equal("second text", viewModel.MsgInspector.TextPreview);

        var nextFile = viewModel.MsgArchiveFiles.Single(file => ReferenceEquals(file.Entry, secondEntry));
        await viewModel.SelectMsgFileAsync(nextFile);

        Assert.Same(secondEntry, viewModel.SelectedEntry);
        Assert.Same(nextFile, viewModel.SelectedMsgFile);
        Assert.True(viewModel.MsgParseResult?.IsSuccess);
        Assert.Equal(archive.Path, viewModel.MsgInspector.OriginalSourcePath);
        Assert.Contains(secondEntry.Path, viewModel.MsgDetails);
        Assert.Equal(new[] { 8 }, viewModel.MsgInspector.VisibleMessages.Select(message => message.Id));
        Assert.Equal("replacement file text", viewModel.MsgInspector.VisibleMessages[0].TextSnippet);
        viewModel.MsgInspector.SelectedMessage = viewModel.MsgInspector.VisibleMessages[0];
        Assert.Equal("replacement file text", viewModel.MsgInspector.TextPreview);

        viewModel.MsgInspector.BeginEdit();
        viewModel.MsgInspector.DraftText = "edited in memory";
        viewModel.MsgInspector.ApplyEdit();
        Assert.True(viewModel.MsgInspector.HasUnsavedOutput);

        using var storedBytes = new MemoryStream();
        await archive.CopyEntryToAsync(secondEntry, storedBytes);
        Assert.Equal(secondBytes, storedBytes.ToArray());
        Assert.Equal(secondBytes, viewModel.MsgInspector.ParseResult!.SourceSnapshot!.ToArray());
    }

    [Fact]
    public async Task MsgFileExplorer_MalformedCandidateDoesNotPreventOpeningAnotherFile()
    {
        using var archive = new ControlledArchive();
        var malformed = archive.AddEntry("dialog/broken.msg", "{1}{}{truncated"u8.ToArray());
        var valid = archive.AddEntry("dialog/valid.msg", "{2}{}{valid}"u8.ToArray());
        using var viewModel = new ArchiveBrowserViewModel();
        SetPrivateField(viewModel, "_archive", archive);
        viewModel.SetEntries(archive.BrowserEntries);

        await viewModel.SelectMsgFileAsync(
            viewModel.MsgArchiveFiles.Single(file => ReferenceEquals(file.Entry, malformed)));

        Assert.False(viewModel.MsgParseResult!.IsSuccess);
        Assert.Contains("Parsing failed", viewModel.MsgStatus);
        Assert.Equal(2, viewModel.MsgArchiveFiles.Count);

        await viewModel.SelectMsgFileAsync(
            viewModel.MsgArchiveFiles.Single(file => ReferenceEquals(file.Entry, valid)));

        Assert.True(viewModel.MsgParseResult!.IsSuccess);
        Assert.Equal(2, Assert.Single(viewModel.MsgInspector.VisibleMessages).Id);
    }

    [Fact]
    public async Task MsgFileExplorer_LoadsCandidateAlreadySelectedWithoutAParsedResult()
    {
        using var archive = new ControlledArchive();
        var entry = archive.AddEntry("dialog/preselected.msg", "{3}{}{loaded}"u8.ToArray());
        using var viewModel = new ArchiveBrowserViewModel();
        SetPrivateField(viewModel, "_archive", archive);
        viewModel.SetEntries(archive.BrowserEntries);
        viewModel.SelectedEntry = entry;
        var file = Assert.Single(viewModel.MsgArchiveFiles);

        Assert.Null(viewModel.MsgParseResult);

        await viewModel.SelectMsgFileAsync(file);

        Assert.True(viewModel.MsgParseResult?.IsSuccess);
        Assert.Equal(3, Assert.Single(viewModel.MsgInspector.VisibleMessages).Id);

        var readCount = archive.CopyCount;
        var parsedResult = viewModel.MsgParseResult;
        await viewModel.SelectMsgFileAsync(file);
        Assert.Same(parsedResult, viewModel.MsgParseResult);
        Assert.Equal(readCount, archive.CopyCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MsgFileExplorer_ListBoxSelectionRefreshesTheEditor(
        bool openFirstFileThroughArchiveBrowser)
    {
        AvaloniaTestEnvironment.EnsureInitialized();
        using var archive = new ControlledArchive();
        var firstEntry = archive.AddEntry("dialog/first.msg", "{1}{}{first}"u8.ToArray());
        var secondEntry = archive.AddEntry("dialog/second.msg", "{2}{}{second}"u8.ToArray());
        using var viewModel = new ArchiveBrowserViewModel();
        SetPrivateField(viewModel, "_archive", archive);
        SetPrivateField(viewModel, "_archivePath", archive.Path);
        viewModel.SetEntries(archive.BrowserEntries);

        if (openFirstFileThroughArchiveBrowser)
        {
            viewModel.SelectedEntry = firstEntry;
            await viewModel.PreviewSelectedEntryAsync();
        }

        var targetEntry = openFirstFileThroughArchiveBrowser ? secondEntry : firstEntry;
        var targetFile = viewModel.MsgArchiveFiles.Single(file => ReferenceEquals(file.Entry, targetEntry));
        var expectedId = openFirstFileThroughArchiveBrowser ? 2 : 1;
        var loaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ArchiveBrowserViewModel.MsgParseResult)
                && viewModel.MsgParseResult?.Entries.SingleOrDefault()?.Id == expectedId)
            {
                loaded.TrySetResult(true);
            }
        };

        await AvaloniaTestEnvironment.RunOnUiThreadAsync(async () =>
        {
            viewModel.SelectedWorkspaceIndex = ArchiveBrowserViewModel.MsgEditorWorkspaceIndex;
            var window = new MainWindow { DataContext = viewModel };
            try
            {
                window.Show();
                var files = Assert.IsType<ListBox>(window.FindControl<ListBox>("MsgFilesList"));
                files.SelectedItem = targetFile;

                await loaded.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Same(targetEntry, viewModel.SelectedEntry);
                Assert.Same(targetFile, viewModel.SelectedMsgFile);
                Assert.Equal(expectedId, Assert.Single(viewModel.MsgInspector.VisibleMessages).Id);
                Assert.Equal(
                    expectedId == 1 ? "first" : "second",
                    Assert.Single(viewModel.MsgInspector.VisibleMessages).TextSnippet);
                var selectedMessage = Assert.Single(viewModel.MsgInspector.VisibleMessages);
                Assert.Same(selectedMessage, viewModel.MsgInspector.SelectedMessage);
                Assert.Equal(
                    expectedId == 1 ? "first" : "second",
                    viewModel.MsgInspector.TextPreview);

                var entries = Assert.IsType<ListBox>(window.FindControl<ListBox>("MsgEntryList"));
                Assert.Same(selectedMessage, entries.SelectedItem);
                Assert.Equal(
                    expectedId == 1 ? "first" : "second",
                    viewModel.MsgInspector.TextPreview);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public async Task MsgFileExplorer_RapidUiSelectionsKeepTheLastFileAfterOlderReadsFinish()
    {
        AvaloniaTestEnvironment.EnsureInitialized();
        using var archive = new ControlledArchive();
        var firstEntry = archive.AddEntry("dialog/first.msg", "{1}{}{first}"u8.ToArray());
        var secondEntry = archive.AddEntry("dialog/second.msg", "{2}{}{second}"u8.ToArray());
        var thirdEntry = archive.AddEntry("dialog/third.msg", "{3}{}{third}"u8.ToArray());
        var firstRead = archive.BlockEntry(firstEntry.Path);
        var secondRead = archive.BlockEntry(secondEntry.Path);
        var thirdRead = archive.BlockEntry(thirdEntry.Path);
        using var viewModel = new ArchiveBrowserViewModel();
        SetPrivateField(viewModel, "_archive", archive);
        viewModel.SetEntries(archive.BrowserEntries);
        var thirdLoaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ArchiveBrowserViewModel.MsgParseResult)
                && viewModel.MsgParseResult?.Entries.SingleOrDefault()?.Id == 3)
            {
                thirdLoaded.TrySetResult(true);
            }
        };

        await AvaloniaTestEnvironment.RunOnUiThreadAsync(async () =>
        {
            viewModel.SelectedWorkspaceIndex = ArchiveBrowserViewModel.MsgEditorWorkspaceIndex;
            var window = new MainWindow { DataContext = viewModel };
            try
            {
                window.Show();
                var files = Assert.IsType<ListBox>(window.FindControl<ListBox>("MsgFilesList"));
                files.SelectedItem = viewModel.MsgArchiveFiles.Single(file => ReferenceEquals(file.Entry, firstEntry));
                await firstRead.WaitStartedAsync().WaitAsync(TimeSpan.FromSeconds(10));
                files.SelectedItem = viewModel.MsgArchiveFiles.Single(file => ReferenceEquals(file.Entry, secondEntry));
                Assert.Same(secondEntry, viewModel.SelectedEntry);
                Assert.Same(secondEntry, viewModel.SelectedMsgFile?.Entry);
                await secondRead.WaitStartedAsync().WaitAsync(TimeSpan.FromSeconds(10));
                files.SelectedItem = viewModel.MsgArchiveFiles.Single(file => ReferenceEquals(file.Entry, thirdEntry));
                await thirdRead.WaitStartedAsync().WaitAsync(TimeSpan.FromSeconds(10));

                thirdRead.Release();
                await thirdLoaded.Task.WaitAsync(TimeSpan.FromSeconds(10));
                firstRead.Release();
                secondRead.Release();
                await Task.WhenAll(
                        firstRead.CopyCompleted.Task,
                        secondRead.CopyCompleted.Task)
                    .WaitAsync(TimeSpan.FromSeconds(10));
                await Task.Yield();

                Assert.Same(thirdEntry, viewModel.SelectedEntry);
                Assert.Equal(3, Assert.Single(viewModel.MsgParseResult!.Entries).Id);
                Assert.Equal("third", Assert.Single(viewModel.MsgInspector.VisibleMessages).TextSnippet);
                var entries = Assert.IsType<ListBox>(window.FindControl<ListBox>("MsgEntryList"));
                var selectedMessage = Assert.Single(viewModel.MsgInspector.VisibleMessages);
                Assert.Same(selectedMessage, viewModel.MsgInspector.SelectedMessage);
                Assert.Same(selectedMessage, entries.SelectedItem);
                Assert.Equal("third", viewModel.MsgInspector.TextPreview);
            }
            finally
            {
                firstRead.Release();
                secondRead.Release();
                thirdRead.Release();
                window.Close();
            }
        });
    }

    [Fact]
    public async Task MsgFileExplorer_ListBoxSelectionAtoBtoCtoARefreshesOnEachFirstSelection()
    {
        AvaloniaTestEnvironment.EnsureInitialized();
        using var archive = new ControlledArchive();
        var entryA = archive.AddEntry("dialog/a.msg", "{7}{}{A first}{7}{}{A duplicate}"u8.ToArray());
        var entryB = archive.AddEntry("dialog/b.msg", "{8}{}{B first}{8}{}{B duplicate}"u8.ToArray());
        var entryC = archive.AddEntry("dialog/c.msg", "{9}{}{C first}{9}{}{C duplicate}"u8.ToArray());
        using var viewModel = new ArchiveBrowserViewModel();
        SetPrivateField(viewModel, "_archive", archive);
        SetPrivateField(viewModel, "_archivePath", archive.Path);
        viewModel.SetEntries(archive.BrowserEntries);

        await AvaloniaTestEnvironment.RunOnUiThreadAsync(async () =>
        {
            viewModel.SelectedWorkspaceIndex = ArchiveBrowserViewModel.MsgEditorWorkspaceIndex;
            var window = new MainWindow { DataContext = viewModel };
            try
            {
                window.Show();
                var files = Assert.IsType<ListBox>(window.FindControl<ListBox>("MsgFilesList"));
                var entries = Assert.IsType<ListBox>(window.FindControl<ListBox>("MsgEntryList"));
                var selectionEventCount = 0;
                files.SelectionChanged += (_, _) => selectionEventCount++;

                async Task SelectAndAssertAsync(
                    ArchiveEntry entry,
                    int id,
                    string messageText,
                    int expectedReadCount)
                {
                    var parsed = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
                    {
                        if (args.PropertyName == nameof(ArchiveBrowserViewModel.MsgParseResult)
                            && viewModel.SelectedEntry == entry
                            && viewModel.MsgParseResult?.Entries.FirstOrDefault()?.Id == id)
                        {
                            parsed.TrySetResult(true);
                        }
                    }

                    viewModel.PropertyChanged += OnViewModelPropertyChanged;
                    try
                    {
                        var priorSelectionEventCount = selectionEventCount;
                        files.SelectedItem = viewModel.MsgArchiveFiles
                            .Single(file => ReferenceEquals(file.Entry, entry));

                        Assert.Equal(priorSelectionEventCount + 1, selectionEventCount);
                        await parsed.Task.WaitAsync(TimeSpan.FromSeconds(10));
                        await Task.Yield();

                        var occurrences = viewModel.MsgInspector.VisibleMessages;
                        Assert.Same(entry, viewModel.SelectedEntry);
                        Assert.Same(entry, viewModel.SelectedMsgFile?.Entry);
                        Assert.Equal(new[] { id, id }, occurrences.Select(message => message.Id));
                        Assert.Equal(new[] { $"{messageText} first", $"{messageText} duplicate" },
                            occurrences.Select(message => message.TextSnippet));
                        Assert.Equal(new[] { 0, 1 }, occurrences.Select(message => message.SourceIndex));
                        Assert.Same(occurrences[0], viewModel.MsgInspector.SelectedMessage);
                        Assert.Equal($"{messageText} first", viewModel.MsgInspector.TextPreview);
                        Assert.Same(viewModel.SelectedMsgFile, files.SelectedItem);
                        Assert.Same(occurrences[0], entries.SelectedItem);
                        Assert.Equal(expectedReadCount, archive.CopyCount);
                    }
                    finally
                    {
                        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
                    }
                }

                await SelectAndAssertAsync(entryA, 7, "A", 1);
                await SelectAndAssertAsync(entryB, 8, "B", 2);
                await SelectAndAssertAsync(entryC, 9, "C", 3);
                await SelectAndAssertAsync(entryA, 7, "A", 4);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public async Task MsgFileExplorer_InFlightAtoBtoCtoASelectionKeepsTheLatestAResult()
    {
        AvaloniaTestEnvironment.EnsureInitialized();
        using var archive = new ControlledArchive();
        var entryA = archive.AddEntry("dialog/a.msg", "{7}{}{A latest}"u8.ToArray());
        var entryB = archive.AddEntry("dialog/b.msg", "{8}{}{B stale}"u8.ToArray());
        var entryC = archive.AddEntry("dialog/c.msg", "{9}{}{C stale}"u8.ToArray());
        var readA = archive.BlockEntry(entryA.Path);
        var readB = archive.BlockEntry(entryB.Path);
        var readC = archive.BlockEntry(entryC.Path);
        using var viewModel = new ArchiveBrowserViewModel();
        SetPrivateField(viewModel, "_archive", archive);
        viewModel.SetEntries(archive.BrowserEntries);
        var latestAParsed = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ArchiveBrowserViewModel.MsgParseResult)
                && ReferenceEquals(viewModel.SelectedEntry, entryA)
                && viewModel.MsgParseResult?.Entries.FirstOrDefault()?.Id == 7)
            {
                latestAParsed.TrySetResult(true);
            }
        };

        await AvaloniaTestEnvironment.RunOnUiThreadAsync(async () =>
        {
            viewModel.SelectedWorkspaceIndex = ArchiveBrowserViewModel.MsgEditorWorkspaceIndex;
            var window = new MainWindow { DataContext = viewModel };
            try
            {
                window.Show();
                var files = Assert.IsType<ListBox>(window.FindControl<ListBox>("MsgFilesList"));
                files.SelectedItem = viewModel.MsgArchiveFiles.Single(file => ReferenceEquals(file.Entry, entryA));
                await readA.WaitStartedAsync();
                files.SelectedItem = viewModel.MsgArchiveFiles.Single(file => ReferenceEquals(file.Entry, entryB));
                await readB.WaitStartedAsync();
                files.SelectedItem = viewModel.MsgArchiveFiles.Single(file => ReferenceEquals(file.Entry, entryC));
                await readC.WaitStartedAsync();
                files.SelectedItem = viewModel.MsgArchiveFiles.Single(file => ReferenceEquals(file.Entry, entryA));

                Assert.Same(entryA, viewModel.SelectedEntry);
                Assert.Equal(4, archive.CopyCount);
                readA.Release();
                await latestAParsed.Task.WaitAsync(TimeSpan.FromSeconds(10));
                readB.Release();
                readC.Release();
                await Task.WhenAll(readB.CopyCompleted.Task, readC.CopyCompleted.Task)
                    .WaitAsync(TimeSpan.FromSeconds(10));
                await Task.Yield();

                Assert.Same(entryA, viewModel.SelectedEntry);
                Assert.Same(entryA, viewModel.SelectedMsgFile?.Entry);
                Assert.Equal(7, Assert.Single(viewModel.MsgParseResult!.Entries).Id);
                Assert.Equal("A latest", Assert.Single(viewModel.MsgInspector.VisibleMessages).TextSnippet);
                Assert.Same(
                    Assert.Single(viewModel.MsgInspector.VisibleMessages),
                    viewModel.MsgInspector.SelectedMessage);
                Assert.Equal("A latest", viewModel.MsgInspector.TextPreview);
                Assert.Same(viewModel.SelectedMsgFile, files.SelectedItem);
            }
            finally
            {
                readA.Release();
                readB.Release();
                readC.Release();
                window.Close();
            }
        });
    }

    [Fact]
    public async Task MsgFileExplorer_FailedCanceledReadCannotOverrideANewerFile()
    {
        using var archive = new ControlledArchive();
        var failedEntry = archive.AddEntry("dialog/failing.msg", "{1}{}{old}"u8.ToArray());
        var currentEntry = archive.AddEntry("dialog/current.msg", "{2}{}{current}"u8.ToArray());
        var failedRead = archive.BlockEntry(failedEntry.Path, failWithIo: true);
        using var viewModel = new ArchiveBrowserViewModel();
        SetPrivateField(viewModel, "_archive", archive);
        viewModel.SetEntries(archive.BrowserEntries);
        Task? staleLoad = null;

        try
        {
            staleLoad = viewModel.SelectMsgFileAsync(
                viewModel.MsgArchiveFiles.Single(file => ReferenceEquals(file.Entry, failedEntry)));
            await failedRead.WaitStartedAsync();
            await viewModel.SelectMsgFileAsync(
                viewModel.MsgArchiveFiles.Single(file => ReferenceEquals(file.Entry, currentEntry)));

            Assert.True(viewModel.MsgParseResult?.IsSuccess);
            Assert.Equal("current", viewModel.MsgInspector.TextPreview);
            Assert.Contains("Parsed 1 entries", viewModel.MsgStatus);

            failedRead.Release();
            await staleLoad.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Same(currentEntry, viewModel.SelectedEntry);
            Assert.Equal("current", viewModel.MsgInspector.TextPreview);
            Assert.Contains("Parsed 1 entries", viewModel.MsgStatus);
        }
        finally
        {
            failedRead.Release();
            if (staleLoad is not null)
            {
                await staleLoad.WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
    }

    [Fact]
    public async Task MsgFileExplorer_ReplacedArchiveCannotPublishTheOldFileResult()
    {
        using var oldArchive = new ControlledArchive();
        using var newArchive = new ControlledArchive();
        var oldEntry = oldArchive.AddEntry("dialog/shared.msg", "{1}{}{old archive}"u8.ToArray());
        var newEntry = newArchive.AddEntry("dialog/shared.msg", "{2}{}{new archive}"u8.ToArray());
        var oldRead = oldArchive.BlockEntry(oldEntry.Path);
        using var viewModel = new ArchiveBrowserViewModel();
        SetPrivateField(viewModel, "_archive", oldArchive);
        SetPrivateField(viewModel, "_archivePath", oldArchive.Path);
        viewModel.SetEntries(oldArchive.BrowserEntries);
        Task? staleLoad = null;

        try
        {
            staleLoad = viewModel.SelectMsgFileAsync(
                viewModel.MsgArchiveFiles.Single(file => ReferenceEquals(file.Entry, oldEntry)));
            await oldRead.WaitStartedAsync();

            SetPrivateField(viewModel, "_archive", newArchive);
            SetPrivateField(viewModel, "_archivePath", newArchive.Path);
            viewModel.SetEntries(newArchive.BrowserEntries);
            await viewModel.SelectMsgFileAsync(
                viewModel.MsgArchiveFiles.Single(file => ReferenceEquals(file.Entry, newEntry)));

            oldRead.Release();
            await staleLoad.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Same(newEntry, viewModel.SelectedEntry);
            Assert.True(viewModel.MsgParseResult?.IsSuccess);
            Assert.Equal(2, Assert.Single(viewModel.MsgParseResult!.Entries).Id);
            Assert.Equal("new archive", viewModel.MsgInspector.TextPreview);
            Assert.Equal(newArchive.Path, viewModel.MsgInspector.OriginalSourcePath);
        }
        finally
        {
            oldRead.Release();
            if (staleLoad is not null)
            {
                await staleLoad.WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
    }

    [Fact]
    public async Task MsgFileExplorer_RejectsSwitchesUntilDraftIsCancelledOrOutputIsSaved()
    {
        using var archive = new ControlledArchive();
        var firstEntry = archive.AddEntry("dialog/first.msg", "{1}{}{original}"u8.ToArray());
        var secondEntry = archive.AddEntry("dialog/second.msg", "{2}{}{next}"u8.ToArray());
        using var viewModel = new ArchiveBrowserViewModel();
        SetPrivateField(viewModel, "_archive", archive);
        viewModel.SetEntries(archive.BrowserEntries);
        viewModel.SelectedEntry = firstEntry;
        viewModel.MsgInspector.SetParseResult(FalloutMsgParser.Parse("{1}{}{original}"u8.ToArray()));
        viewModel.MsgInspector.SelectedMessage =
            Assert.Single(viewModel.MsgInspector.VisibleMessages);
        var secondFile = viewModel.MsgArchiveFiles.Single(file => ReferenceEquals(file.Entry, secondEntry));

        viewModel.MsgInspector.BeginEdit();
        viewModel.MsgInspector.DraftText = "draft";
        await viewModel.SelectMsgFileAsync(secondFile);

        Assert.Same(firstEntry, viewModel.SelectedEntry);
        Assert.Equal("draft", viewModel.MsgInspector.DraftText);
        Assert.True(viewModel.MsgInspector.IsEditing);
        Assert.False(viewModel.CanChangeArchiveSource);
        await viewModel.LoadArchiveAsync("replacement.dat");
        Assert.Same(firstEntry, viewModel.SelectedEntry);
        Assert.Equal("draft", viewModel.MsgInspector.DraftText);

        viewModel.MsgInspector.CancelEdit();
        viewModel.MsgInspector.BeginEdit();
        viewModel.MsgInspector.DraftText = "applied but not saved";
        viewModel.MsgInspector.ApplyEdit();
        var output = viewModel.MsgInspector.PatchedOutputBytes;
        Assert.True(viewModel.MsgInspector.HasUnsavedOutput);

        await viewModel.SelectMsgFileAsync(secondFile);

        Assert.Same(firstEntry, viewModel.SelectedEntry);
        Assert.Equal(output, viewModel.MsgInspector.PatchedOutputBytes);
        Assert.True(viewModel.MsgInspector.HasUnsavedOutput);
        Assert.False(viewModel.CanChangeArchiveSource);
        Assert.False(viewModel.CanOpenArchive);
        Assert.Contains("Save the applied MSG output", viewModel.StatusMessage);
        await viewModel.LoadArchiveAsync("replacement.dat");
        Assert.Same(firstEntry, viewModel.SelectedEntry);
        Assert.Equal(output, viewModel.MsgInspector.PatchedOutputBytes);

        viewModel.MsgInspector.MarkOutputSaved("synthetic-output.msg");
        Assert.False(viewModel.MsgInspector.HasUnsavedOutput);
        await viewModel.SelectMsgFileAsync(secondFile);

        Assert.Same(secondEntry, viewModel.SelectedEntry);
        Assert.True(viewModel.MsgParseResult?.IsSuccess);
    }

    [Fact]
    public async Task MsgFileExplorer_RapidSelectionDoesNotPublishStaleFileResult()
    {
        using var archive = new ControlledArchive();
        var slowEntry = archive.AddEntry("dialog/slow.msg", "{1}{}{stale}"u8.ToArray());
        var latestEntry = archive.AddEntry("dialog/latest.msg", "{2}{}{latest}"u8.ToArray());
        var slowRead = archive.BlockEntry(slowEntry.Path);
        using var viewModel = new ArchiveBrowserViewModel();
        SetPrivateField(viewModel, "_archive", archive);
        viewModel.SetEntries(archive.BrowserEntries);
        var slowFile = viewModel.MsgArchiveFiles.Single(file => ReferenceEquals(file.Entry, slowEntry));
        var latestFile = viewModel.MsgArchiveFiles.Single(file => ReferenceEquals(file.Entry, latestEntry));

        Task? staleSelection = null;
        try
        {
            staleSelection = viewModel.SelectMsgFileAsync(slowFile);
            await slowRead.WaitStartedAsync();
            await viewModel.SelectMsgFileAsync(latestFile);
            slowRead.Release();
            await staleSelection.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Same(latestEntry, viewModel.SelectedEntry);
            Assert.Same(latestFile, viewModel.SelectedMsgFile);
            Assert.Equal(2, Assert.Single(viewModel.MsgParseResult!.Entries).Id);
            Assert.Equal("latest", Assert.Single(viewModel.MsgInspector.VisibleMessages).TextSnippet);
        }
        finally
        {
            slowRead.Release();
            if (staleSelection is not null)
            {
                await staleSelection.WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
    }

    [Fact]
    public async Task PreviewSelectedEntryAsync_ParsesMsgFromOpenedFallout1Archive()
    {
        const string path = "text/english/dialog/test.msg";
        using var archiveFile = new TemporaryArchive(
            CreateDat1Archive(path, "{7}{}{fallout-one}"u8.ToArray()));
        using var viewModel = new ArchiveBrowserViewModel();

        await viewModel.LoadArchiveAsync(archiveFile.Path);
        var entry = Assert.Single(viewModel.Entries);
        Assert.Single(viewModel.MsgArchiveFiles);
        Assert.Equal(path, Assert.Single(viewModel.MsgArchiveFiles).ArchiveRelativePath);
        viewModel.SelectedEntry = entry;
        await viewModel.PreviewSelectedEntryAsync();

        Assert.Equal("Fallout 1 DAT1", viewModel.ArchiveFormatDisplay);
        Assert.True(viewModel.MsgParseResult?.IsSuccess);
        Assert.Equal(7, Assert.Single(viewModel.MsgParseResult!.Entries).Id);
        Assert.Equal(entry.Path, viewModel.PreviewMessage);
    }

    [Fact]
    public async Task LoadArchiveAsync_ResetsPreviewMessageWhenReplacingArchive()
    {
        const string previewPath = "text/english/dialog/previous.msg";
        using var firstArchive = new TemporaryArchive(
            CreateDat1Archive(previewPath, "{7}{}{previous}"u8.ToArray()));
        using var secondArchive = new TemporaryArchive(
            CreateDat1Archive("text/english/dialog/next.msg", "{8}{}{next}"u8.ToArray()));
        using var viewModel = new ArchiveBrowserViewModel();

        await viewModel.LoadArchiveAsync(firstArchive.Path);
        var previewEntry = Assert.Single(viewModel.Entries);
        viewModel.SelectedEntry = previewEntry;
        await viewModel.PreviewSelectedEntryAsync();

        Assert.Equal(previewPath, viewModel.PreviewMessage);

        await viewModel.LoadArchiveAsync(secondArchive.Path);

        Assert.Null(viewModel.SelectedEntry);
        Assert.Equal("Select a .FRM, .MAP, .PRO, .MSG, .LST, or .INT entry to inspect.",
            viewModel.PreviewMessage);
        Assert.DoesNotContain(previewPath, viewModel.PreviewMessage);
    }

    [Fact]
    public async Task PreviewSelectedEntryAsync_ResetsMsgInspectorWhenChangingMessageFiles()
    {
        using var archive = new ControlledArchive();
        var firstEntry = archive.AddEntry(
            "dialog/first.msg",
            "{5}{audio}{first}{5}{other-audio}{second}"u8.ToArray());
        var secondEntry = archive.AddEntry("dialog/second.msg", "{9}{}{new file}"u8.ToArray());
        using var viewModel = new ArchiveBrowserViewModel();
        SetPrivateField(viewModel, "_archive", archive);
        viewModel.SetEntries(archive.BrowserEntries);

        viewModel.SelectedEntry = firstEntry;
        await viewModel.PreviewSelectedEntryAsync();
        viewModel.MsgInspector.IdFilterText = "5";
        viewModel.MsgInspector.SelectedMessage = viewModel.MsgInspector.VisibleMessages[1];
        viewModel.MsgInspector.SelectedPayloadField = MsgPayloadField.Audio;

        viewModel.SelectedEntry = secondEntry;
        await viewModel.PreviewSelectedEntryAsync();

        Assert.Empty(viewModel.MsgInspector.IdFilterText);
        Assert.Single(viewModel.MsgInspector.VisibleMessages);
        Assert.Equal(9, viewModel.MsgInspector.VisibleMessages[0].Id);
        Assert.Same(viewModel.MsgInspector.VisibleMessages[0], viewModel.MsgInspector.SelectedMessage);
        Assert.Equal("new file", viewModel.MsgInspector.TextPreview);
        Assert.Equal(MsgPayloadField.Text, viewModel.MsgInspector.SelectedPayloadField);
        Assert.Contains("6E 65 77 20 66 69 6C 65", viewModel.MsgInspector.SelectedPayloadHex);
    }

    [Fact]
    public async Task PreviewSelectedEntryAsync_ClearsMsgInspectorStateForMalformedFile()
    {
        using var archive = new ControlledArchive();
        var validEntry = archive.AddEntry("dialog/valid.msg", "{5}{}{first}"u8.ToArray());
        var malformedEntry = archive.AddEntry("dialog/malformed.msg", "{2}{}{broken"u8.ToArray());
        using var viewModel = new ArchiveBrowserViewModel();
        SetPrivateField(viewModel, "_archive", archive);
        viewModel.SetEntries(archive.BrowserEntries);

        viewModel.SelectedEntry = validEntry;
        await viewModel.PreviewSelectedEntryAsync();
        viewModel.MsgInspector.IdFilterText = "5";
        viewModel.MsgInspector.SelectedMessage = Assert.Single(viewModel.MsgInspector.VisibleMessages);
        viewModel.MsgInspector.SelectedPayloadField = MsgPayloadField.Audio;

        viewModel.SelectedEntry = malformedEntry;
        await viewModel.PreviewSelectedEntryAsync();

        Assert.False(viewModel.MsgInspector.ParseResult?.IsSuccess);
        Assert.Empty(viewModel.MsgInspector.VisibleMessages);
        Assert.Null(viewModel.MsgInspector.SelectedMessage);
        Assert.Empty(viewModel.MsgInspector.IdFilterText);
        Assert.Equal(MsgPayloadField.Text, viewModel.MsgInspector.SelectedPayloadField);
        Assert.Empty(viewModel.MsgInspector.SelectedPayloadHex);
        Assert.Contains("Parsing failed at byte", viewModel.MsgStatus);
    }

    [Fact]
    public async Task PreviewSelectedEntryAsync_ShowsEmptyMsgAsSuccessfulWithNoEntries()
    {
        using var archive = new ControlledArchive();
        var entry = archive.AddEntry("text/english/dialog/empty.msg", []);
        using var viewModel = new ArchiveBrowserViewModel();
        SetPrivateField(viewModel, "_archive", archive);
        viewModel.SetEntries(archive.BrowserEntries);
        viewModel.SelectedEntry = entry;

        await viewModel.PreviewSelectedEntryAsync();

        Assert.Equal(1, archive.CopyCount);
        Assert.True(viewModel.MsgParseResult?.IsSuccess);
        Assert.Empty(viewModel.MsgParseResult!.Entries);
        Assert.Contains("no entries", viewModel.MsgStatus);
        Assert.Equal(entry.Path, viewModel.PreviewMessage);
    }

    [Fact]
    public async Task PreviewSelectedEntryAsync_ShowsMsgParseFailureWithoutPartialEntries()
    {
        using var archive = new ControlledArchive();
        var entry = archive.AddEntry(
            "text/english/dialog/malformed.msg",
            "{1}{}{valid}}"u8.ToArray());
        using var viewModel = new ArchiveBrowserViewModel();
        SetPrivateField(viewModel, "_archive", archive);
        viewModel.SetEntries(archive.BrowserEntries);
        viewModel.SelectedEntry = entry;

        await viewModel.PreviewSelectedEntryAsync();

        var result = Assert.IsType<FalloutMsgParseResult>(viewModel.MsgParseResult);
        Assert.False(result.IsSuccess);
        Assert.Empty(result.Entries);
        Assert.Equal(FalloutMsgDiagnosticCode.MismatchedDelimiter, result.Diagnostic?.Code);
        Assert.Contains("byte 12", viewModel.MsgStatus);
        Assert.Contains("Could not parse MSG", viewModel.PreviewMessage);
    }

    [Fact]
    public async Task PreviewSelectedEntryAsync_RejectsOversizedMsgBeforeReadingPayload()
    {
        using var archive = new ControlledArchive();
        var entry = archive.AddEntry(
            "text/english/dialog/oversized.msg",
            [1],
            FalloutMsgParser.MaximumFileSizeBytes + 1L);
        using var viewModel = new ArchiveBrowserViewModel();
        SetPrivateField(viewModel, "_archive", archive);
        viewModel.SetEntries(archive.BrowserEntries);
        viewModel.SelectedEntry = entry;

        await viewModel.PreviewSelectedEntryAsync();

        Assert.Equal(0, archive.CopyCount);
        Assert.True(viewModel.IsMsgInspector);
        Assert.Null(viewModel.MsgParseResult);
        Assert.Contains("was not read", viewModel.MsgStatus);
    }

    [Fact]
    public async Task PreviewSelectedEntryAsync_ReportsMsgDeclaredAndActualSizeMismatch()
    {
        using var archive = new ControlledArchive();
        var msgBytes = "{1}{}{text}"u8.ToArray();
        var declaredSize = msgBytes.LongLength - 1;
        var entry = archive.AddEntry(
            "text/english/dialog/size-mismatch.msg",
            msgBytes,
            declaredSize);
        using var viewModel = new ArchiveBrowserViewModel();
        SetPrivateField(viewModel, "_archive", archive);
        viewModel.SetEntries(archive.BrowserEntries);
        viewModel.SelectedEntry = entry;

        await viewModel.PreviewSelectedEntryAsync();

        Assert.Equal(1, archive.CopyCount);
        Assert.Null(viewModel.MsgParseResult);
        Assert.Contains(
            $"declares {declaredSize} bytes but {msgBytes.LongLength} bytes were read",
            viewModel.MsgStatus);
        Assert.StartsWith("Could not read MSG:", viewModel.PreviewMessage);
    }

    [Fact]
    public async Task PreviewSelectedEntryAsync_DisplaysScriptListSlotsAndNavigatesOnlyToUniqueIntEntry()
    {
        using var archive = new ControlledArchive();
        var listEntry = archive.AddEntry(
            "SCRIPTS\\SCRIPTS.LST",
            "first.int\n\n# comment\nfirst.int\nbad name.int\n"u8.ToArray());
        var targetEntry = archive.AddEntry("SCRIPTS\\FIRST.INT", CreateValidInt());
        using var viewModel = new ArchiveBrowserViewModel();
        SetPrivateField(viewModel, "_archive", archive);
        viewModel.SetEntries(archive.BrowserEntries);
        viewModel.SelectedEntry = listEntry;

        await viewModel.PreviewSelectedEntryAsync();

        Assert.True(viewModel.IsScriptListInspector);
        Assert.Equal("Script List Inspector", viewModel.PreviewTitle);
        Assert.Equal(5, viewModel.ScriptListSlots.Count);
        Assert.Equal(Enumerable.Range(0, 5), viewModel.ScriptListSlots.Select(slot => slot.Slot));
        Assert.Equal(
            new[]
            {
                FalloutScriptListSlotStatus.Candidate,
                FalloutScriptListSlotStatus.Empty,
                FalloutScriptListSlotStatus.InvalidName,
                FalloutScriptListSlotStatus.Candidate,
                FalloutScriptListSlotStatus.InvalidName
            },
            viewModel.ScriptListSlots.Select(slot => slot.Status));
        Assert.Equal("first", viewModel.ScriptListSlots[0].ScriptName);
        Assert.Equal("scripts/first.int", viewModel.ScriptListSlots[0].IntPathCandidate);
        Assert.Equal(viewModel.ScriptListSlots[0].IntPathCandidate, viewModel.ScriptListSlots[3].IntPathCandidate);
        Assert.Equal(1, archive.CopyCount);

        viewModel.SearchText = "missing-search-text";
        viewModel.SelectedExtension = ".LST";
        viewModel.SelectedScriptListSlot = viewModel.ScriptListSlots[3];

        Assert.Equal(FalloutScriptListIntTargetStatus.Found, viewModel.ScriptListIntTargetStatus);
        Assert.Contains("Found exactly one", viewModel.ScriptListIntTargetDetails);
        Assert.True(viewModel.CanNavigateToSelectedScriptInt);
        Assert.Same(listEntry, viewModel.SelectedEntry);

        Assert.True(viewModel.NavigateToSelectedScriptInt());

        Assert.Same(targetEntry, viewModel.SelectedEntry);
        Assert.Equal(".INT", viewModel.SelectedExtension);
        Assert.Equal(string.Empty, viewModel.SearchText);
        await viewModel.PreviewSelectedEntryAsync();
        Assert.True(viewModel.IsIntInspector);
        Assert.False(viewModel.IsScriptListInspector);
        Assert.NotNull(viewModel.IntMetadataReadResult?.Metadata);
        Assert.Equal(2, archive.CopyCount);
        Assert.True(viewModel.CanReturnToScriptList);
        Assert.True(viewModel.ReturnToScriptList());
        Assert.Same(listEntry, viewModel.SelectedEntry);

        await viewModel.PreviewSelectedEntryAsync();

        Assert.True(viewModel.IsScriptListInspector);
        Assert.Same(viewModel.ScriptListSlots[3], viewModel.SelectedScriptListSlot);
        Assert.Equal(3, archive.CopyCount);
        Assert.False(viewModel.CanReturnToScriptList);
    }

    [Fact]
    public async Task ScriptListNavigation_DisablesMissingAndInvalidSlots()
    {
        using var archive = new ControlledArchive();
        var listEntry = archive.AddEntry(
            FalloutScriptListResolver.ScriptListPath,
            "missing.int\n# comment\nbad name.int\n"u8.ToArray());
        using var viewModel = new ArchiveBrowserViewModel();
        SetPrivateField(viewModel, "_archive", archive);
        viewModel.SetEntries(archive.BrowserEntries);
        viewModel.SelectedEntry = listEntry;
        await viewModel.PreviewSelectedEntryAsync();

        viewModel.SelectedScriptListSlot = viewModel.ScriptListSlots[0];

        Assert.Equal(FalloutScriptListIntTargetStatus.Missing, viewModel.ScriptListIntTargetStatus);
        Assert.False(viewModel.CanNavigateToSelectedScriptInt);
        Assert.False(viewModel.NavigateToSelectedScriptInt());
        Assert.Same(listEntry, viewModel.SelectedEntry);
        Assert.False(viewModel.CanReturnToScriptList);

        viewModel.SelectedScriptListSlot = viewModel.ScriptListSlots[1];

        Assert.Equal(FalloutScriptListIntTargetStatus.Invalid, viewModel.ScriptListIntTargetStatus);
        Assert.False(viewModel.CanNavigateToSelectedScriptInt);
        Assert.False(viewModel.NavigateToSelectedScriptInt());
        Assert.Same(listEntry, viewModel.SelectedEntry);
        Assert.False(viewModel.CanReturnToScriptList);

        viewModel.SelectedScriptListSlot = viewModel.ScriptListSlots[2];

        Assert.Equal(FalloutScriptListIntTargetStatus.Invalid, viewModel.ScriptListIntTargetStatus);
        Assert.False(viewModel.NavigateToSelectedScriptInt());
        Assert.Same(listEntry, viewModel.SelectedEntry);
        Assert.False(viewModel.CanReturnToScriptList);
    }

    [Fact]
    public async Task ScriptListNavigation_DisablesAmbiguousTarget()
    {
        using var archive = new ControlledArchive(FalloutGame.Fallout1);
        var listEntry = archive.AddEntry(
            FalloutScriptListResolver.ScriptListPath,
            "first.int\n"u8.ToArray());
        archive.AddEntry("scripts/first.int", CreateValidInt());
        archive.AddDuplicateEntry("SCRIPTS\\FIRST.INT", CreateValidInt());
        using var viewModel = new ArchiveBrowserViewModel();
        SetPrivateField(viewModel, "_archive", archive);
        viewModel.SetEntries(archive.BrowserEntries);
        viewModel.SelectedEntry = listEntry;
        await viewModel.PreviewSelectedEntryAsync();
        viewModel.SelectedScriptListSlot = Assert.Single(viewModel.ScriptListSlots);

        Assert.Equal(FalloutScriptListIntTargetStatus.Ambiguous, viewModel.ScriptListIntTargetStatus);
        Assert.Contains("2 archive entries", viewModel.ScriptListIntTargetDetails);
        Assert.False(viewModel.CanNavigateToSelectedScriptInt);
        Assert.False(viewModel.NavigateToSelectedScriptInt());
        Assert.Same(listEntry, viewModel.SelectedEntry);
        Assert.False(viewModel.CanReturnToScriptList);
    }

    [Fact]
    public async Task ScriptListNavigation_ReturnContextIsClearedWhenEntriesAreReplacedOrClosed()
    {
        using var archive = new ControlledArchive();
        using var replacementArchive = new ControlledArchive();
        var listEntry = archive.AddEntry(FalloutScriptListResolver.ScriptListPath, "first.int\n"u8.ToArray());
        var intEntry = archive.AddEntry("scripts/first.int", CreateValidInt());
        var replacementList = replacementArchive.AddEntry(
            FalloutScriptListResolver.ScriptListPath,
            "second.int\n"u8.ToArray());
        var replacementInt = replacementArchive.AddEntry("scripts/second.int", CreateValidInt());
        using var viewModel = new ArchiveBrowserViewModel();
        SetPrivateField(viewModel, "_archive", archive);
        viewModel.SetEntries(archive.BrowserEntries);
        viewModel.SelectedEntry = listEntry;
        await viewModel.PreviewSelectedEntryAsync();
        viewModel.SelectedScriptListSlot = Assert.Single(viewModel.ScriptListSlots);

        Assert.True(viewModel.NavigateToSelectedScriptInt());
        Assert.Same(intEntry, viewModel.SelectedEntry);
        Assert.True(viewModel.CanReturnToScriptList);

        SetPrivateField(viewModel, "_archive", replacementArchive);
        viewModel.SetEntries(replacementArchive.BrowserEntries);
        Assert.False(viewModel.CanReturnToScriptList);
        Assert.False(viewModel.ReturnToScriptList());

        viewModel.SelectedEntry = replacementList;
        await viewModel.PreviewSelectedEntryAsync();
        viewModel.SelectedScriptListSlot = Assert.Single(viewModel.ScriptListSlots);
        Assert.True(viewModel.NavigateToSelectedScriptInt());
        Assert.Same(replacementInt, viewModel.SelectedEntry);
        Assert.True(viewModel.CanReturnToScriptList);

        viewModel.SetEntries([]);
        Assert.False(viewModel.CanReturnToScriptList);
        Assert.False(viewModel.ReturnToScriptList());
    }

    [Fact]
    public async Task ScriptListNavigation_DoesNotRestoreSlotAfterSelectionChangesDuringRead()
    {
        var synchronizationContext = SynchronizationContext.Current;
        AvaloniaTestEnvironment.EnsureInitialized();
        SynchronizationContext.SetSynchronizationContext(synchronizationContext);
        using var archive = new ControlledArchive();
        var listEntry = archive.AddEntry(
            FalloutScriptListResolver.ScriptListPath,
            "first.int\nfirst.int\n"u8.ToArray());
        archive.AddEntry("scripts/first.int", CreateValidInt());
        var msgEntry = archive.AddEntry("dialog/current.msg", "{2}{}{current}"u8.ToArray());
        using var viewModel = new ArchiveBrowserViewModel();
        SetPrivateField(viewModel, "_archive", archive);
        viewModel.SetEntries(archive.BrowserEntries);
        viewModel.SelectedEntry = listEntry;
        await viewModel.PreviewSelectedEntryAsync();
        viewModel.SelectedScriptListSlot = viewModel.ScriptListSlots[1];
        Assert.True(viewModel.NavigateToSelectedScriptInt());
        await viewModel.PreviewSelectedEntryAsync();

        var listRead = archive.BlockEntry(listEntry.Path);
        Task? stalePreview = null;
        try
        {
            Assert.True(viewModel.ReturnToScriptList());
            stalePreview = viewModel.PreviewSelectedEntryAsync();
            await listRead.WaitStartedAsync();

            viewModel.SelectedEntry = msgEntry;
            await viewModel.PreviewSelectedEntryAsync();
            listRead.Release();
            await stalePreview.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Same(msgEntry, viewModel.SelectedEntry);
            Assert.True(viewModel.IsMsgInspector);
            Assert.False(viewModel.IsScriptListInspector);
            Assert.Null(viewModel.SelectedScriptListSlot);
            Assert.False(viewModel.CanReturnToScriptList);
        }
        finally
        {
            listRead.Release();
            if (stalePreview is not null)
            {
                await stalePreview.WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
    }

    [Fact]
    public async Task PreviewSelectedEntryAsync_DoesNotPublishStaleScriptListAfterSelectionChanges()
    {
        var synchronizationContext = SynchronizationContext.Current;
        AvaloniaTestEnvironment.EnsureInitialized();
        SynchronizationContext.SetSynchronizationContext(synchronizationContext);
        using var archive = new ControlledArchive();
        var listEntry = archive.AddEntry(FalloutScriptListResolver.ScriptListPath, "stale.int\n"u8.ToArray());
        var msgEntry = archive.AddEntry("dialog/current.msg", "{2}{}{current}"u8.ToArray());
        var listRead = archive.BlockEntry(listEntry.Path);
        using var viewModel = new ArchiveBrowserViewModel();
        SetPrivateField(viewModel, "_archive", archive);
        viewModel.SetEntries(archive.BrowserEntries);

        Task? stalePreview = null;
        try
        {
            viewModel.SelectedEntry = listEntry;
            stalePreview = viewModel.PreviewSelectedEntryAsync();
            await listRead.WaitStartedAsync();

            viewModel.SelectedEntry = msgEntry;
            await viewModel.PreviewSelectedEntryAsync();
            listRead.Release();
            await stalePreview.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.True(viewModel.IsMsgInspector);
            Assert.False(viewModel.IsScriptListInspector);
            Assert.Empty(viewModel.ScriptListSlots);
            Assert.Equal(msgEntry.Path, viewModel.PreviewMessage);
        }
        finally
        {
            listRead.Release();
            if (stalePreview is not null)
            {
                await stalePreview.WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
    }

    [Fact]
    public async Task PreviewSelectedEntryAsync_DoesNotPublishScriptListFromReplacedArchive()
    {
        var synchronizationContext = SynchronizationContext.Current;
        AvaloniaTestEnvironment.EnsureInitialized();
        SynchronizationContext.SetSynchronizationContext(synchronizationContext);
        using var oldArchive = new ControlledArchive();
        using var newArchive = new ControlledArchive();
        var oldList = oldArchive.AddEntry(FalloutScriptListResolver.ScriptListPath, "old.int\n"u8.ToArray());
        var newList = newArchive.AddEntry(FalloutScriptListResolver.ScriptListPath, "new.int\n"u8.ToArray());
        var oldRead = oldArchive.BlockEntry(oldList.Path);
        using var viewModel = new ArchiveBrowserViewModel();
        SetPrivateField(viewModel, "_archive", oldArchive);
        viewModel.SetEntries(oldArchive.BrowserEntries);

        Task? stalePreview = null;
        try
        {
            viewModel.SelectedEntry = oldList;
            stalePreview = viewModel.PreviewSelectedEntryAsync();
            await oldRead.WaitStartedAsync();

            SetPrivateField(viewModel, "_archive", newArchive);
            viewModel.SetEntries(newArchive.BrowserEntries);
            viewModel.SelectedEntry = newList;
            await viewModel.PreviewSelectedEntryAsync();
            oldRead.Release();
            await stalePreview.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.True(viewModel.IsScriptListInspector);
            Assert.Single(viewModel.ScriptListSlots);
            Assert.Equal("new", viewModel.ScriptListSlots[0].ScriptName);
            Assert.Equal(newList.Path, viewModel.PreviewMessage);
        }
        finally
        {
            oldRead.Release();
            if (stalePreview is not null)
            {
                await stalePreview.WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
    }

    [Fact]
    public async Task PreviewSelectedEntryAsync_ParsesIntMetadataAndDisplaysRawFieldsAndIdentifierBytes()
    {
        using var archive = new ControlledArchive();
        var entry = archive.AddEntry("scripts/TEST.INT", CreateValidInt());
        using var viewModel = new ArchiveBrowserViewModel();
        SetPrivateField(viewModel, "_archive", archive);
        viewModel.SetEntries(archive.BrowserEntries);
        viewModel.SelectedEntry = entry;

        await viewModel.PreviewSelectedEntryAsync();

        var result = Assert.IsType<FalloutIntMetadataReadResult>(viewModel.IntMetadataReadResult);
        var metadata = Assert.IsType<FalloutIntMetadata>(result.Metadata);
        var procedure = Assert.Single(metadata.Procedures);
        Assert.Equal(1, archive.CopyCount);
        Assert.Contains(".INT", viewModel.ExtensionOptions);
        Assert.Equal("INT Metadata Inspector", viewModel.PreviewTitle);
        Assert.True(viewModel.IsIntInspector);
        Assert.False(viewModel.IsMsgInspector);
        Assert.False(viewModel.IsMapInspector);
        Assert.Equal(FalloutGame.Fallout2, metadata.Game);
        Assert.False(viewModel.CanReturnToScriptList);
        Assert.Equal(1, metadata.ProcedureCount);
        Assert.Equal(4, procedure.RawField0);
        Assert.Equal(0, procedure.RawField4);
        Assert.Equal(0x01020304, procedure.RawField8);
        Assert.Equal(0x11223344, procedure.RawField12);
        Assert.Equal(42, procedure.RawField16);
        Assert.Equal(0x55667788, procedure.RawField20);
        Assert.Contains("Procedure count: 1", viewModel.IntProcedureCountDisplay);
        Assert.Contains("00000000  00 00 00 04 41 42 43 00", viewModel.IntIdentifierTableHex);
        Assert.Equal(entry.Path, viewModel.PreviewMessage);
    }

    [Fact]
    public async Task PreviewSelectedEntryAsync_ShowsIntParserDiagnosticWithoutPartialMetadata()
    {
        using var archive = new ControlledArchive();
        var entry = archive.AddEntry("scripts/MALFORMED.INT", new byte[42]);
        using var viewModel = new ArchiveBrowserViewModel();
        SetPrivateField(viewModel, "_archive", archive);
        viewModel.SetEntries(archive.BrowserEntries);
        viewModel.SelectedEntry = entry;

        await viewModel.PreviewSelectedEntryAsync();

        var result = Assert.IsType<FalloutIntMetadataReadResult>(viewModel.IntMetadataReadResult);
        Assert.False(result.IsSuccess);
        Assert.Null(result.Metadata);
        Assert.Equal(FalloutIntMetadataDiagnosticCode.TruncatedProcedureCount, result.Diagnostic?.Code);
        Assert.Equal(42, result.Diagnostic?.ByteOffset);
        Assert.True(viewModel.HasIntDiagnostic);
        Assert.Contains("TruncatedProcedureCount at byte 42", viewModel.IntDiagnosticDisplay);
        Assert.Contains("TruncatedProcedureCount", viewModel.IntStatus);
        Assert.Contains("Could not parse INT", viewModel.PreviewMessage);
    }

    [Fact]
    public async Task PreviewSelectedEntryAsync_RejectsOversizedIntBeforeReadingPayload()
    {
        using var archive = new ControlledArchive();
        var entry = archive.AddEntry(
            "scripts/OVERSIZED.INT",
            [1],
            FalloutIntMetadataReader.MaximumFileSizeBytes + 1L);
        using var viewModel = new ArchiveBrowserViewModel();
        SetPrivateField(viewModel, "_archive", archive);
        viewModel.SetEntries(archive.BrowserEntries);
        viewModel.SelectedEntry = entry;

        await viewModel.PreviewSelectedEntryAsync();

        Assert.Equal(0, archive.CopyCount);
        Assert.True(viewModel.IsIntInspector);
        Assert.Null(viewModel.IntMetadataReadResult);
        Assert.Contains("INT preview is limited", viewModel.IntStatus);
        Assert.Contains("the entry was not read", viewModel.PreviewMessage);
    }

    [Fact]
    public async Task PreviewSelectedEntryAsync_ClearsPreviousIntStateWhenReadFails()
    {
        using var archive = new ControlledArchive();
        var bytes = CreateValidInt();
        var validEntry = archive.AddEntry("scripts/VALID.INT", bytes);
        var mismatchedEntry = archive.AddEntry("scripts/MISMATCH.INT", bytes, bytes.LongLength - 1);
        using var viewModel = new ArchiveBrowserViewModel();
        SetPrivateField(viewModel, "_archive", archive);
        viewModel.SetEntries(archive.BrowserEntries);

        viewModel.SelectedEntry = validEntry;
        await viewModel.PreviewSelectedEntryAsync();
        Assert.NotNull(viewModel.IntMetadataReadResult?.Metadata);
        Assert.NotEmpty(viewModel.IntIdentifierTableHex);

        viewModel.SelectedEntry = mismatchedEntry;
        await viewModel.PreviewSelectedEntryAsync();

        Assert.True(viewModel.IsIntInspector);
        Assert.Null(viewModel.IntMetadataReadResult);
        Assert.Empty(viewModel.IntIdentifierTableHex);
        Assert.Contains("Could not read INT", viewModel.IntStatus);
        Assert.Contains("declares", viewModel.PreviewMessage);
    }

    [Fact]
    public async Task PreviewSelectedEntryAsync_ClearsIntStateWhenSwitchingToAnotherRoute()
    {
        using var archive = new ControlledArchive();
        var intEntry = archive.AddEntry("scripts/VALID.INT", CreateValidInt());
        var msgEntry = archive.AddEntry("text/english/dialog/sample.MSG", "{1}{}{text}"u8.ToArray());
        using var viewModel = new ArchiveBrowserViewModel();
        SetPrivateField(viewModel, "_archive", archive);
        viewModel.SetEntries(archive.BrowserEntries);

        viewModel.SelectedEntry = intEntry;
        await viewModel.PreviewSelectedEntryAsync();
        Assert.NotNull(viewModel.IntMetadataReadResult?.Metadata);

        viewModel.SelectedEntry = msgEntry;
        await viewModel.PreviewSelectedEntryAsync();

        Assert.False(viewModel.IsIntInspector);
        Assert.Null(viewModel.IntMetadataReadResult);
        Assert.Empty(viewModel.IntIdentifierTableHex);
        Assert.True(viewModel.IsMsgInspector);
        Assert.True(viewModel.MsgParseResult?.IsSuccess);
    }

    [Fact]
    public async Task PreviewSelectedEntryAsync_CancelsIntReadWhenSelectionChanges()
    {
        var synchronizationContext = SynchronizationContext.Current;
        AvaloniaTestEnvironment.EnsureInitialized();
        SynchronizationContext.SetSynchronizationContext(synchronizationContext);
        using var archive = new ControlledArchive();
        var intEntry = archive.AddEntry("scripts/STALE.INT", CreateValidInt());
        var msgEntry = archive.AddEntry("text/english/dialog/CURRENT.MSG", "{2}{}{current}"u8.ToArray());
        var intRead = archive.BlockEntry(intEntry.Path);
        using var viewModel = new ArchiveBrowserViewModel();
        SetPrivateField(viewModel, "_archive", archive);
        viewModel.SetEntries(archive.BrowserEntries);

        Task? intPreview = null;
        try
        {
            viewModel.SelectedEntry = intEntry;
            intPreview = viewModel.PreviewSelectedEntryAsync();
            await intRead.WaitStartedAsync();

            viewModel.SelectedEntry = msgEntry;
            await viewModel.PreviewSelectedEntryAsync();

            Assert.False(viewModel.IsIntInspector);
            Assert.Null(viewModel.IntMetadataReadResult);
            Assert.True(viewModel.MsgParseResult?.IsSuccess);
            intRead.Release();
            await intPreview.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(msgEntry.Path, viewModel.PreviewMessage);
            Assert.Null(viewModel.IntMetadataReadResult);
        }
        finally
        {
            intRead.Release();
            if (intPreview is not null)
            {
                await intPreview.WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
    }

    [Fact]
    public async Task Dispose_ClearsIntPreviewState()
    {
        using var archive = new ControlledArchive();
        var entry = archive.AddEntry("scripts/VALID.INT", CreateValidInt());
        var viewModel = new ArchiveBrowserViewModel();
        SetPrivateField(viewModel, "_archive", archive);
        viewModel.SetEntries(archive.BrowserEntries);
        viewModel.SelectedEntry = entry;
        await viewModel.PreviewSelectedEntryAsync();
        Assert.NotNull(viewModel.IntMetadataReadResult?.Metadata);

        viewModel.Dispose();

        Assert.False(viewModel.IsIntInspector);
        Assert.Null(viewModel.IntMetadataReadResult);
        Assert.Empty(viewModel.IntIdentifierTableHex);
    }

    [Fact]
    public async Task PreviewSelectedEntryAsync_ClearsFrmImageBeforeMapRoute()
    {
        AvaloniaTestEnvironment.EnsureInitialized();
        using var archiveFile = new TemporaryArchive(CreateDat1Archive("MAPS/TEST.MAP"));
        using var viewModel = new ArchiveBrowserViewModel();

        await viewModel.LoadArchiveAsync(archiveFile.Path);
        viewModel.FrmPreview.SetPreview(
            new FrmDocument(
                4,
                10,
                0,
                1,
                [new FrmDirection(0, "NE", 0, 0, [new FrmFrame(1, 1, 0, 0, [1])])]),
            FalloutPalette.Parse(new byte[FalloutPalette.MinimalFileSize]),
            "ART/CRITTERS/STALE.FRM");
        Assert.NotNull(viewModel.FrmPreview.PreviewBitmap);

        viewModel.SelectedEntry = Assert.Single(viewModel.Entries);
        await viewModel.PreviewSelectedEntryAsync();

        Assert.Null(viewModel.FrmPreview.PreviewBitmap);
        Assert.False(viewModel.FrmPreview.HasPreview);
        Assert.StartsWith("Could not inspect MAP:", viewModel.PreviewMessage);
    }

    [Fact]
    public async Task PreviewSelectedEntryAsync_RejectsOversizedMapBeforeReadingPayload()
    {
        using var archive = new ControlledArchive();
        var entry = archive.AddEntry(
            "MAPS/OVERSIZED.MAP",
            CreateValidEmptyMap(),
            ArchiveBrowserViewModel.MaximumMapPreviewSizeBytes + 1);
        using var viewModel = new ArchiveBrowserViewModel();
        SetPrivateField(viewModel, "_archive", archive);
        viewModel.SetEntries(archive.BrowserEntries);
        viewModel.SelectedEntry = entry;

        await viewModel.PreviewSelectedEntryAsync();

        Assert.Equal(0, archive.CopyCount);
        Assert.Contains("64 MiB", viewModel.PreviewMessage);
        Assert.Contains("The entry was not read.", viewModel.PreviewMessage);
    }

    [Fact]
    public async Task PreviewSelectedEntryAsync_ParsesValidMapWithinSizeLimit()
    {
        using var archive = new ControlledArchive();
        var entry = archive.AddEntry("MAPS/VALID.MAP", CreateValidEmptyMap());
        using var viewModel = new ArchiveBrowserViewModel();
        SetPrivateField(viewModel, "_archive", archive);
        viewModel.SetEntries(archive.BrowserEntries);
        viewModel.SelectedEntry = entry;

        await viewModel.PreviewSelectedEntryAsync();

        Assert.Equal(1, archive.CopyCount);
        Assert.True(viewModel.IsMapInspector);
        Assert.False(viewModel.IsMapEditorEmpty);
        Assert.Equal(ArchiveBrowserViewModel.MapEditorWorkspaceIndex, viewModel.SelectedWorkspaceIndex);
        var editSession = viewModel.MapInspector.EditSession;
        Assert.NotNull(editSession);

        viewModel.SelectedWorkspaceIndex = 0;
        Assert.Same(editSession, viewModel.MapInspector.EditSession);
        Assert.True(viewModel.IsMapInspector);
        viewModel.SelectedWorkspaceIndex = ArchiveBrowserViewModel.MapEditorWorkspaceIndex;
        Assert.Same(editSession, viewModel.MapInspector.EditSession);

        Assert.Equal(entry.Path, viewModel.PreviewMessage);
        Assert.Equal(
            "Decoded 0 records, including 0 top-level objects.",
            viewModel.MapObjectStatusDisplay);
    }

    [Fact]
    public async Task PreviewSelectedEntryAsync_DeclaredSizeAtLimitUsesNormalReadPath()
    {
        using var archive = new ControlledArchive();
        var entry = archive.AddEntry(
            "MAPS/BOUNDARY.MAP",
            CreateValidEmptyMap(),
            ArchiveBrowserViewModel.MaximumMapPreviewSizeBytes);
        using var viewModel = new ArchiveBrowserViewModel();
        SetPrivateField(viewModel, "_archive", archive);
        viewModel.SetEntries(archive.BrowserEntries);
        viewModel.SelectedEntry = entry;

        await viewModel.PreviewSelectedEntryAsync();

        Assert.Equal(1, archive.CopyCount);
        Assert.StartsWith("Could not inspect MAP: MAP entry declares", viewModel.PreviewMessage);
        Assert.DoesNotContain("preview is limited", viewModel.PreviewMessage);
    }

    [Fact]
    public async Task PreviewSelectedEntryAsync_SupersededMapReadCannotPublishOrClearNewerPreview()
    {
        var synchronizationContext = SynchronizationContext.Current;
        AvaloniaTestEnvironment.EnsureInitialized();
        SynchronizationContext.SetSynchronizationContext(synchronizationContext);
        using var archive = new ControlledArchive();
        var mapEntry = archive.AddEntry("MAPS/STALE.MAP", CreateValidEmptyMap());
        var frmEntry = archive.AddEntry(
            "ART/CRITTERS/CURRENT.FRM",
            FrmFixture.Create([[new FrmFixtureFrame(1, 1, 0, 0, [17])]], framesPerSecond: 17));
        archive.AddEntry("COLOR.PAL", new byte[FalloutPalette.MinimalFileSize]);
        var mapRead = archive.BlockEntry(mapEntry.Path);
        var frmRead = archive.BlockEntry(frmEntry.Path);
        using var viewModel = new ArchiveBrowserViewModel();
        SetPrivateField(viewModel, "_archive", archive);
        viewModel.SetEntries(archive.BrowserEntries);

        Task? mapPreview = null;
        Task? frmPreview = null;
        try
        {
            viewModel.SelectedEntry = mapEntry;
            mapPreview = viewModel.PreviewSelectedEntryAsync();
            await mapRead.WaitStartedAsync();

            viewModel.SelectedEntry = frmEntry;
            frmPreview = viewModel.PreviewSelectedEntryAsync();
            await frmRead.WaitStartedAsync();
            var currentCancellation = GetPrivateField<CancellationTokenSource?>(
                viewModel,
                "_previewCancellation");
            Assert.NotNull(currentCancellation);

            mapRead.Release();
            await mapPreview.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Same(
                currentCancellation,
                GetPrivateField<CancellationTokenSource?>(viewModel, "_previewCancellation"));
            Assert.Equal("FRM Preview", viewModel.PreviewTitle);
            Assert.Equal("Reading and decoding FRM...", viewModel.PreviewMessage);
            Assert.False(viewModel.IsMapInspector);

            frmRead.Release();
            await frmPreview.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(frmEntry.Path, viewModel.PreviewMessage);
            Assert.Equal("CURRENT_NE_Frame01.png", viewModel.FrmPreview.SuggestedPngFileName);
        }
        finally
        {
            mapRead.Release();
            frmRead.Release();
            if (mapPreview is not null)
            {
                await mapPreview.WaitAsync(TimeSpan.FromSeconds(10));
            }

            if (frmPreview is not null)
            {
                await frmPreview.WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
    }

    [Fact]
    public async Task PreviewSelectedEntryAsync_ShowsStandaloneProInspection()
    {
        using var archive = new ControlledArchive();
        var entry = archive.AddEntry(
            "proto/items/STANDALONE.pro",
            CreateItemPrototype(0x00000001));
        using var viewModel = new ArchiveBrowserViewModel();
        SetPrivateField(viewModel, "_archive", archive);
        viewModel.SetEntries(archive.BrowserEntries);
        viewModel.SelectedEntry = entry;

        await viewModel.PreviewSelectedEntryAsync();

        Assert.Equal(1, archive.CopyCount);
        Assert.Equal("PRO Inspector", viewModel.PreviewTitle);
        Assert.True(viewModel.IsProInspector);
        Assert.False(viewModel.IsMapInspector);
        Assert.Equal(entry.Path, viewModel.PreviewMessage);
        Assert.Contains(entry.Path, viewModel.StandaloneProDetails);
        Assert.Contains("Selected directly from the archive", viewModel.StandaloneProDetails);
        Assert.Contains("Item (0)", viewModel.StandaloneProDetails);
        Assert.Contains("Misc (5)", viewModel.StandaloneProDetails);
        Assert.Contains("Prototype PID, subtype, and Item size validated.", viewModel.StandaloneProStatus);
        Assert.Contains("messageId:", viewModel.StandaloneProFieldsDisplay);
        Assert.True(viewModel.HasStandaloneProRawBytes);
        Assert.Equal(entry.UncompressedSize, viewModel.StandaloneProInspection?.ActualSize);
        Assert.True(viewModel.StandaloneProInspection?.IsReadComplete == true);
        Assert.True(viewModel.CanExportStandaloneProReport);
        Assert.True(viewModel.CanExportStandaloneProRawBin);
    }

    [Fact]
    public async Task ExportStandaloneProReportAsync_WritesUtf8ReportForCurrentInspection()
    {
        using var archive = new ControlledArchive();
        archive.AddEntry("proto/items/REPORT.pro", CreateItemPrototype(0x00000001));
        using var viewModel = new ArchiveBrowserViewModel();
        SetPrivateField(viewModel, "_archive", archive);
        viewModel.SetEntries(archive.BrowserEntries);
        viewModel.SelectedEntry = Assert.Single(archive.BrowserEntries);
        await viewModel.PreviewSelectedEntryAsync();
        var inspection = Assert.IsType<Fallout2StandalonePrototypeInspection>(viewModel.StandaloneProInspection);
        using var destination = new MemoryStream();

        await viewModel.ExportStandaloneProReportAsync(destination, inspection);

        var report = Encoding.UTF8.GetString(destination.ToArray());
        Assert.StartsWith("Fallout Asset Explorer — PRO Inspection Report", report);
        Assert.Contains("proto/items/REPORT.pro", report);
        Assert.Contains("Parsing result: Success", report);
    }

    [Fact]
    public async Task ExportStandaloneProRawBinAsync_WritesExactlyTheReadBytes()
    {
        using var archive = new ControlledArchive();
        var bytes = CreateItemPrototype(0x00000001);
        archive.AddEntry("proto/items/RAW.pro", bytes);
        using var viewModel = new ArchiveBrowserViewModel();
        SetPrivateField(viewModel, "_archive", archive);
        viewModel.SetEntries(archive.BrowserEntries);
        viewModel.SelectedEntry = Assert.Single(archive.BrowserEntries);
        await viewModel.PreviewSelectedEntryAsync();
        var inspection = Assert.IsType<Fallout2StandalonePrototypeInspection>(viewModel.StandaloneProInspection);
        using var destination = new MemoryStream();

        await viewModel.ExportStandaloneProRawBinAsync(destination, inspection);

        Assert.Equal(bytes, destination.ToArray());
    }

    [Fact]
    public async Task ExportStandaloneProRawBinAsync_RejectsIncompleteReadWithoutWriting()
    {
        using var archive = new ControlledArchive();
        var bytes = CreateItemPrototype(0x00000001);
        var entry = archive.AddEntry("proto/items/INCOMPLETE.pro", bytes, bytes.Length + 1L);
        using var viewModel = new ArchiveBrowserViewModel();
        SetPrivateField(viewModel, "_archive", archive);
        viewModel.SetEntries(archive.BrowserEntries);
        viewModel.SelectedEntry = entry;
        await viewModel.PreviewSelectedEntryAsync();
        var inspection = Assert.IsType<Fallout2StandalonePrototypeInspection>(viewModel.StandaloneProInspection);
        using var destination = new MemoryStream();

        Assert.False(inspection.IsReadComplete);
        Assert.Equal(bytes.Length, inspection.ActualSize);
        Assert.Equal(bytes, inspection.RawBytes.ToArray());
        Assert.False(viewModel.CanExportStandaloneProRawBin);
        await Assert.ThrowsAsync<InvalidDataException>(
            () => viewModel.ExportStandaloneProRawBinAsync(destination, inspection));
        Assert.Empty(destination.ToArray());
    }

    [Fact]
    public async Task ExportStandaloneProReportAsync_PropagatesWriteFailureAndCancellation()
    {
        using var archive = new ControlledArchive();
        archive.AddEntry("proto/items/WRITE-FAIL.pro", CreateItemPrototype(0x00000001));
        using var viewModel = new ArchiveBrowserViewModel();
        SetPrivateField(viewModel, "_archive", archive);
        viewModel.SetEntries(archive.BrowserEntries);
        viewModel.SelectedEntry = Assert.Single(archive.BrowserEntries);
        await viewModel.PreviewSelectedEntryAsync();
        var inspection = Assert.IsType<Fallout2StandalonePrototypeInspection>(viewModel.StandaloneProInspection);

        await Assert.ThrowsAsync<IOException>(
            () => viewModel.ExportStandaloneProReportAsync(new ThrowingWriteStream(), inspection));

        using var cancelledDestination = new MemoryStream();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => viewModel.ExportStandaloneProReportAsync(
                cancelledDestination,
                inspection,
                cancellation.Token));
        Assert.Empty(cancelledDestination.ToArray());
        Assert.True(viewModel.CanExportStandaloneProReport);
    }

    [Fact]
    public async Task ExportStandaloneProReportAsync_CancelsWhenSelectionChangesDuringWrite()
    {
        using var archive = new ControlledArchive();
        archive.AddEntry("proto/items/OLD.pro", CreateItemPrototype(0x00000001));
        var newEntry = archive.AddEntry("proto/items/NEW.pro", CreateItemPrototype(0x00000002));
        using var viewModel = new ArchiveBrowserViewModel();
        SetPrivateField(viewModel, "_archive", archive);
        viewModel.SetEntries(archive.BrowserEntries);
        viewModel.SelectedEntry = archive.BrowserEntries[0];
        await viewModel.PreviewSelectedEntryAsync();
        var oldInspection = Assert.IsType<Fallout2StandalonePrototypeInspection>(viewModel.StandaloneProInspection);
        using var destination = new BlockingWriteStream();

        var export = viewModel.ExportStandaloneProReportAsync(destination, oldInspection);
        await destination.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        viewModel.SelectedEntry = newEntry;
        await viewModel.PreviewSelectedEntryAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => export);
        Assert.Empty(destination.ToArray());
        Assert.Equal(newEntry.Path, viewModel.StandaloneProInspection?.EntryPath);
        Assert.True(viewModel.CanExportStandaloneProReport);
    }

    [Fact]
    public async Task ExportFrmSpritesheetsAsync_ExportsVisibleFramesWithArchivePalette()
    {
        using var archive = new ControlledArchive();
        archive.AddEntry("COLOR.PAL", new byte[FalloutPalette.MinimalFileSize]);
        archive.AddEntry(
            "art/critters/first.frm",
            FrmFixture.Create([[new FrmFixtureFrame(1, 1, 0, 0, [11])]]));
        archive.AddEntry(
            "art/critters/second.frm",
            FrmFixture.Create([[new FrmFixtureFrame(1, 1, 0, 0, [22])]]));
        archive.AddEntry(
            "art/critters/third.frm",
            FrmFixture.Create([[new FrmFixtureFrame(1, 1, 0, 0, [33])]]));
        archive.AddEntry("text/english/dialog/readme.msg", "{1}{}{readme}"u8.ToArray());
        using var viewModel = new ArchiveBrowserViewModel();
        using var destination = new TemporaryDirectory();
        SetPrivateField(viewModel, "_archive", archive);
        viewModel.SetEntries(archive.BrowserEntries);

        Assert.True(viewModel.CanExportVisibleFrms);
        await viewModel.ExportFrmSpritesheetsAsync(viewModel.VisibleEntries.ToArray(), destination.Path);

        AssertPngFile(Path.Combine(destination.Path, "first.png"));
        AssertPngFile(Path.Combine(destination.Path, "second.png"));
        AssertPngFile(Path.Combine(destination.Path, "third.png"));
        Assert.Equal(3, Directory.GetFiles(destination.Path, "*.png").Length);
        Assert.Contains("3 exported, 2 skipped, 0 failed", viewModel.StatusMessage);
        Assert.Contains("Palette: COLOR.PAL in", viewModel.StatusMessage);
    }

    [Fact]
    public async Task ExportFrmSpritesheetsAsync_ExportsOnlySelectedFrmEntries()
    {
        using var archive = new ControlledArchive();
        archive.AddEntry("COLOR.PAL", new byte[FalloutPalette.MinimalFileSize]);
        var frmEntry = archive.AddEntry(
            "art/critters/selected-first.frm",
            FrmFixture.Create([[new FrmFixtureFrame(1, 1, 0, 0, [11])]]));
        var secondFrmEntry = archive.AddEntry(
            "art/critters/selected-second.frm",
            FrmFixture.Create([[new FrmFixtureFrame(1, 1, 0, 0, [22])]]));
        var thirdFrmEntry = archive.AddEntry(
            "art/critters/selected-third.frm",
            FrmFixture.Create([[new FrmFixtureFrame(1, 1, 0, 0, [33])]]));
        var msgEntry = archive.AddEntry("text/english/dialog/readme.msg", "{1}{}{readme}"u8.ToArray());
        using var viewModel = new ArchiveBrowserViewModel();
        using var destination = new TemporaryDirectory();
        SetPrivateField(viewModel, "_archive", archive);
        viewModel.SetEntries(archive.BrowserEntries);

        viewModel.SearchText = ".msg";
        Assert.False(viewModel.CanExportVisibleFrms);
        viewModel.SearchText = string.Empty;
        viewModel.UpdateSelectionCount(4);
        viewModel.UpdateSelectedFrmCount(3);
        Assert.True(viewModel.CanExportSelectedFrms);

        await viewModel.ExportFrmSpritesheetsAsync(
            [frmEntry, secondFrmEntry, thirdFrmEntry, msgEntry],
            destination.Path);

        AssertPngFile(Path.Combine(destination.Path, "selected-first.png"));
        AssertPngFile(Path.Combine(destination.Path, "selected-second.png"));
        AssertPngFile(Path.Combine(destination.Path, "selected-third.png"));
        Assert.Equal(3, Directory.GetFiles(destination.Path, "*.png").Length);
        Assert.Contains("3 exported, 1 skipped, 0 failed", viewModel.StatusMessage);
    }

    [Fact]
    public async Task ExportFrmSpritesheetsAsync_UsesAdjacentMasterPaletteForCritterArchive()
    {
        using var gameDirectory = new TemporaryDirectory();
        var paletteBytes = new byte[FalloutPalette.MinimalFileSize];
        paletteBytes[3] = 18;
        File.WriteAllBytes(
            Path.Combine(gameDirectory.Path, "MASTER.DAT"),
            CreateDat1Archive("COLOR.PAL", paletteBytes));
        using var archive = new ControlledArchive(
            FalloutGame.Fallout1,
            Path.Combine(gameDirectory.Path, "CRITTER.DAT"));
        archive.AddEntry(
            "ART/CRITTERS/HERO.FRM",
            FrmFixture.Create([[new FrmFixtureFrame(1, 1, 0, 0, [1])]]));
        using var viewModel = new ArchiveBrowserViewModel();
        using var destination = new TemporaryDirectory();
        SetPrivateField(viewModel, "_archive", archive);
        viewModel.SetEntries(archive.BrowserEntries);

        await viewModel.ExportFrmSpritesheetsAsync(viewModel.VisibleEntries.ToArray(), destination.Path);

        AssertPngFile(Path.Combine(destination.Path, "HERO.png"));
        Assert.Contains("Palette: COLOR.PAL in MASTER.DAT", viewModel.StatusMessage);
        Assert.Contains("1 exported, 0 skipped, 0 failed", viewModel.StatusMessage);
    }

    [Fact]
    public async Task ExportFrmSpritesheetsAsync_ContinuesAfterOneMalformedFrm()
    {
        using var archive = new ControlledArchive();
        archive.AddEntry("COLOR.PAL", new byte[FalloutPalette.MinimalFileSize]);
        archive.AddEntry(
            "art/critters/valid-before.frm",
            FrmFixture.Create([[new FrmFixtureFrame(1, 1, 0, 0, [11])]]));
        var malformedEntry = archive.AddEntry("art/critters/malformed.frm", [1, 2, 3]);
        archive.AddEntry(
            "art/critters/valid-after.frm",
            FrmFixture.Create([[new FrmFixtureFrame(1, 1, 0, 0, [22])]]));
        using var viewModel = new ArchiveBrowserViewModel();
        using var destination = new TemporaryDirectory();
        SetPrivateField(viewModel, "_archive", archive);
        viewModel.SetEntries(archive.BrowserEntries);

        await viewModel.ExportFrmSpritesheetsAsync(viewModel.VisibleEntries.ToArray(), destination.Path);

        AssertPngFile(Path.Combine(destination.Path, "valid-before.png"));
        AssertPngFile(Path.Combine(destination.Path, "valid-after.png"));
        Assert.Equal(2, Directory.GetFiles(destination.Path, "*.png").Length);
        Assert.Contains("2 exported, 1 skipped, 1 failed", viewModel.StatusMessage);
        Assert.Contains(malformedEntry.Path, viewModel.StatusMessage);
    }

    [Fact]
    public async Task ExportFrmSpritesheetsAsync_UsesSafeUniqueOutputNames()
    {
        using var archive = new ControlledArchive();
        archive.AddEntry("COLOR.PAL", new byte[FalloutPalette.MinimalFileSize]);
        archive.AddEntry(
            @"..\..\escape.frm",
            FrmFixture.Create([[new FrmFixtureFrame(1, 1, 0, 0, [11])]]));
        archive.AddEntry(
            "other/ESCAPE.FRM",
            FrmFixture.Create([[new FrmFixtureFrame(1, 1, 0, 0, [22])]]));
        using var viewModel = new ArchiveBrowserViewModel();
        using var destination = new TemporaryDirectory();
        var existingFile = Path.Combine(destination.Path, "escape.png");
        File.WriteAllText(existingFile, "preserve existing output");
        SetPrivateField(viewModel, "_archive", archive);
        viewModel.SetEntries(archive.BrowserEntries);

        await viewModel.ExportFrmSpritesheetsAsync(viewModel.VisibleEntries.ToArray(), destination.Path);

        Assert.Equal("preserve existing output", File.ReadAllText(existingFile));
        AssertPngFile(Path.Combine(destination.Path, "escape (2).png"));
        AssertPngFile(Path.Combine(destination.Path, "ESCAPE (3).png"));
        Assert.Equal(3, Directory.GetFiles(destination.Path, "*.png").Length);
        Assert.All(
            Directory.GetFiles(destination.Path),
            path => Assert.Equal(destination.Path, Path.GetDirectoryName(Path.GetFullPath(path))));
    }

    [Fact]
    public async Task ExportFrmSpritesheetsAsync_CancelsWithoutPublishingPartialOutput()
    {
        using var archive = new ControlledArchive();
        archive.AddEntry("COLOR.PAL", new byte[FalloutPalette.MinimalFileSize]);
        var entry = archive.AddEntry(
            "art/critters/cancel.frm",
            FrmFixture.Create([[new FrmFixtureFrame(1, 1, 0, 0, [11])]]));
        var read = archive.BlockEntry(entry.Path);
        using var viewModel = new ArchiveBrowserViewModel();
        using var destination = new TemporaryDirectory();
        SetPrivateField(viewModel, "_archive", archive);
        viewModel.SetEntries(archive.BrowserEntries);

        var export = viewModel.ExportFrmSpritesheetsAsync([entry], destination.Path);
        await read.WaitStartedAsync();
        viewModel.CancelFrmBatchExport();
        read.Release();
        await export.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Empty(Directory.GetFiles(destination.Path));
        Assert.False(viewModel.IsExportingFrms);
        Assert.Contains("FRM spritesheet export cancelled", viewModel.StatusMessage);
        Assert.Contains("0 exported, 1 skipped, 0 failed", viewModel.StatusMessage);
    }

    [Fact]
    public async Task ExportFrmSpritesheetsAsync_DoesNotPublishStatusAfterArchiveReplacement()
    {
        using var archive = new ControlledArchive();
        archive.AddEntry("COLOR.PAL", new byte[FalloutPalette.MinimalFileSize]);
        var entry = archive.AddEntry(
            "art/critters/stale.frm",
            FrmFixture.Create([[new FrmFixtureFrame(1, 1, 0, 0, [11])]]));
        var read = archive.BlockEntry(entry.Path);
        using var viewModel = new ArchiveBrowserViewModel();
        using var destination = new TemporaryDirectory();
        using var replacement = new TemporaryArchive(CreateDat1Archive("other.bin"));
        SetPrivateField(viewModel, "_archive", archive);
        viewModel.SetEntries(archive.BrowserEntries);

        var export = viewModel.ExportFrmSpritesheetsAsync([entry], destination.Path);
        await read.WaitStartedAsync();
        await viewModel.LoadArchiveAsync(replacement.Path);
        var replacementStatus = viewModel.StatusMessage;
        read.Release();
        await export.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(replacementStatus, viewModel.StatusMessage);
        Assert.False(viewModel.IsExportingFrms);
        Assert.Empty(Directory.GetFiles(destination.Path));
    }

    [Fact]
    public async Task ExportFrmSpritesheetsAsync_DoesNotPublishStatusAfterViewModelDisposal()
    {
        using var archive = new ControlledArchive();
        archive.AddEntry("COLOR.PAL", new byte[FalloutPalette.MinimalFileSize]);
        var entry = archive.AddEntry(
            "art/critters/closed.frm",
            FrmFixture.Create([[new FrmFixtureFrame(1, 1, 0, 0, [11])]]));
        var read = archive.BlockEntry(entry.Path);
        var viewModel = new ArchiveBrowserViewModel();
        using var destination = new TemporaryDirectory();
        SetPrivateField(viewModel, "_archive", archive);
        viewModel.SetEntries(archive.BrowserEntries);

        var export = viewModel.ExportFrmSpritesheetsAsync([entry], destination.Path);
        await read.WaitStartedAsync();
        viewModel.Dispose();
        var disposedStatus = viewModel.StatusMessage;
        read.Release();
        await export.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(disposedStatus, viewModel.StatusMessage);
        Assert.False(viewModel.IsExportingFrms);
        Assert.Empty(Directory.GetFiles(destination.Path));
    }

    [Fact]
    public async Task PreviewSelectedEntryAsync_RejectsOversizedProBeforeReadingPayload()
    {
        using var archive = new ControlledArchive();
        var entry = archive.AddEntry(
            "proto/items/OVERSIZED.PRO",
            CreateItemPrototype(0x00000001),
            Fallout2PrototypeResolver.MaximumPrototypeSizeBytes + 1L);
        using var viewModel = new ArchiveBrowserViewModel();
        SetPrivateField(viewModel, "_archive", archive);
        viewModel.SetEntries(archive.BrowserEntries);
        viewModel.SelectedEntry = entry;

        await viewModel.PreviewSelectedEntryAsync();

        Assert.Equal(0, archive.CopyCount);
        Assert.Equal("PRO Inspector", viewModel.PreviewTitle);
        Assert.True(viewModel.IsProInspector);
        Assert.Contains("1 MiB preview limit", viewModel.StandaloneProStatus);
        Assert.Contains("The entry was not read.", viewModel.StandaloneProStatus);
        Assert.False(viewModel.HasStandaloneProRawBytes);
    }

    [Fact]
    public async Task PreviewSelectedEntryAsync_SupersededProReadCannotPublishOrClearNewerPreview()
    {
        var synchronizationContext = SynchronizationContext.Current;
        AvaloniaTestEnvironment.EnsureInitialized();
        SynchronizationContext.SetSynchronizationContext(synchronizationContext);
        using var archive = new ControlledArchive();
        var staleEntry = archive.AddEntry(
            "proto/items/STALE.PRO",
            CreateItemPrototype(0x00000001));
        var currentEntry = archive.AddEntry(
            "proto/items/CURRENT.PRO",
            CreateItemPrototype(0x00000002));
        var staleRead = archive.BlockEntry(staleEntry.Path);
        var currentRead = archive.BlockEntry(currentEntry.Path);
        using var viewModel = new ArchiveBrowserViewModel();
        SetPrivateField(viewModel, "_archive", archive);
        viewModel.SetEntries(archive.BrowserEntries);

        Task? stalePreview = null;
        Task? currentPreview = null;
        try
        {
            viewModel.SelectedEntry = staleEntry;
            stalePreview = viewModel.PreviewSelectedEntryAsync();
            await staleRead.WaitStartedAsync();

            viewModel.SelectedEntry = currentEntry;
            currentPreview = viewModel.PreviewSelectedEntryAsync();
            await currentRead.WaitStartedAsync();
            var currentCancellation = GetPrivateField<CancellationTokenSource?>(
                viewModel,
                "_previewCancellation");
            Assert.NotNull(currentCancellation);

            staleRead.Release();
            await stalePreview.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Same(
                currentCancellation,
                GetPrivateField<CancellationTokenSource?>(viewModel, "_previewCancellation"));
            Assert.True(viewModel.IsProInspector);
            Assert.Equal("Reading standalone PRO entry...", viewModel.StandaloneProStatus);

            currentRead.Release();
            await currentPreview.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(currentEntry.Path, viewModel.PreviewMessage);
            Assert.Contains(currentEntry.Path, viewModel.StandaloneProDetails);
            Assert.Contains("Embedded PID: 0x00000002", viewModel.StandaloneProDetails);
        }
        finally
        {
            staleRead.Release();
            currentRead.Release();
            if (stalePreview is not null)
            {
                await stalePreview.WaitAsync(TimeSpan.FromSeconds(10));
            }

            if (currentPreview is not null)
            {
                await currentPreview.WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreviewSelectedEntryAsync_StaleFrmReadCannotReplaceNewerPreviewOrCancellation(
        bool completeNewerPreviewBeforeReleasingStaleRead)
    {
        var synchronizationContext = SynchronizationContext.Current;
        AvaloniaTestEnvironment.EnsureInitialized();
        SynchronizationContext.SetSynchronizationContext(synchronizationContext);
        using var archive = new ControlledArchive();
        var firstFrm = archive.AddEntry(
            "ART/CRITTERS/FIRST.FRM",
            FrmFixture.Create([[new FrmFixtureFrame(1, 1, 0, 0, [11])]], framesPerSecond: 11));
        var secondFrm = archive.AddEntry(
            "ART/CRITTERS/SECOND.FRM",
            FrmFixture.Create(
            [[
                new FrmFixtureFrame(1, 1, 0, 0, [22]),
                new FrmFixtureFrame(1, 1, 0, 0, [23])
            ]],
            framesPerSecond: 17));
        archive.AddEntry("COLOR.PAL", new byte[FalloutPalette.MinimalFileSize]);
        var firstRead = archive.BlockEntry(firstFrm.Path);
        var secondRead = archive.BlockEntry(secondFrm.Path);
        using var viewModel = new ArchiveBrowserViewModel();
        SetPrivateField(viewModel, "_archive", archive);
        viewModel.SetEntries(archive.BrowserEntries);

        Task? firstPreview = null;
        Task? secondPreview = null;
        try
        {
            viewModel.SelectedEntry = firstFrm;
            firstPreview = viewModel.PreviewSelectedEntryAsync();
            await firstRead.WaitStartedAsync();

            viewModel.SelectedEntry = secondFrm;
            secondPreview = viewModel.PreviewSelectedEntryAsync();
            await secondRead.WaitStartedAsync();

            var secondCancellation = GetPrivateField<CancellationTokenSource?>(
                viewModel,
                "_previewCancellation");
            Assert.NotNull(secondCancellation);

            if (!completeNewerPreviewBeforeReleasingStaleRead)
            {
                firstRead.Release();
                await firstPreview.WaitAsync(TimeSpan.FromSeconds(10));

                Assert.Same(
                    secondCancellation,
                    GetPrivateField<CancellationTokenSource?>(viewModel, "_previewCancellation"));
                Assert.Equal("FRM Preview", viewModel.PreviewTitle);
                Assert.Equal("Reading and decoding FRM...", viewModel.PreviewMessage);
                Assert.Null(viewModel.FrmPreview.PreviewBitmap);
            }

            secondRead.Release();
            await secondPreview.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal("FRM Preview", viewModel.PreviewTitle);
            Assert.Equal(secondFrm.Path, viewModel.PreviewMessage);
            Assert.Equal(
                "Fallout 2 COLOR.PAL applied; visual color match is not verified.",
                viewModel.PaletteStatus);
            Assert.Equal("SECOND_NE_Frame01.png", viewModel.FrmPreview.SuggestedPngFileName);
            Assert.Contains("FRM rate metadata 17 FPS", viewModel.FrmPreview.PreviewDetails);
            Assert.Equal("Frame 1 / 2", viewModel.FrmPreview.CurrentFrameDisplay);
            Assert.Equal(new PixelSize(1, 1), viewModel.FrmPreview.PreviewBitmap?.PixelSize);

            if (completeNewerPreviewBeforeReleasingStaleRead)
            {
                var publishedBitmap = viewModel.FrmPreview.PreviewBitmap;
                var publishedTitle = viewModel.PreviewTitle;
                var publishedMessage = viewModel.PreviewMessage;
                var publishedPaletteStatus = viewModel.PaletteStatus;
                var publishedDetails = viewModel.FrmPreview.PreviewDetails;
                var publishedFileName = viewModel.FrmPreview.SuggestedPngFileName;
                var publishedFrameDisplay = viewModel.FrmPreview.CurrentFrameDisplay;
                var publishedCancellation = GetPrivateField<CancellationTokenSource?>(
                    viewModel,
                    "_previewCancellation");
                Assert.Null(publishedCancellation);

                firstRead.Release();
                await firstPreview.WaitAsync(TimeSpan.FromSeconds(10));

                Assert.Same(publishedBitmap, viewModel.FrmPreview.PreviewBitmap);
                Assert.Equal(publishedTitle, viewModel.PreviewTitle);
                Assert.Equal(publishedMessage, viewModel.PreviewMessage);
                Assert.Equal(publishedPaletteStatus, viewModel.PaletteStatus);
                Assert.Equal(publishedDetails, viewModel.FrmPreview.PreviewDetails);
                Assert.Equal(publishedFileName, viewModel.FrmPreview.SuggestedPngFileName);
                Assert.Equal(publishedFrameDisplay, viewModel.FrmPreview.CurrentFrameDisplay);
                Assert.Null(GetPrivateField<CancellationTokenSource?>(
                    viewModel,
                    "_previewCancellation"));

                using var bitmapOutput = new MemoryStream();
                viewModel.FrmPreview.PreviewBitmap!.Save(bitmapOutput);
                Assert.NotEmpty(bitmapOutput.ToArray());
            }
        }
        finally
        {
            firstRead.Release();
            secondRead.Release();
            if (firstPreview is not null)
            {
                await firstPreview.WaitAsync(TimeSpan.FromSeconds(10));
            }

            if (secondPreview is not null)
            {
                await secondPreview.WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrototypeDiagnostics_StaleSuccessOrExpectedIoCannotReplaceNewerSelection(
        bool failFirstPrototypeRead)
    {
        using var archive = new ControlledArchive();
        archive.AddEntry("proto/items/items.lst", Encoding.ASCII.GetBytes("first.pro\nsecond.pro\n"));
        var firstPid = 0x00000001;
        var secondPid = 0x00000002;
        var firstPrototype = archive.AddEntry(
            "proto/items/first.pro",
            CreateItemPrototype(firstPid));
        var secondPrototype = archive.AddEntry(
            "proto/items/second.pro",
            CreateItemPrototype(secondPid));
        var firstRead = archive.BlockEntry(firstPrototype.Path, failWithIo: failFirstPrototypeRead);
        var secondRead = archive.BlockEntry(secondPrototype.Path);
        var resolver = new Fallout2PrototypeResolver(archive);

        using var viewModel = new ArchiveBrowserViewModel();
        var firstPlacement = CreateItemPlacement(1, firstPid);
        var secondPlacement = CreateItemPlacement(2, secondPid);
        viewModel.MapObjectPlacements.Add(firstPlacement);
        viewModel.MapObjectPlacements.Add(secondPlacement);
        viewModel.MapInspector.SelectedMapObject = firstPlacement;
        SetPrivateField(viewModel, "_mapPrototypeResolver", resolver);

        using var firstCancellation = new CancellationTokenSource();
        SetPrivateField(viewModel, "_mapObjectPrototypeDiagnosticCancellation", firstCancellation);
        viewModel.MapInspector.SetMapObjectPrototypeDiagnostic(
            "Loading prototype diagnostics...",
            $"Selected object PID: 0x{unchecked((uint)firstPid):X8}",
            string.Empty,
            string.Empty,
            []);
        var firstDiagnostic = StartPrototypeDiagnosticLoad(
            viewModel,
            firstPlacement,
            resolver,
            firstCancellation);
        try
        {
            await firstRead.WaitStartedAsync();
            var secondDiagnosticReady = WaitForPrototypeFileAsync(
                viewModel.MapInspector,
                "second.pro");

            viewModel.MapInspector.SelectedMapObject = secondPlacement;
            await secondRead.WaitStartedAsync();

            var secondCancellation = GetPrivateField<CancellationTokenSource?>(
                viewModel,
                "_mapObjectPrototypeDiagnosticCancellation");
            Assert.NotNull(secondCancellation);

            firstRead.Release();
            await firstDiagnostic.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Same(
                secondCancellation,
                GetPrivateField<CancellationTokenSource?>(
                    viewModel,
                    "_mapObjectPrototypeDiagnosticCancellation"));
            Assert.Equal("Loading prototype diagnostics...", viewModel.MapInspector.MapObjectPrototypeDiagnosticStatus);
            Assert.Contains("0x00000002", viewModel.MapInspector.MapObjectPrototypeDiagnosticDetails);
            Assert.DoesNotContain("first.pro", viewModel.MapInspector.MapObjectPrototypeDiagnosticDetails);
            Assert.Equal(failFirstPrototypeRead, firstRead.FailedWithIo);

            secondRead.Release();
            await secondDiagnosticReady.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Contains("Verified file: second.pro", viewModel.MapInspector.MapObjectPrototypeDiagnosticDetails);
            Assert.Contains("Prototype PID: 0x00000002", viewModel.MapInspector.MapObjectPrototypeDiagnosticDetails);
            Assert.NotEmpty(viewModel.MapInspector.MapObjectPrototypeItemFieldsDisplay);
            Assert.NotEmpty(viewModel.MapInspector.MapObjectPrototypeRawByteLines);
        }
        finally
        {
            firstRead.Release();
            secondRead.Release();
            await firstDiagnostic.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private static byte[] CreateEmptyDat1Archive()
    {
        byte[] archive = new byte[16];
        return archive;
    }

    private static MapObjectPlacement CreateItemPlacement(int recordIndex, int rawPid) =>
        new(
            RecordIndex: recordIndex,
            RecordOffset: 0,
            ElevationGroup: 0,
            RawObjectId: 0,
            RawHexIndex: 20_099,
            HexX: 100,
            HexY: 100,
            RawOffsetX: 0,
            RawOffsetY: 0,
            RawScreenX: 0,
            RawScreenY: 0,
            RawFrame: 0,
            RawRotation: 0,
            RawFid: 0,
            RawObjectFlags: 0,
            StoredElevation: 0,
            RawPid: rawPid,
            PidTypeIdentifier: 0,
            RawCid: 0,
            RawLightDistance: 0,
            RawLightIntensity: 0,
            RawField74: 0,
            RawScriptId: 0,
            RawScriptField: 0,
            RawInventoryLength: 0,
            RawInventoryCapacity: 0,
            RawInventoryPointer: 0,
            RawPayloadWords: [],
            Warning: null,
            UnsupportedReason: null);

    private static byte[] CreateItemPrototype(int pid)
    {
        var bytes = new byte[0x45];
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(0, sizeof(int)), pid);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(0x20, sizeof(int)), 5);
        return bytes;
    }

    private static Task StartPrototypeDiagnosticLoad(
        ArchiveBrowserViewModel viewModel,
        MapObjectPlacement placement,
        Fallout2PrototypeResolver resolver,
        CancellationTokenSource cancellation)
    {
        var method = typeof(ArchiveBrowserViewModel).GetMethod(
            "LoadMapObjectPrototypeDiagnosticAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsAssignableFrom<Task>(
            method.Invoke(viewModel, [placement, resolver, cancellation]));
    }

    private static async Task WaitForPrototypeFileAsync(
        MapInspectorViewModel inspector,
        string fileName)
    {
        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        void OnPropertyChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (inspector.MapObjectPrototypeDiagnosticDetails.Contains(
                    $"Verified file: {fileName}",
                    StringComparison.OrdinalIgnoreCase))
            {
                completion.TrySetResult(true);
            }
        }

        inspector.PropertyChanged += OnPropertyChanged;
        try
        {
            OnPropertyChanged(inspector, new PropertyChangedEventArgs(
                nameof(inspector.MapObjectPrototypeDiagnosticDetails)));
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            inspector.PropertyChanged -= OnPropertyChanged;
        }
    }

    private static T GetPrivateField<T>(ArchiveBrowserViewModel viewModel, string name)
    {
        var field = typeof(ArchiveBrowserViewModel).GetField(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (T)field.GetValue(viewModel)!;
    }

    private static void SetPrivateField<T>(
        ArchiveBrowserViewModel viewModel,
        string name,
        T value)
    {
        var field = typeof(ArchiveBrowserViewModel).GetField(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(viewModel, value);
    }

    private static byte[] CreateDat1Archive(string entryPath, byte[]? payload = null)
    {
        var separator = entryPath.LastIndexOf('/');
        var folder = separator < 0 ? "." : entryPath[..separator];
        var fileName = separator < 0 ? entryPath : entryPath[(separator + 1)..];
        payload ??= new byte[] { 0x46, 0x52, 0x4D };
        using var stream = new MemoryStream();

        WriteUInt32BigEndian(stream, 1);
        stream.Write(new byte[12]);
        WriteName(stream, folder);
        WriteUInt32BigEndian(stream, 1);
        stream.Write(new byte[12]);
        WriteName(stream, fileName);
        WriteUInt32BigEndian(stream, Dat1Archive.RawEntryType);
        var offsetPosition = stream.Position;
        WriteUInt32BigEndian(stream, 0);
        WriteUInt32BigEndian(stream, checked((uint)payload.Length));
        WriteUInt32BigEndian(stream, checked((uint)payload.Length));

        var payloadOffset = checked((uint)stream.Position);
        stream.Position = offsetPosition;
        WriteUInt32BigEndian(stream, payloadOffset);
        stream.Position = stream.Length;
        stream.Write(payload);
        return stream.ToArray();
    }

    private static byte[] CreateValidInt()
    {
        byte[] identifierPayload = [0x41, 0x42, 0x43, 0x00];
        var procedureTableOffset = 42 + sizeof(int);
        var identifierTableOffset = procedureTableOffset + 6 * sizeof(int);
        var staticStringTableOffset = identifierTableOffset + sizeof(int) + identifierPayload.Length;
        var data = new byte[staticStringTableOffset + sizeof(int)];

        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(42), 1);
        int[] fields = [4, 0, 0x01020304, 0x11223344, 42, 0x55667788];
        for (var index = 0; index < fields.Length; index++)
        {
            BinaryPrimitives.WriteInt32BigEndian(
                data.AsSpan(procedureTableOffset + index * sizeof(int)),
                fields[index]);
        }

        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(identifierTableOffset), identifierPayload.Length);
        identifierPayload.CopyTo(data.AsSpan(identifierTableOffset + sizeof(int)));
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(staticStringTableOffset), 0x12345678);
        return data;
    }

    private static byte[] CreateValidEmptyMap()
    {
        var data = new byte[MapHeaderDecoder.HeaderSize + 5 * sizeof(int) + 4 * sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(data, 19);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(0x28), 0x0e);
        return data;
    }

    private static void WriteName(Stream stream, string value)
    {
        var bytes = Encoding.Latin1.GetBytes(value);
        stream.WriteByte(checked((byte)bytes.Length));
        stream.Write(bytes);
    }

    private static void WriteUInt32BigEndian(Stream stream, uint value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void AssertPngFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length >= 8);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, bytes[..8]);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"fae-frm-export-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    private sealed class TemporaryArchive : IDisposable
    {
        public TemporaryArchive(byte[] content)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"fae-command-test-{Guid.NewGuid():N}.dat");
            File.WriteAllBytes(Path, content);
        }

        public string Path { get; }

        public void Dispose() => File.Delete(Path);
    }

    private sealed class ThrowingWriteStream : MemoryStream
    {
        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException(new IOException("Synthetic write failure."));
    }

    private sealed class BlockingWriteStream : MemoryStream
    {
        public TaskCompletionSource<bool> WriteStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            WriteStarted.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            await base.WriteAsync(buffer, cancellationToken);
        }
    }

    private sealed class ControlledArchive : IGameArchive, IDisposable
    {
        private readonly Dictionary<string, (ArchiveEntry Entry, byte[] Bytes)> _entries =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly List<(ArchiveEntry Entry, byte[] Bytes)> _additionalEntries = [];
        private readonly Dictionary<string, ReadGate> _readGates =
            new(StringComparer.OrdinalIgnoreCase);
        private int _copyCount;

        public ControlledArchive(FalloutGame game = FalloutGame.Fallout2, string? path = null)
        {
            Game = game;
            Path = path
                ?? System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    $"fae-controlled-{Guid.NewGuid():N}.dat");
            File.WriteAllBytes(Path, []);
        }

        public string Path { get; }

        public FalloutGame Game { get; }

        public ArchiveFormat Format => Game == FalloutGame.Fallout1 ? ArchiveFormat.Dat1 : ArchiveFormat.Dat2;

        public IReadOnlyList<ArchiveEntry> BrowserEntries =>
            _entries.Values.Select(value => value.Entry)
                .Concat(_additionalEntries.Select(value => value.Entry))
                .ToArray();

        public int CopyCount => Volatile.Read(ref _copyCount);

        public ArchiveEntry AddEntry(string path, byte[] bytes, long? uncompressedSize = null)
        {
            var entry = new ArchiveEntry(path, uncompressedSize ?? bytes.LongLength, null, Path);
            _entries.Add(path, (entry, bytes));
            return entry;
        }

        public ArchiveEntry AddDuplicateEntry(string path, byte[] bytes)
        {
            var entry = new ArchiveEntry(path, bytes.LongLength, null, Path);
            _additionalEntries.Add((entry, bytes));
            return entry;
        }

        public ReadGate BlockEntry(string path, bool failWithIo = false)
        {
            var gate = new ReadGate(failWithIo);
            _readGates.Add(path, gate);
            return gate;
        }

        public async Task CopyEntryToAsync(
            ArchiveEntry entry,
            Stream destination,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _copyCount);
            byte[]? bytes = null;
            if (_entries.TryGetValue(entry.Path, out var item)
                && ReferenceEquals(item.Entry, entry))
            {
                bytes = item.Bytes;
            }
            else
            {
                foreach (var additionalEntry in _additionalEntries)
                {
                    if (ReferenceEquals(additionalEntry.Entry, entry))
                    {
                        bytes = additionalEntry.Bytes;
                        break;
                    }
                }
            }

            if (bytes is null)
            {
                throw new IOException($"Unexpected synthetic archive entry: {entry.Path}");
            }

            _readGates.TryGetValue(entry.Path, out var gate);
            try
            {
                if (gate is not null)
                {
                    gate.Started.TrySetResult(true);
                    await gate.Released.Task.ConfigureAwait(false);
                    if (gate.FailWithIo)
                    {
                        gate.FailedWithIo = true;
                        throw new IOException($"Synthetic I/O failure for {entry.Path}.");
                    }
                }

                await destination.WriteAsync(bytes, CancellationToken.None);
            }
            finally
            {
                gate?.CopyCompleted.TrySetResult(true);
            }
        }

        public void Dispose() => File.Delete(Path);

        public sealed class ReadGate(bool failWithIo)
        {
            public TaskCompletionSource<bool> Started { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<bool> Released { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<bool> CopyCompleted { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public bool FailWithIo { get; } = failWithIo;

            public bool FailedWithIo { get; set; }

            public Task WaitStartedAsync() => Started.Task.WaitAsync(TimeSpan.FromSeconds(10));

            public void Release() => Released.TrySetResult(true);
        }
    }
}
