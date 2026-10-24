namespace XingyuConnect;

internal static class BrandAssets
{
    // These shared resources remain alive for the lifetime of the application.
    public static Bitmap Logo { get; }
    public static Icon WindowIcon { get; }

    static BrandAssets()
    {
        var assembly = typeof(BrandAssets).Assembly;
        using var logoStream = assembly.GetManifestResourceStream("XingyuConnect.Branding.logo-icon.png")!;
        using var source = Image.FromStream(logoStream);
        Logo = new Bitmap(source);
        using var iconStream = assembly.GetManifestResourceStream("XingyuConnect.Branding.app.ico")!;
        WindowIcon = new Icon(iconStream);
    }
}
