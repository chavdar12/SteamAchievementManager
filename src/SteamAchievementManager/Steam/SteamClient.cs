using System.Globalization;
using System.Runtime.InteropServices;

namespace SteamAchievementManager.Steam;

internal enum ClientInitializeFailure
{
    GetInstallPath,
    Load,
    CreateSteamClient,
    CreateSteamPipe,
    ConnectToGlobalUser,
    AppIdMismatch
}

[UnmanagedFunctionPointer(CallingConvention.ThisCall)]
internal delegate int CreateSteamPipeFn(IntPtr self);

[UnmanagedFunctionPointer(CallingConvention.ThisCall)]
[return: MarshalAs(UnmanagedType.I1)]
internal delegate bool ReleaseSteamPipeFn(IntPtr self, int pipe);

[UnmanagedFunctionPointer(CallingConvention.ThisCall)]
internal delegate int ConnectToGlobalUserFn(IntPtr self, int pipe);

[UnmanagedFunctionPointer(CallingConvention.ThisCall)]
internal delegate void ReleaseUserFn(IntPtr self, int pipe, int user);

[UnmanagedFunctionPointer(CallingConvention.ThisCall)]
internal delegate IntPtr GetUserInterfaceFn(IntPtr self, int user, int pipe,
    [MarshalAs(UnmanagedType.LPUTF8Str)] string version);

[UnmanagedFunctionPointer(CallingConvention.ThisCall)]
internal delegate IntPtr GetPipeInterfaceFn(IntPtr self, int pipe,
    [MarshalAs(UnmanagedType.LPUTF8Str)] string version);

[UnmanagedFunctionPointer(CallingConvention.ThisCall)]
internal delegate void GetSteamIdFn(IntPtr self, out ulong steamId);

[UnmanagedFunctionPointer(CallingConvention.ThisCall)]
internal delegate uint GetAppIdFn(IntPtr self);

internal sealed class ClientInitializeException(ClientInitializeFailure failure, string message) : Exception(message)
{
    public ClientInitializeFailure Failure { get; } = failure;
}

// a connection to the running Steam client (ISteamClient018) as the logged-in user
internal sealed class SteamClient : IDisposable
{
    private readonly NativeInterface _client;
    private readonly List<(int Id, Action<IntPtr> Handler)> _callbacks = [];
    private int _pipe;
    private int _user;
    private bool _isRunningCallbacks;

    public SteamClient(uint appId)
    {
        if (string.IsNullOrEmpty(SteamLibrary.InstallPath))
            throw new ClientInitializeException(ClientInitializeFailure.GetInstallPath,
                "failed to get Steam install path");

        // binds this process to the game, so its stats can be read and written
        if (appId != 0) Environment.SetEnvironmentVariable("SteamAppId", appId.ToString(CultureInfo.InvariantCulture));

        if (SteamLibrary.Load() == false)
            throw new ClientInitializeException(ClientInitializeFailure.Load, "failed to load SteamClient");

        var address = SteamLibrary.CreateInterface("SteamClient018");
        if (address == IntPtr.Zero)
            throw new ClientInitializeException(ClientInitializeFailure.CreateSteamClient,
                "failed to create ISteamClient018");
        _client = new NativeInterface(address);

        _pipe = _client.Function<CreateSteamPipeFn>(0)(_client.Self);
        if (_pipe == 0)
            throw new ClientInitializeException(ClientInitializeFailure.CreateSteamPipe, "failed to create pipe");

        _user = _client.Function<ConnectToGlobalUserFn>(2)(_client.Self, _pipe);
        if (_user == 0)
            throw Fail(ClientInitializeFailure.ConnectToGlobalUser, "failed to connect to global user");

        var utils = new NativeInterface(GetPipeInterface(9, "SteamUtils005"));
        if (appId != 0 && utils.Function<GetAppIdFn>(9)(utils.Self) != appId)
            throw Fail(ClientInitializeFailure.AppIdMismatch, "appID mismatch");

        var user = new NativeInterface(GetUserInterface(5, "SteamUser012"));
        user.Function<GetSteamIdFn>(2)(user.Self, out var steamId);
        SteamId = steamId;

        UserStats = new SteamUserStats(GetUserInterface(13, "STEAMUSERSTATS_INTERFACE_VERSION013"));
        Apps = new SteamApps(
            GetUserInterface(15, "STEAMAPPS_INTERFACE_VERSION001"),
            GetUserInterface(15, "STEAMAPPS_INTERFACE_VERSION008"));
    }

    public ulong SteamId { get; }
    public SteamApps Apps { get; }
    public SteamUserStats UserStats { get; }

    public void OnCallback<T>(Action<T> handler) where T : struct, ICallback =>
        _callbacks.Add((T.Id, param => handler(Marshal.PtrToStructure<T>(param))));

    // dispatches pending callbacks to the registered handlers; called from a UI timer
    public void RunCallbacks()
    {
        if (_isRunningCallbacks) return;

        _isRunningCallbacks = true;
        try
        {
            while (SteamLibrary.GetCallback(_pipe, out var message))
            {
                foreach (var (id, handler) in _callbacks)
                    if (id == message.Id)
                        handler(message.Param);
                SteamLibrary.FreeLastCallback(_pipe);
            }
        }
        finally
        {
            _isRunningCallbacks = false;
        }
    }

    public void Dispose()
    {
        if (_pipe == 0) return;

        if (_user != 0) _client.Function<ReleaseUserFn>(4)(_client.Self, _pipe, _user);
        _client.Function<ReleaseSteamPipeFn>(1)(_client.Self, _pipe);
        _user = _pipe = 0;
    }

    private IntPtr GetUserInterface(int slot, string version) =>
        _client.Function<GetUserInterfaceFn>(slot)(_client.Self, _user, _pipe, version);

    private IntPtr GetPipeInterface(int slot, string version) =>
        _client.Function<GetPipeInterfaceFn>(slot)(_client.Self, _pipe, version);

    private ClientInitializeException Fail(ClientInitializeFailure failure, string message)
    {
        Dispose();
        return new ClientInitializeException(failure, message);
    }
}
