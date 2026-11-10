namespace SteamAchievementManager.Picker;

internal sealed class GameInfo(uint id, string type, string name)
{
    public uint Id { get; } = id;

    // "normal", "demo", "mod" or "junk"
    public string Type { get; } = type;

    public string Name { get; set => field = value ?? $"App {Id}"; } = name ?? $"App {id}";

    public string LogoUrl { get; set; }
    public Image Logo { get; set; }

    // ticked in the list
    public bool Checked { get; set; }
}
