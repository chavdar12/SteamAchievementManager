namespace SteamAchievementManager.Picker;

internal sealed class GameInfo(uint id, string type, string name)
{
    public uint Id { get; } = id;

    // "normal", "demo", "mod" or "junk"
    public string Type { get; } = type;

    public string Name
    {
        get;
        set => field = value ?? $"App {Id}";
    } = name ?? $"App {id}";

    public string LogoUrl { get; set; }
    public Image Logo { get; set; }

    // Steam has this game's achievement definitions cached on this PC (so it can be managed)
    public bool HasAchievements { get; set; }

    // Unix time; 0 if never played or unknown
    public long LastPlayed { get; set; }

    // ticked in the list
    public bool Checked { get; set; }
}