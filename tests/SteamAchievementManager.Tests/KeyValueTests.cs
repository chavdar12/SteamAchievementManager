using SteamAchievementManager.Schema;

namespace SteamAchievementManager.Tests;

public class KeyValueTests
{
    private static KeyValue Load(KeyValueWriter writer)
    {
        return KeyValue.Load(writer.Save());
    }

    [Fact]
    public void ReadsNestedValuesOfEveryType()
    {
        var root = Load(new KeyValueWriter()
            .Begin("game")
            .String("name", "Spacewar").Int("count", 42).Float("ratio", 1.5f).UInt64("big", 0x1_0000_0007)
            .Begin("child").String("leaf", "ü").End()
            .End()
            .End());

        var game = root["game"];
        Assert.Equal("Spacewar", game["name"].AsString(""));
        Assert.Equal(42, game["count"].AsInteger(0));
        Assert.Equal(1.5f, game["ratio"].AsFloat(0));
        Assert.Equal(7, game["big"].AsInteger(0)); // low 32 bits
        Assert.Equal("ü", game["child"]["leaf"].AsString(""));
        Assert.Equal(5, game.Children.Count);
    }

    [Fact]
    public void LookupIsCaseInsensitive()
    {
        var root = Load(new KeyValueWriter().String("Name", "x").End());
        Assert.Equal("x", root["NAME"].AsString(""));
    }

    [Fact]
    public void MissingKeysChainAndReturnDefaults()
    {
        var root = Load(new KeyValueWriter().String("a", "1").End());
        var missing = root["nope"]["deeper"];
        Assert.False(missing.Exists);
        Assert.Equal("d", missing.AsString("d"));
        Assert.Equal(7, missing.AsInteger(7));
        Assert.Empty(missing.Children);
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("junk", true)] // falls back to the default
    public void ParsesBooleansFromStrings(string value, bool expected)
    {
        var root = Load(new KeyValueWriter().String("flag", value).End());
        Assert.Equal(expected, root["flag"].AsBoolean(true));
    }

    [Fact]
    public void RejectsTruncatedAndTrailingData()
    {
        var bytes = new KeyValueWriter().Begin("a").Int("b", 1).End().End().ToArray();

        var truncated = Path.GetTempFileName();
        File.WriteAllBytes(truncated, bytes[..^1]);
        Assert.Null(KeyValue.Load(truncated));

        var trailing = Path.GetTempFileName();
        File.WriteAllBytes(trailing, [.. bytes, 0xFF]);
        Assert.Null(KeyValue.Load(trailing));
    }

    [Fact]
    public void RejectsUnknownNodeTypesAndMissingFiles()
    {
        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, [42, (byte)'a', 0]);
        Assert.Null(KeyValue.Load(path));
        Assert.Null(KeyValue.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())));
    }
}