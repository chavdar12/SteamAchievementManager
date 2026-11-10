using SteamAchievementManager.Schema;
using SteamAchievementManager.Steam;

namespace SteamAchievementManager.Manager;

// "<appId> --unlock-all" / "--lock-all": unlocks or locks every unprotected achievement without creating
// any window. Prints the number of changed achievements; on failure prints the reason to stderr and returns 1.
internal static class UnlockAllRunner
{
    public static int Run(uint appId, SteamClient steam, bool unlock)
    {
        ApplicationContext context = new();
        string error = null;
        var unlocked = 0;
        var isDone = false;
        var isReceived = false;

        void Finish(string failure)
        {
            if (isDone) return;
            isDone = true;
            error = failure;
            context.ExitThread();
        }

        steam.OnCallback<UserStatsReceived>(param =>
        {
            // Steam may resend stats after the store; acting on them again would store twice
            if (isReceived) return;
            isReceived = true;

            if (param.Result != 1)
            {
                Finish(param.Result == 2
                    ? "Steam returned no stats (the game may not be owned)"
                    : $"Steam error {param.Result} while reading stats");
                return;
            }

            var schema = GameSchema.Load(appId, steam.Apps.GetCurrentGameLanguage());
            if (schema == null)
            {
                Finish("no achievement data found");
                return;
            }

            // protected achievements can only be set by the game's servers, so they are skipped
            foreach (var def in schema.Achievements)
            {
                if (string.IsNullOrEmpty(def.Id) || def.IsProtected ||
                    steam.UserStats.GetAchievement(def.Id, out var isAchieved, out _) == false || isAchieved == unlock)
                    continue;

                if (steam.UserStats.SetAchievement(def.Id, unlock) == false)
                {
                    Finish($"Steam refused to change {def.Id}");
                    return;
                }

                unlocked++;
            }

            if (unlocked == 0)
                Finish(null);
            else if (!steam.UserStats.StoreStats())
                Finish("Steam refused to save the changes");
            // otherwise finish in UserStatsStored, so exiting does not cut off the store
        });

        steam.OnCallback<UserStatsStored>(param =>
            Finish(param.Result == 1 ? null : $"Steam error {param.Result} while saving"));

        if (steam.UserStats.RequestUserStats(steam.SteamId) == 0)
        {
            Console.Error.WriteLine("couldn't request stats from Steam");
            return 1;
        }

        // the callback timer set up by Program delivers the results inside this message loop
        Application.Run(context);

        if (error != null)
        {
            Console.Error.WriteLine(error);
            return 1;
        }

        Console.WriteLine(unlocked);
        return 0;
    }
}