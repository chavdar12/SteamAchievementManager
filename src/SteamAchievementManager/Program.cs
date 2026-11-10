/* Copyright (c) 2024 Rick (rick 'at' gibbed 'dot' us)
 *
 * This software is provided 'as-is', without any express or implied
 * warranty. In no event will the authors be held liable for any damages
 * arising from the use of this software.
 *
 * Permission is granted to anyone to use this software for any purpose,
 * including commercial applications, and to alter it and redistribute it
 * freely, subject to the following restrictions:
 *
 * 1. The origin of this software must not be misrepresented; you must not
 *    claim that you wrote the original software. If you use this software
 *    in a product, an acknowledgment in the product documentation would
 *    be appreciated but is not required.
 *
 * 2. Altered source versions must be plainly marked as such, and must not
 *    be misrepresented as being the original software.
 *
 * 3. This notice may not be removed or altered from any source
 *    distribution.
 */

using System;
using System.Globalization;
using System.Windows.Forms;

namespace SAM;

internal static class Program
{
    // no args: game picker; <appId> [--unlock-all]: achievement manager for that game
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        long appId = 0;
        var unlockAll = args.Length > 1 && args[1] == "--unlock-all";
        if (args.Length > 0 &&
            long.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out appId) == false)
        {
            ShowError("Could not parse application ID from command line argument.");
            return 1;
        }

        if (API.Steam.GetInstallPath() == Application.StartupPath)
        {
            ShowError("This tool declines to being run from the Steam directory.");
            return 1;
        }

        using API.Client client = new();
        if (InitializeClient(client, appId) == false) return 1;

        if (appId == 0)
        {
            Application.Run(new Picker.GamePicker(client));
            return 0;
        }

        Game.Manager manager = new(appId, client, unlockAll);
        Application.Run(manager);
        return unlockAll == true && manager.UnlockAllSucceeded == false ? 1 : 0;
    }

    private static bool InitializeClient(API.Client client, long appId)
    {
        try
        {
            client.Initialize(appId);
            return true;
        }
        catch (API.ClientInitializeException e)
        {
            var message = "Steam is not running. Please start Steam then run this tool again.";
            if (appId != 0 && e.Failure == API.ClientInitializeFailure.ConnectToGlobalUser)
                message += "\n\nIf you have the game through Family Share, the game may be locked due to\n" +
                           "the Family Share account actively playing a game.";

            if (string.IsNullOrEmpty(e.Message) == false) message += "\n\n(" + e.Message + ")";

            ShowError(message);
            return false;
        }
        catch (DllNotFoundException)
        {
            ShowError("You've caused an exceptional error!");
            return false;
        }
    }

    private static void ShowError(string message)
    {
        MessageBox.Show(message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}