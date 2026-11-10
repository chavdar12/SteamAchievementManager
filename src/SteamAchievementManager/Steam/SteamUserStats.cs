using System.Runtime.InteropServices;

namespace SteamAchievementManager.Steam;

[UnmanagedFunctionPointer(CallingConvention.ThisCall)]
[return: MarshalAs(UnmanagedType.I1)]
internal delegate bool GetStatIntFn(IntPtr self, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, out int value);

[UnmanagedFunctionPointer(CallingConvention.ThisCall)]
[return: MarshalAs(UnmanagedType.I1)]
internal delegate bool GetStatFloatFn(IntPtr self, [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
    out float value);

[UnmanagedFunctionPointer(CallingConvention.ThisCall)]
[return: MarshalAs(UnmanagedType.I1)]
internal delegate bool SetStatIntFn(IntPtr self, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int value);

[UnmanagedFunctionPointer(CallingConvention.ThisCall)]
[return: MarshalAs(UnmanagedType.I1)]
internal delegate bool SetStatFloatFn(IntPtr self, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, float value);

[UnmanagedFunctionPointer(CallingConvention.ThisCall)]
[return: MarshalAs(UnmanagedType.I1)]
internal delegate bool AchievementFn(IntPtr self, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

[UnmanagedFunctionPointer(CallingConvention.ThisCall)]
[return: MarshalAs(UnmanagedType.I1)]
internal delegate bool GetAchievementAndUnlockTimeFn(IntPtr self,
    [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
    [MarshalAs(UnmanagedType.I1)] out bool isAchieved, out uint unlockTime);

[UnmanagedFunctionPointer(CallingConvention.ThisCall)]
[return: MarshalAs(UnmanagedType.I1)]
internal delegate bool StoreStatsFn(IntPtr self);

[UnmanagedFunctionPointer(CallingConvention.ThisCall)]
internal delegate ulong RequestUserStatsFn(IntPtr self, ulong steamId);

[UnmanagedFunctionPointer(CallingConvention.ThisCall)]
internal delegate ulong RequestGlobalAchievementPercentagesFn(IntPtr self);

[UnmanagedFunctionPointer(CallingConvention.ThisCall)]
[return: MarshalAs(UnmanagedType.I1)]
internal delegate bool GetAchievementAchievedPercentFn(IntPtr self,
    [MarshalAs(UnmanagedType.LPUTF8Str)] string name, out float percent);

[UnmanagedFunctionPointer(CallingConvention.ThisCall)]
[return: MarshalAs(UnmanagedType.I1)]
internal delegate bool ResetAllStatsFn(IntPtr self, [MarshalAs(UnmanagedType.I1)] bool achievementsToo);

// ISteamUserStats013: the current game's achievements and stats
internal sealed class SteamUserStats(IntPtr address)
{
    private readonly NativeInterface _stats = new(address);

    // value is a boxed int, or float when isFloat
    public bool GetStat(string name, bool isFloat, out object value)
    {
        bool found;
        if (isFloat)
        {
            found = _stats.Function<GetStatFloatFn>(0)(_stats.Self, name, out var floatValue);
            value = floatValue;
        }
        else
        {
            found = _stats.Function<GetStatIntFn>(1)(_stats.Self, name, out var intValue);
            value = intValue;
        }

        return found;
    }

    public bool SetStat(string name, object value)
    {
        return value switch
        {
            float f => _stats.Function<SetStatFloatFn>(2)(_stats.Self, name, f),
            int i => _stats.Function<SetStatIntFn>(3)(_stats.Self, name, i),
            _ => throw new ArgumentException("stat value must be int or float", nameof(value))
        };
    }

    public bool GetAchievement(string name, out bool isAchieved, out uint unlockTime)
    {
        return _stats.Function<GetAchievementAndUnlockTimeFn>(8)(_stats.Self, name, out isAchieved, out unlockTime);
    }

    // slot 6 sets the achievement, slot 7 clears it
    public bool SetAchievement(string name, bool achieved)
    {
        return _stats.Function<AchievementFn>(achieved ? 6 : 7)(_stats.Self, name);
    }

    public bool StoreStats()
    {
        return _stats.Function<StoreStatsFn>(9)(_stats.Self);
    }

    // returns 0 on failure; the result arrives as a UserStatsReceived callback
    public ulong RequestUserStats(ulong steamId)
    {
        return _stats.Function<RequestUserStatsFn>(15)(_stats.Self, steamId);
    }

    // returns 0 on failure; the result arrives as a GlobalAchievementPercentagesReady callback
    public ulong RequestGlobalAchievementPercentages()
    {
        return _stats.Function<RequestGlobalAchievementPercentagesFn>(33)(_stats.Self);
    }

    // the share of all players (0-100) who have the achievement
    public bool GetAchievementAchievedPercent(string name, out float percent)
    {
        return _stats.Function<GetAchievementAchievedPercentFn>(36)(_stats.Self, name, out percent);
    }

    public bool ResetAllStats(bool achievementsToo)
    {
        return _stats.Function<ResetAllStatsFn>(20)(_stats.Self, achievementsToo);
    }
}