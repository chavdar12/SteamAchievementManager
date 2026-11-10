namespace SteamAchievementManager.Picker;

internal sealed class GameInfo(uint id, string type, string name)
{
    public uint Id { get; } = id;

    // "normal", "demo", "mod" or "junk"
    public string Type { get; } = type;

    public string Name { get; set => field = value ?? $"App {Id}"; } = name ?? $"App {id}";

    public string ImageUrl { get; set; }
    public int ImageIndex { get; set; }

    // the virtual list item last created for this game
    public ListViewItem Item { get; set; }
}
