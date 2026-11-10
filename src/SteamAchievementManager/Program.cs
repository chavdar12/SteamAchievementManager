using System.Globalization;
using SteamAchievementManager.Common;
using SteamAchievementManager.Manager;
using SteamAchievementManager.Picker;
using SteamAchievementManager.Steam;

namespace SteamAchievementManager;

internal static class Program
{
    // no args: game picker; <appId> [--unlock-all]: achievement manager for that game
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
#pragma warning disable WFO5001 // dark mode is still marked experimental in WinForms
        Application.SetColorMode(SystemColorMode.System); // follow the Windows light/dark setting
#pragma warning restore WFO5001

        uint appId = 0;
        if (args.Length > 0 &&
            uint.TryParse(args[0], NumberStyles.None, CultureInfo.InvariantCulture, out appId) == false)
            return Fail("Could not parse application ID from command line argument.");
        var unlockAll = args.Length > 1 && args[1] == "--unlock-all";

        if (SteamLibrary.InstallPath == Application.StartupPath)
            return Fail("This tool declines to being run from the Steam directory.");

        SteamClient steam;
        try
        {
            steam = new SteamClient(appId);
        }
        catch (ClientInitializeException e)
        {
            var message = "Steam is not running. Please start Steam then run this tool again.";
            if (appId != 0 && e.Failure == ClientInitializeFailure.ConnectToGlobalUser)
                message += "\n\nIf you have the game through Family Share, the game may be locked due to\n" +
                           "the Family Share account actively playing a game.";
            return Fail(message + "\n\n(" + e.Message + ")");
        }

        using (steam)
        using (var callbackTimer = new System.Windows.Forms.Timer { Interval = 100, Enabled = true })
        {
            callbackTimer.Tick += (_, _) => steam.RunCallbacks();

            if (appId == 0)
            {
                Application.Run(new GamePicker(steam));
                return 0;
            }

            AchievementManager manager = new(appId, steam, unlockAll);
            Application.Run(manager);
            return unlockAll && manager.UnlockAllSucceeded == false ? 1 : 0;
        }
    }

    private static int Fail(string message)
    {
        Dialogs.Error(null, message);
        return 1;
    }
}
