using SteamAchievementManager.Schema;

namespace SteamAchievementManager.Manager;

// an achievement as currently stored on Steam
internal sealed class AchievementInfo(AchievementDefinition definition, bool isAchieved, DateTime? unlockTime)
{
    public AchievementDefinition Definition { get; } = definition;
    public bool IsAchieved { get; set; } = isAchieved;
    public DateTime? UnlockTime { get; } = unlockTime;

    // icon file name on the Steam CDN, also the key in the icon image list
    public string Icon => IsAchieved ? Definition.IconNormal : Definition.IconLocked;
}
