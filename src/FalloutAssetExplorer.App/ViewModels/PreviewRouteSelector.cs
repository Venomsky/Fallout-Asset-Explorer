namespace FalloutAssetExplorer.App.ViewModels;

public enum PreviewRoute
{
    Unsupported,
    Frm,
    Map,
    Pro,
    Msg,
    Lst,
    Int
}

public static class PreviewRouteSelector
{
    public static PreviewRoute Select(string? extension)
    {
        if (string.Equals(extension, ".FRM", StringComparison.OrdinalIgnoreCase))
        {
            return PreviewRoute.Frm;
        }

        if (string.Equals(extension, ".MAP", StringComparison.OrdinalIgnoreCase))
        {
            return PreviewRoute.Map;
        }

        if (string.Equals(extension, ".PRO", StringComparison.OrdinalIgnoreCase))
        {
            return PreviewRoute.Pro;
        }

        if (string.Equals(extension, ".MSG", StringComparison.OrdinalIgnoreCase))
        {
            return PreviewRoute.Msg;
        }

        if (string.Equals(extension, ".LST", StringComparison.OrdinalIgnoreCase))
        {
            return PreviewRoute.Lst;
        }

        if (string.Equals(extension, ".INT", StringComparison.OrdinalIgnoreCase))
        {
            return PreviewRoute.Int;
        }

        return PreviewRoute.Unsupported;
    }
}
