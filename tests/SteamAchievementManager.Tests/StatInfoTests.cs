using System.Globalization;
using SteamAchievementManager.Manager;
using SteamAchievementManager.Schema;

namespace SteamAchievementManager.Tests;

public class StatInfoTests
{
    private static StatInfo Stat(object value, bool isFloat = false, int permission = 0, bool incrementOnly = false)
    {
        return new StatInfo(new StatDefinition("ID", "Name", isFloat, incrementOnly, permission), value);
    }

    [Fact]
    public void ParsesEditedTextAndTracksChanges()
    {
        var stat = Stat(5);
        Assert.False(stat.IsModified);

        stat.Value = "7";
        Assert.Equal(7, stat.Value);
        Assert.True(stat.IsModified);

        stat.Value = "5";
        Assert.False(stat.IsModified);
    }

    [Fact]
    public void ParsesFloatsInTheCurrentCulture()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var stat = Stat(1.5f, true);
        stat.Value = "2.25";
        Assert.Equal(2.25f, stat.Value);
    }

    [Fact]
    public void RejectsInvalidText()
    {
        Assert.Throws<FormatException>(() => Stat(1).Value = "abc");
    }

    [Fact]
    public void ProtectedStatsRejectChangesButAllowTheSameValue()
    {
        var stat = Stat(3, permission: 2);
        stat.Value = "3";
        Assert.Throws<StatIsProtectedException>(() => stat.Value = "4");
        Assert.Equal(3, stat.Value);
    }

    [Theory]
    [InlineData(0, false, "None")]
    [InlineData(0, true, "IncrementOnly")]
    [InlineData(2, false, "Protected")]
    [InlineData(6, true, "IncrementOnly, Protected, UnknownPermission")]
    public void DescribesFlags(int permission, bool incrementOnly, string expected)
    {
        Assert.Equal(expected, Stat(0, permission: permission, incrementOnly: incrementOnly).Extra);
    }
}