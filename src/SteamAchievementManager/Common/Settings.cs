using System.Text.Json;

namespace SteamAchievementManager.Common;

internal sealed record WindowPlacement(int X, int Y, int Width, int Height, bool IsMaximized);

// remembered between runs in %LOCALAPPDATA%\SteamAchievementManager\settings.json
internal sealed class Settings
{
    private static readonly string FilePath = Path.Combine(AppData.Folder, "settings.json");

    public WindowPlacement Picker { get; set; }
    public WindowPlacement Manager { get; set; }
    public bool ShowGames { get; set; } = true;
    public bool ShowDemos { get; set; }
    public bool ShowMods { get; set; }
    public bool ShowJunk { get; set; }
    public bool OnlyWithAchievements { get; set; }
    public string GameSort { get; set; } = "name";
    public string AchievementSort { get; set; } = "game";

    // a missing or broken file gives the defaults
    public static Settings Load()
    {
        try
        {
            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return new Settings();
        }
    }

    // re-reads the file first, so windows in other processes don't overwrite each other's settings
    public static void Update(Action<Settings> change)
    {
        var settings = Load();
        change(settings);
        try
        {
            Directory.CreateDirectory(AppData.Folder);
            File.WriteAllText(FilePath,
                JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // not being able to remember settings is not worth bothering the user about
        }
    }

    public static WindowPlacement PlacementOf(Form form)
    {
        var bounds = form.WindowState == FormWindowState.Normal ? form.Bounds : form.RestoreBounds;
        return new WindowPlacement(bounds.X, bounds.Y, bounds.Width, bounds.Height,
            form.WindowState == FormWindowState.Maximized);
    }

    // ignored when the saved spot is no longer on any screen (e.g. a monitor was unplugged)
    public static void Restore(Form form, WindowPlacement placement)
    {
        if (placement == null || placement.Width <= 0 || placement.Height <= 0) return;

        Rectangle bounds = new(placement.X, placement.Y, placement.Width, placement.Height);
        if (Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(bounds)) == false) return;

        form.StartPosition = FormStartPosition.Manual;
        form.Bounds = bounds;
        if (placement.IsMaximized) form.WindowState = FormWindowState.Maximized;
    }
}

internal static class AppData
{
    public static string Folder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SteamAchievementManager");
}