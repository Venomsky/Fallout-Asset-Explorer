namespace FalloutAssetExplorer.App;

public static class MapSaveAsService
{
    public static async Task<string?> SaveAsync(
        byte[]? outputBytes,
        string? destinationPath,
        string? originalArchivePath,
        bool overwriteConfirmed,
        CancellationToken cancellationToken = default)
    {
        if (destinationPath is null)
        {
            return null;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(outputBytes);
        cancellationToken.ThrowIfCancellationRequested();

        var fullDestinationPath = Path.GetFullPath(destinationPath);
        if (!string.IsNullOrWhiteSpace(originalArchivePath))
        {
            var fullOriginalArchivePath = Path.GetFullPath(originalArchivePath);
            var pathComparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (string.Equals(
                    fullDestinationPath,
                    fullOriginalArchivePath,
                    pathComparison))
            {
                throw new IOException(
                    "The MAP Save As destination is the original archive. Choose a different path.");
            }
        }

        var outputSnapshot = outputBytes.ToArray();
        await SpriteExportFileWriter.WriteAtomicallyAsync(
            fullDestinationPath,
            overwriteConfirmed,
            async (stream, token) =>
                await stream.WriteAsync(outputSnapshot.AsMemory(), token),
            cancellationToken);

        return fullDestinationPath;
    }
}
