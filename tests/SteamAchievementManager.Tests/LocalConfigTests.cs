using SteamAchievementManager.Steam;

namespace SteamAchievementManager.Tests;

public class LocalConfigTests
{
    [Fact]
    public void ReadsLastPlayedOfEachAppIncludingNestedSections()
    {
        const string text = """
            "UserLocalConfigStore"
            {
                // comment
                "Software" { "Valve" { "Steam" { "apps"
                {
                    "480"
                    {
                        "cloud" { "last_sync_state" "synchronized" }
                        "LastPlayed"   "1700000000"
                        "Playtime" "12"
                    }
                    "620" { "LastPlayed" "1600000000" }
                    "bad" { "LastPlayed" "1" }
                    "730" { "Note" "say \"hi\"" "LastPlayed" "1500000000" }
                } } } }
                "LastPlayed" "999"
            }
            """;

        var lastPlayed = LocalConfig.ReadLastPlayed(text);

        Assert.Equal(3, lastPlayed.Count);
        Assert.Equal(1700000000, lastPlayed[480]);
        Assert.Equal(1600000000, lastPlayed[620]);
        Assert.Equal(1500000000, lastPlayed[730]);
    }

    [Fact]
    public void ToleratesTruncatedText()
    {
        Assert.Empty(LocalConfig.ReadLastPlayed("\"apps\" { \"480\" { \"LastPlayed\""));
    }
}
