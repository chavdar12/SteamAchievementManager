namespace SteamAchievementManager.Common;

// the app icon from the Assets folder, embedded by file name; toolbar icons come from Glyphs
internal static class Assets
{
    public static Icon App { get; } = new(typeof(Assets).Assembly.GetManifestResourceStream("app.ico")!);
}