using System.Runtime.InteropServices;
using System.Text;

namespace SteamAchievementManager.Steam;

[UnmanagedFunctionPointer(CallingConvention.ThisCall)]
internal delegate int GetAppDataFn(IntPtr self, uint appId, [MarshalAs(UnmanagedType.LPUTF8Str)] string key,
    byte[] value, int valueLength);

[UnmanagedFunctionPointer(CallingConvention.ThisCall)]
internal delegate IntPtr GetCurrentGameLanguageFn(IntPtr self);

[UnmanagedFunctionPointer(CallingConvention.ThisCall)]
[return: MarshalAs(UnmanagedType.I1)]
internal delegate bool IsSubscribedAppFn(IntPtr self, uint appId);

// ISteamApps001 (app metadata) and ISteamApps008 (ownership, language)
internal sealed class SteamApps(IntPtr apps001, IntPtr apps008)
{
    private readonly NativeInterface _apps001 = new(apps001);
    private readonly NativeInterface _apps008 = new(apps008);

    // e.g. key "name"; null if the app has no such value
    public string GetAppData(uint appId, string key)
    {
        var buffer = new byte[1024];
        if (_apps001.Function<GetAppDataFn>(0)(_apps001.Self, appId, key, buffer, buffer.Length) == 0) return null;

        var length = Array.IndexOf(buffer, (byte)0);
        return Encoding.UTF8.GetString(buffer, 0, length < 0 ? buffer.Length : length);
    }

    public string GetCurrentGameLanguage() =>
        Marshal.PtrToStringUTF8(_apps008.Function<GetCurrentGameLanguageFn>(4)(_apps008.Self));

    public bool IsSubscribedApp(uint appId) => _apps008.Function<IsSubscribedAppFn>(6)(_apps008.Self, appId);
}
