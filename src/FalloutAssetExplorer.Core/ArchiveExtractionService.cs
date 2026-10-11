namespace FalloutAssetExplorer.Core;

public enum OverwritePolicy
{
    SkipExisting,
    OverwriteExisting,
    CancelOperation
}

public sealed record ExtractionProgress(
    int CompletedEntries,
    int TotalEntries,
    string? CurrentEntryPath,
    string? Message = null);

public sealed record ExtractionEntryResult(
    ArchiveEntry Entry,
    string? OutputPath,
    ExtractionStatus Status,
    string? Message)
{
    public bool Succeeded => Status == ExtractionStatus.Success;
}

public enum ExtractionStatus
{
    Success,
    Skipped,
    Failed,
    Cancelled
}

public sealed record ExtractionSummary(
    int TotalEntries,
    int Successful,
    int Skipped,
    int Failed,
    int Cancelled,
    bool WasCancelled)
{
    public bool HasFailures => Failed > 0 || Cancelled > 0;

    public IReadOnlyList<ExtractionEntryResult> Results { get; init; } = [];
}

public sealed record ExtractionOptions(
    OverwritePolicy OverwritePolicy = OverwritePolicy.SkipExisting,
    bool PreserveRelativeDirectories = true);

public sealed class ArchiveExtractionService
{
    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public async Task<ExtractionSummary> ExtractAsync(
        IGameArchive archive,
        IReadOnlyList<ArchiveEntry> entries,
        string destinationDirectory,
        ExtractionOptions? options = null,
        IProgress<ExtractionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);

        var extractionOptions = options ?? new ExtractionOptions();
        var entryList = entries.ToList();
        if (entryList.Count == 0)
        {
            return new ExtractionSummary(0, 0, 0, 0, 0, false);
        }

        var rootDirectory = Path.GetFullPath(destinationDirectory);
        var uniqueOutputPaths = new HashSet<string>(PathComparer);
        var results = new List<ExtractionEntryResult>(entryList.Count);
        var successful = 0;
        var skipped = 0;
        var failed = 0;
        var cancelled = 0;

