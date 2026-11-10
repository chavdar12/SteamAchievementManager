using System.Runtime.InteropServices;

namespace SteamAchievementManager.Steam;

// a callback payload struct, identified by Steam's callback ID
internal interface ICallback
{
    static abstract int Id { get; }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct CallbackMessage
{
    public int User;
    public int Id;
    public IntPtr Param;
    public int ParamSize;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct AppDataChanged : ICallback
{
    public static int Id => 1001;

    public uint AppId;
    [MarshalAs(UnmanagedType.I1)] public bool Result;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct UserStatsReceived : ICallback
{
    public static int Id => 1101;

    public ulong GameId;
    public int Result;
    public ulong SteamIdUser;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct UserStatsStored : ICallback
{
    public static int Id => 1102;

    public ulong GameId;
    public int Result;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct GlobalAchievementPercentagesReady : ICallback
{
    public static int Id => 1110;

    public ulong GameId;
    public int Result;
}