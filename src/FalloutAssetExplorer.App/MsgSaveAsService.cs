namespace FalloutAssetExplorer.App;

public static class MsgSaveAsService
{
    public static async Task<string?> SaveAsync(
        byte[]? patchedOutputBytes,
        string? destinationPath,
        string? originalSourcePath,
        bool overwriteConfirmed,
        CancellationToken cancellationToken = default)
    {
        if (destinationPath is null)
        {
            return null;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(patchedOutputBytes);
        cancellationToken.ThrowIfCancellationRequested();

        var fullDestinationPath = Path.GetFullPath(destinationPath);
        if (!string.IsNullOrWhiteSpace(originalSourcePath))
        {
            var fullOriginalSourcePath = Path.GetFullPath(originalSourcePath);
            var pathComparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (string.Equals(
                    fullDestinationPath,
                    fullOriginalSourcePath,
                    pathComparison))
            {
                throw new IOException(
                    "The Save As destination is the original source file. Choose a different path.");
            }
        }

        var outputSnapshot = patchedOutputBytes.ToArray();
        await SpriteExportFileWriter.WriteAtomicallyAsync(
            fullDestinationPath,
            overwriteConfirmed,
            async (stream, token) =>
                await stream.WriteAsync(outputSnapshot.AsMemory(), token),
            cancellationToken);

        return fullDestinationPath;
    }
}
