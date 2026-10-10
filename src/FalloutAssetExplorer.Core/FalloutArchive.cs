namespace FalloutAssetExplorer.Core;

public static class FalloutArchive
{
    public static IGameArchive Open(string path, CancellationToken cancellationToken = default)
    {
        InvalidDataException dat2Failure;
        try
        {
            return Dat2Archive.Open(path, cancellationToken);
        }
        catch (InvalidDataException exception)
        {
            dat2Failure = exception;
        }

        try
        {
            return Dat1Archive.Open(path, cancellationToken);
        }
        catch (InvalidDataException dat1Failure)
        {
            throw new InvalidDataException(
                $"The file is not a valid supported Fallout DAT1 or DAT2 archive. "
                + $"DAT2 validation failed: {dat2Failure.Message} "
                + $"DAT1 validation failed: {dat1Failure.Message}",
                new AggregateException(dat2Failure, dat1Failure));
        }
    }
}
