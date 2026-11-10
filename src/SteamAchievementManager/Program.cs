using System.Globalization;
using SteamAchievementManager.Manager;
using SteamAchievementManager.Picker;
using SteamAchievementManager.Steam;

namespace SteamAchievementManager;

internal static class Program
{
    // no args: game picker; <appId>: achievement manager for that game;
    // <appId> --unlock-all / --lock-all: unlocks or locks everything without showing a window
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
#pragma warning disable WFO5001 // dark mode is still marked experimental in WinForms
        Application.SetColorMode(SystemColorMode.System); // follow the Windows light/dark setting
#pragma warning restore WFO5001

        var setAll = args.Length > 1 && args[1] is "--unlock-all" or "--lock-all";
        uint appId = 0;
        if (args.Length > 0 &&
            uint.TryParse(args[0], NumberStyles.None, CultureInfo.InvariantCulture, out appId) == false)
            return Fail("Could not parse application ID from command line argument.", setAll);

        if (SteamLibrary.InstallPath == Application.StartupPath)
            return Fail("This tool declines to being run from the Steam directory.", setAll);

        SteamClient steam;
        try
        {
            steam = new SteamClient(appId);
        }
        catch (ClientInitializeException e)
        {
            if (setAll)
                return Fail(e.Failure == ClientInitializeFailure.ConnectToGlobalUser
                    ? "Steam couldn't open the game (is Family Sharing using it?)"
                    : $"Steam isn't available ({e.Message})", true);

            var message = "Steam is not running. Please start Steam then run this tool again.";
            if (appId != 0 && e.Failure == ClientInitializeFailure.ConnectToGlobalUser)
                message += "\n\nIf you have the game through Family Share, the game may be locked due to\n" +
                           "the Family Share account actively playing a game.";
            return Fail(message + "\n\n(" + e.Message + ")", false);
        }

        using (steam)
        using (var callbackTimer = new System.Windows.Forms.Timer { Interval = 100, Enabled = true })
        {
            callbackTimer.Tick += (_, _) => steam.RunCallbacks();

            if (setAll) return UnlockAllRunner.Run(appId, steam, args[1] == "--unlock-all");

            Application.Run(appId == 0 ? new GamePicker(steam) : new AchievementManager(appId, steam));
            return 0;
        }
    }

    // nothing is on screen yet, so this is the one place a message box is used;
    // the silent unlock/lock-all mode reports to its parent through stderr instead
    private static int Fail(string message, bool silent)
    {
        if (silent)
            Console.Error.WriteLine(message);
        else
            MessageBox.Show(message, "Steam Achievement Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);
        return 1;
    }
}
