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

    public bool SetStat(string name, object value) => value switch
    {
        float f => _stats.Function<SetStatFloatFn>(2)(_stats.Self, name, f),
        int i => _stats.Function<SetStatIntFn>(3)(_stats.Self, name, i),
        _ => throw new ArgumentException("stat value must be int or float", nameof(value))
    };

    public bool GetAchievement(string name, out bool isAchieved, out uint unlockTime) =>
        _stats.Function<GetAchievementAndUnlockTimeFn>(8)(_stats.Self, name, out isAchieved, out unlockTime);

    // slot 6 sets the achievement, slot 7 clears it
    public bool SetAchievement(string name, bool achieved) =>
        _stats.Function<AchievementFn>(achieved ? 6 : 7)(_stats.Self, name);

    public bool StoreStats() => _stats.Function<StoreStatsFn>(9)(_stats.Self);

    // returns 0 on failure; the result arrives as a UserStatsReceived callback
    public ulong RequestUserStats(ulong steamId) =>
        _stats.Function<RequestUserStatsFn>(15)(_stats.Self, steamId);

    public bool ResetAllStats(bool achievementsToo) =>
        _stats.Function<ResetAllStatsFn>(20)(_stats.Self, achievementsToo);
}
