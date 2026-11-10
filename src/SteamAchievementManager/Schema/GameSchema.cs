using System.Globalization;
using SteamAchievementManager.Steam;

namespace SteamAchievementManager.Schema;

internal sealed record AchievementDefinition(
    string Id,
    string Name,
    string Description,
    string IconNormal,
    string IconLocked,
    int Permission)
{
    // protected achievements can only be set by the game's servers
    public bool IsProtected => (Permission & 3) != 0;
}

internal sealed record StatDefinition(
    string Id,
    string DisplayName,
    bool IsFloat,
    bool IncrementOnly,
    int Permission)
{
    public bool IsProtected => (Permission & 2) != 0;
}

internal enum StatType
{
    Invalid = 0,
    Integer = 1,
    Int = Integer,
    Float = 2,
    AverageRate = 3,
    Achievements = 4,
    GroupAchievements = 5
}

// a game's achievement and stat definitions, read from Steam's cached
// appcache/stats/UserGameStatsSchema_<appId>.bin
internal sealed class GameSchema
{
    public List<AchievementDefinition> Achievements { get; } = [];
    public List<StatDefinition> Stats { get; } = [];

    // null if the schema is missing or invalid; names are localized to the given language
    public static GameSchema Load(uint appId, string language) =>
        Load(Path.Combine(SteamLibrary.InstallPath ?? "", "appcache", "stats", $"UserGameStatsSchema_{appId}.bin"),
            appId, language);

    public static GameSchema Load(string path, uint appId, string language)
    {
        var stats = KeyValue.Load(path)?[appId.ToString(CultureInfo.InvariantCulture)]["stats"];
        if (stats == null || stats.Exists == false) return null;

        GameSchema schema = new();
        foreach (var stat in stats.Children)
        {
            var id = stat["name"].AsString("");
            var type = GetStatType(stat);
            switch (type)
            {
                case StatType.Integer:
                case StatType.Float:
                case StatType.AverageRate:
                    schema.Stats.Add(new StatDefinition(
                        id,
                        Localize(stat["display"]["name"], language, id),
                        type != StatType.Integer,
                        stat["incrementonly"].AsBoolean(false),
                        stat["permission"].AsInteger(0)));
                    break;

                case StatType.Achievements:
                case StatType.GroupAchievements:
                    foreach (var bits in stat.Children.Where(c =>
                                 string.Equals(c.Name, "bits", StringComparison.OrdinalIgnoreCase)))
                    foreach (var bit in bits.Children)
                    {
                        var bitId = bit["name"].AsString("");
                        var display = bit["display"];
                        var iconNormal = display["icon"].AsString("");
                        var iconLocked = display["icon_gray"].AsString("");
                        schema.Achievements.Add(new AchievementDefinition(
                            bitId,
                            Localize(display["name"], language, bitId),
                            Localize(display["desc"], language, ""),
                            iconNormal,
                            iconLocked.Length > 0 ? iconLocked : iconNormal,
                            bit["permission"].AsInteger(0)));
                    }

                    break;
            }
        }

        return schema;
    }

    private static StatType GetStatType(KeyValue stat)
    {
        // new format: "type" is a name; old format: "type_int" (or "type") is a number
        var type = stat["type"];
        if (type.Value is string name && Enum.TryParse(name, true, out StatType parsed) &&
            parsed != StatType.Invalid)
            return parsed;

        var typeInt = stat["type_int"];
        return (StatType)(typeInt.Exists ? typeInt : type).AsInteger(0);
    }

    // the value in the given language, then English, then the node's own value
    private static string Localize(KeyValue node, string language, string defaultValue)
    {
        foreach (var candidate in new[] { node[language], node["english"], node })
        {
            var value = candidate.AsString("");
            if (value.Length > 0) return value;
        }

        return defaultValue;
    }
}
