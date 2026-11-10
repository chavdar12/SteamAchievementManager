namespace SteamAchievementManager.Common;

// the images in the Assets folder, embedded by file name
internal static class Assets
{
    public static Icon App { get; } = new(Open("app.ico"));
    public static Bitmap Download { get; } = Image("download.png");
    public static Bitmap Filter { get; } = Image("filter.png");
    public static Bitmap Invert { get; } = Image("invert.png");
    public static Bitmap Lock { get; } = Image("lock.png");
    public static Bitmap Refresh { get; } = Image("refresh.png");
    public static Bitmap Reset { get; } = Image("reset.png");
    public static Bitmap Save { get; } = Image("save.png");
    public static Bitmap Search { get; } = Image("search.png");
    public static Bitmap Unlock { get; } = Image("unlock.png");

    private static Bitmap Image(string name) => new(Open(name));

    private static Stream Open(string name) => typeof(Assets).Assembly.GetManifestResourceStream(name);
}
