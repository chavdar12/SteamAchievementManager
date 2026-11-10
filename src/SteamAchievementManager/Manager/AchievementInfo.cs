using SteamAchievementManager.Schema;

namespace SteamAchievementManager.Manager;

// an achievement as stored on Steam, plus the state ticked in the list
internal sealed class AchievementInfo(AchievementDefinition definition, bool isAchieved, DateTime? unlockTime)
{
    public AchievementDefinition Definition { get; } = definition;
    public bool IsAchieved { get; set; } = isAchieved;
    public DateTime? UnlockTime { get; } = unlockTime;

    // the state to store; differs from IsAchieved while the change is unsaved
    public bool Checked { get; set; } = isAchieved;
    public bool IsModified => Checked != IsAchieved;

    // names starting with '#' are untranslated tokens; show the ID instead
    public bool HasName => string.IsNullOrEmpty(Definition.Name) == false && Definition.Name.StartsWith('#') == false;
    public string DisplayName => HasName ? Definition.Name : Definition.Id;
    public string Description => HasName ? Definition.Description ?? "" : "";

    // icon file name on the Steam CDN
    public string Icon => IsAchieved ? Definition.IconNormal : Definition.IconLocked;
}