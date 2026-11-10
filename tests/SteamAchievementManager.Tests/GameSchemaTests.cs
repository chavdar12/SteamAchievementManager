using SteamAchievementManager.Schema;

namespace SteamAchievementManager.Tests;

public class GameSchemaTests
{
    private const uint AppId = 480;

    private static GameSchema Load(Action<KeyValueWriter> stats, string language = "english")
    {
        var writer = new KeyValueWriter().Begin($"{AppId}").Begin("stats");
        stats(writer);
        return GameSchema.Load(writer.End().End().End().Save(), AppId, language);
    }

    [Fact]
    public void ReadsStatsInTheNewFormat()
    {
        var schema = Load(w => w
            .Begin("1").String("name", "KILLS").String("type", "int").String("incrementonly", "1")
            .Int("permission", 2)
            .Begin("display").Begin("name").String("english", "Kills").End().End()
            .End()
            .Begin("2").String("name", "DISTANCE").String("type", "float").End());

        var kills = Assert.Single(schema.Stats, s => s.Id == "KILLS");
        Assert.Equal("Kills", kills.DisplayName);
        Assert.False(kills.IsFloat);
        Assert.True(kills.IncrementOnly);
        Assert.True(kills.IsProtected);

        var distance = Assert.Single(schema.Stats, s => s.Id == "DISTANCE");
        Assert.True(distance.IsFloat);
        Assert.Equal("DISTANCE", distance.DisplayName); // no display name: falls back to the ID
    }

    [Fact]
    public void ReadsStatsInTheOldFormat()
    {
        var schema = Load(w => w
            .Begin("1").String("name", "A").Int("type_int", 1).End()
            .Begin("2").String("name", "B").Int("type", 3).End()); // average rate is a float

        Assert.False(schema.Stats.Single(s => s.Id == "A").IsFloat);
        Assert.True(schema.Stats.Single(s => s.Id == "B").IsFloat);
    }

    [Fact]
    public void ReadsLocalizedAchievementsWithIconFallback()
    {
        var schema = Load(w => w
            .Begin("3").String("type", "achievements")
            .Begin("bits")
            .Begin("0").String("name", "WIN").Int("permission", 0)
            .Begin("display")
            .Begin("name").String("english", "Win").String("german", "Sieg").End()
            .Begin("desc").String("english", "Win a game").End()
            .String("icon", "win.jpg")
            .End()
            .End()
            .End()
            .End(), language: "german");

        var win = Assert.Single(schema.Achievements);
        Assert.Equal("WIN", win.Id);
        Assert.Equal("Sieg", win.Name);
        Assert.Equal("Win a game", win.Description); // no German text: English
        Assert.Equal("win.jpg", win.IconNormal);
        Assert.Equal("win.jpg", win.IconLocked); // no gray icon: the normal one
        Assert.False(win.IsProtected);
    }

    [Fact]
    public void SkipsUnknownStatTypes()
    {
        var schema = Load(w => w.Begin("1").String("name", "X").Int("type", 99).End());
        Assert.Empty(schema.Stats);
        Assert.Empty(schema.Achievements);
    }

    [Fact]
    public void ReturnsNullWithoutAStatsSection()
    {
        var path = new KeyValueWriter().Begin($"{AppId}").End().End().Save();
        Assert.Null(GameSchema.Load(path, AppId, "english"));
    }
}