        try
        {
            if (cancellationToken.IsCancellationRequested)
            {
                MarkRemainingEntriesCancelled(entryList, 0, results, ref cancelled, progress);
                return CreateSummary(entryList.Count, successful, skipped, failed, cancelled, true, results);
            }

            Directory.CreateDirectory(rootDirectory);
            for (var index = 0; index < entryList.Count; index++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    MarkRemainingEntriesCancelled(entryList, index, results, ref cancelled, progress);
                    break;
                }

                var entry = entryList[index];
                progress?.Report(new ExtractionProgress(results.Count, entryList.Count, entry.Path));
                var resolvedPath = ResolveOutputPath(rootDirectory, entry.Path, extractionOptions.PreserveRelativeDirectories);
                if (resolvedPath is null)
                {
                    skipped++;
                    AddResult(entry, null, ExtractionStatus.Skipped, "Unsafe archive path.", results, progress, entryList.Count);
                    continue;
                }

                if (!uniqueOutputPaths.Add(resolvedPath))
                {
                    skipped++;
                    AddResult(entry, resolvedPath, ExtractionStatus.Skipped, "Duplicate output path.", results, progress, entryList.Count);
                    continue;
                }

                string? tempPath = null;
                try
                {
                    EnsureNoLinksWithinRoot(rootDirectory, resolvedPath);
                    if (Directory.Exists(resolvedPath))
                    {
                        throw new IOException("The extraction destination is an existing directory.");
                    }

                    if (File.Exists(resolvedPath))
                    {
                        switch (extractionOptions.OverwritePolicy)
                        {
                            case OverwritePolicy.SkipExisting:
                                skipped++;
                                AddResult(entry, resolvedPath, ExtractionStatus.Skipped, "File already exists.", results, progress, entryList.Count);
                                continue;
                            case OverwritePolicy.CancelOperation:
                                MarkRemainingEntriesCancelled(
                                    entryList,
                                    index,
                                    results,
                                    ref cancelled,
                                    progress,
                                    currentEntryMessage: "Extraction cancelled by the overwrite policy.");
                                return CreateSummary(entryList.Count, successful, skipped, failed, cancelled, true, results);
                        }
                    }

                    var destinationDirectoryPath = Path.GetDirectoryName(resolvedPath)
                        ?? throw new IOException("The extraction destination has no parent directory.");
                    Directory.CreateDirectory(destinationDirectoryPath);
                    EnsureNoLinksWithinRoot(rootDirectory, resolvedPath);
                    tempPath = CreateTemporaryFilePath(rootDirectory);

                    await using (var output = new FileStream(
                        tempPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        64 * 1024,
                        FileOptions.Asynchronous | FileOptions.SequentialScan))
                    {
                        await archive.CopyEntryToAsync(entry, output, cancellationToken);
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    EnsureNoLinksWithinRoot(rootDirectory, resolvedPath);
                    if (File.Exists(resolvedPath))
                    {
                        if (extractionOptions.OverwritePolicy != OverwritePolicy.OverwriteExisting)
                        {
                            skipped++;
                            DeleteTemporaryFile(tempPath);
                            tempPath = null;
                            AddResult(entry, resolvedPath, ExtractionStatus.Skipped, "File appeared during extraction.", results, progress, entryList.Count);
                            continue;
                        }

                        cancellationToken.ThrowIfCancellationRequested();
                        File.Move(tempPath, resolvedPath, overwrite: true);
                    }
                    else
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        File.Move(tempPath, resolvedPath);
                    }

                    tempPath = null;
                    successful++;
                    AddResult(entry, resolvedPath, ExtractionStatus.Success, null, results, progress, entryList.Count);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    var cleanupError = TryDeleteTemporaryFile(tempPath);
                    MarkRemainingEntriesCancelled(
                        entryList,
                        index,
                        results,
                        ref cancelled,
                        progress,
                        currentEntryMessage: AppendCleanupError("Extraction was cancelled.", cleanupError));
                    break;
                }
                catch (Exception exception)
                {
                    failed++;
                    var cleanupError = TryDeleteTemporaryFile(tempPath);
                    AddResult(
                        entry,
                        resolvedPath,
                        ExtractionStatus.Failed,
                        $"{exception.GetType().Name}: {exception.Message}{FormatCleanupError(cleanupError)}",
                        results,
                        progress,
                        entryList.Count);
                }
            }
        }
        catch (Exception exception)
        {
            failed += entryList.Count - results.Count;
            for (var index = results.Count; index < entryList.Count; index++)
            {
                results.Add(new ExtractionEntryResult(
                    entryList[index],
                    null,
                    ExtractionStatus.Failed,
                    $"{exception.GetType().Name}: {exception.Message}"));
            }
        }

        return CreateSummary(entryList.Count, successful, skipped, failed, cancelled, cancelled > 0, results);
    }

    private static ExtractionSummary CreateSummary(
        int total,
        int successful,
        int skipped,
        int failed,
        int cancelled,
        bool wasCancelled,
        IReadOnlyList<ExtractionEntryResult> results) =>
        new(total, successful, skipped, failed, cancelled, wasCancelled)
        {
            Results = results.ToArray()
        };

    private static void MarkRemainingEntriesCancelled(
        IReadOnlyList<ArchiveEntry> entries,
        int startIndex,
        List<ExtractionEntryResult> results,
        ref int cancelled,
        IProgress<ExtractionProgress>? progress,
        string currentEntryMessage = "Not processed because extraction was cancelled.")
    {
        for (var index = startIndex; index < entries.Count; index++)
        {
            var entry = entries[index];
            var message = index == startIndex
                ? currentEntryMessage
                : "Not started because extraction was cancelled.";
            results.Add(new ExtractionEntryResult(entry, null, ExtractionStatus.Cancelled, message));
            cancelled++;
            progress?.Report(new ExtractionProgress(
                results.Count,
                entries.Count,
                entry.Path,
                message));
        }
    }

    private static void AddResult(
        ArchiveEntry entry,
        string? outputPath,
        ExtractionStatus status,
        string? message,
        List<ExtractionEntryResult> results,
        IProgress<ExtractionProgress>? progress,
        int total)
    {
        results.Add(new ExtractionEntryResult(entry, outputPath, status, message));
        progress?.Report(new ExtractionProgress(
            results.Count,
            total,
            entry.Path,
            status switch
            {
                ExtractionStatus.Success => "Extracted successfully.",
                ExtractionStatus.Skipped => $"Skipped: {message}",
                ExtractionStatus.Failed => "Failed: see extraction diagnostics.",
                _ => message
            }));
    }

    public static string? ResolveOutputPath(
        string destinationDirectory,
        string archiveRelativePath,
        bool preserveRelativeDirectories = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(archiveRelativePath);

        var normalized = NormalizeArchiveRelativePath(archiveRelativePath);
        if (normalized is null)
        {
            return null;
        }

        var rootDirectory = Path.GetFullPath(destinationDirectory);
        if (!preserveRelativeDirectories)
        {
            var fileName = Path.GetFileName(normalized);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return null;
            }

            return Path.GetFullPath(Path.Combine(rootDirectory, fileName));
        }

        var candidate = Path.Combine(rootDirectory, normalized.Replace('/', Path.DirectorySeparatorChar));
        var resolved = Path.GetFullPath(candidate);
        if (!IsContainedWithin(rootDirectory, resolved))
        {
            return null;
        }

        return resolved;
    }

    public static bool IsSafeRelativeArchivePath(string archiveRelativePath, out string? error)
    {
        error = NormalizeArchiveRelativePath(archiveRelativePath) is null
            ? "The archive path is not safe for filesystem extraction."
            : null;
        return error is null;
    }

    private static string? NormalizeArchiveRelativePath(string archiveRelativePath)
    {
        if (string.IsNullOrWhiteSpace(archiveRelativePath))
        {
            return null;
        }

        var replaced = archiveRelativePath.Replace('\\', '/');
        if (replaced.Length == 0 || replaced is "." or "/")
        {
            return null;
        }

        if (replaced.StartsWith("/", StringComparison.Ordinal)
            || replaced.StartsWith("\\", StringComparison.Ordinal)
            || replaced.StartsWith("//", StringComparison.Ordinal)
            || replaced.StartsWith("\\\\", StringComparison.Ordinal)
            || (replaced.Length >= 2 && char.IsLetter(replaced[0]) && replaced[1] == ':'))
        {
            return null;
        }

        var segments = replaced.Split('/');
        if (segments.Length == 0)
        {
            return null;
        }

        foreach (var segment in segments)
        {
            if (string.IsNullOrEmpty(segment)
                || segment == ".."
                || segment == ".")
            {
                return null;
            }

            if (segment.Contains('\\'))
            {
                return null;
            }

            if (segment.IndexOfAny(['<', '>', ':', '"', '|', '?', '*']) >= 0)
            {
                return null;
            }

            if (segment.Any(char.IsControl))
            {
                return null;
            }

            if (segment.EndsWith('.') || segment.EndsWith(' '))
            {
                return null;
            }

            if (segment.Length > 255)
            {
                return null;
            }

            if (IsWindowsReservedDeviceName(segment))
            {
                return null;
            }
        }

        return string.Join(Path.DirectorySeparatorChar.ToString(), segments);
    }

    private static bool IsWindowsReservedDeviceName(string segment)
    {
        var trimmed = segment.TrimEnd('.');
        if (trimmed.Length == 0)
        {
            return true;
        }

        if (trimmed.Length > 4 && trimmed.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
        {
            var rest = trimmed[3..];
            if (rest.Length == 0 || (rest.Length == 1 && char.IsDigit(rest[0])))
            {
                return true;
            }
        }

        if (trimmed.Length > 4 && trimmed.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
        {
            var rest = trimmed[3..];
            if (rest.Length == 0 || (rest.Length == 1 && char.IsDigit(rest[0])))
            {
                return true;
            }
        }

        return trimmed.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("COM1", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("COM2", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("COM3", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("COM4", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("COM5", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("COM6", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("COM7", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("COM8", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("COM9", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("LPT1", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("LPT2", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("LPT3", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("LPT4", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("LPT5", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("LPT6", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("LPT7", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("LPT8", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("LPT9", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsContainedWithin(string rootDirectory, string candidatePath)
    {
        var rootFull = Path.GetFullPath(rootDirectory);
        var candidateFull = Path.GetFullPath(candidatePath);
        if (string.Equals(rootFull, candidateFull, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var relative = Path.GetRelativePath(rootFull, candidateFull);
        return !relative.StartsWith("..", StringComparison.Ordinal)
            && !Path.IsPathRooted(relative)
            && relative != ".";
    }

    private static void EnsureNoLinksWithinRoot(string rootDirectory, string targetPath)
    {
        var relativePath = Path.GetRelativePath(rootDirectory, targetPath);
        if (relativePath == "."
            || Path.IsPathRooted(relativePath)
            || relativePath == ".."
            || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new IOException("The resolved extraction path is outside the selected destination.");
        }

        var currentPath = rootDirectory;
        foreach (var component in relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            currentPath = Path.Combine(currentPath, component);
            FileAttributes? attributes;
            try
            {
                attributes = File.GetAttributes(currentPath);
            }
            catch (FileNotFoundException)
            {
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }

            if (attributes.HasValue && (attributes.Value & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException($"The extraction path traverses a symbolic link or filesystem junction: '{currentPath}'.");
            }
        }
    }

    private static string CreateTemporaryFilePath(string rootDirectory)
    {
        var tempFolder = Path.Combine(rootDirectory, ".fae-extract");
        EnsureNoLinksWithinRoot(rootDirectory, tempFolder);
        Directory.CreateDirectory(tempFolder);
        EnsureNoLinksWithinRoot(rootDirectory, tempFolder);
        return Path.Combine(tempFolder, $"{Guid.NewGuid():N}.tmp");
    }

    private static void DeleteTemporaryFile(string path)
    {
        File.Delete(path);
    }

    private static Exception? TryDeleteTemporaryFile(string? path)
    {
        if (path is null)
        {
            return null;
        }

        try
        {
            File.Delete(path);
            return null;
        }
        catch (IOException exception)
        {
            return exception;
        }
        catch (UnauthorizedAccessException exception)
        {
            return exception;
        }
    }

    private static string FormatCleanupError(Exception? exception) =>
        exception is null
            ? string.Empty
            : $" Temporary-file cleanup also failed: {exception.GetType().Name}: {exception.Message}";

    private static string AppendCleanupError(string message, Exception? exception) =>
        $"{message}{FormatCleanupError(exception)}";
}
