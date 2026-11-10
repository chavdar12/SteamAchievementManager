using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SteamAchievementManager.Steam;

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate IntPtr CreateInterfaceFn([MarshalAs(UnmanagedType.LPStr)] string version, IntPtr returnCode);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
[return: MarshalAs(UnmanagedType.I1)]
internal delegate bool GetCallbackFn(int pipe, out CallbackMessage message, out int call);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
[return: MarshalAs(UnmanagedType.I1)]
internal delegate bool FreeLastCallbackFn(int pipe);

// steamclient.dll from the local Steam install, and its flat exports
internal static class SteamLibrary
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetDllDirectory(string path);

    private static CreateInterfaceFn _createInterface;
    private static GetCallbackFn _getCallback;
    private static FreeLastCallbackFn _freeLastCallback;

    public static string InstallPath =>
        (string)Registry.GetValue(@"HKEY_LOCAL_MACHINE\Software\Valve\Steam", "InstallPath", null);

    public static bool Load()
    {
        if (_createInterface != null) return true;

        var path = InstallPath;
        if (path == null) return false;

        // steamclient.dll loads its dependencies from the Steam directory and its bin folder
        SetDllDirectory(path + ";" + Path.Combine(path, "bin"));
        if (NativeLibrary.TryLoad(Path.Combine(path, "steamclient.dll"), out var module) == false) return false;

        var getCallback = GetExport<GetCallbackFn>(module, "Steam_BGetCallback");
        var freeLastCallback = GetExport<FreeLastCallbackFn>(module, "Steam_FreeLastCallback");
        var createInterface = GetExport<CreateInterfaceFn>(module, "CreateInterface");
        if (getCallback == null || freeLastCallback == null || createInterface == null) return false;

        (_getCallback, _freeLastCallback, _createInterface) = (getCallback, freeLastCallback, createInterface);
        return true;
    }

    public static IntPtr CreateInterface(string version) => _createInterface(version, IntPtr.Zero);

    public static bool GetCallback(int pipe, out CallbackMessage message) => _getCallback(pipe, out message, out _);

    public static void FreeLastCallback(int pipe) => _freeLastCallback(pipe);

    private static T GetExport<T>(IntPtr module, string name) where T : Delegate =>
        NativeLibrary.TryGetExport(module, name, out var address)
            ? Marshal.GetDelegateForFunctionPointer<T>(address)
            : null;
}
