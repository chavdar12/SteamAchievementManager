using System.Globalization;
using System.Text;

namespace SteamAchievementManager.Steam;

// reads when each game was last played from Steam's userdata/<account>/config/localconfig.vdf
internal static class LocalConfig
{
    // empty if the file is missing or unreadable
    public static Dictionary<uint, long> LoadLastPlayed(ulong steamId)
    {
        var path = Path.Combine(SteamLibrary.InstallPath ?? "", "userdata",
            (steamId & 0xFFFFFFFF).ToString(CultureInfo.InvariantCulture), "config", "localconfig.vdf");
        try
        {
            return ReadLastPlayed(File.ReadAllText(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    // app ID -> Unix time of "LastPlayed" under any "apps" section of the text KeyValues
    public static Dictionary<uint, long> ReadLastPlayed(string text)
    {
        Dictionary<uint, long> result = [];
        List<string> path = [];
        string key = null;
        foreach (var (token, isQuoted) in Tokens(text))
            if (isQuoted == false && token == "{")
            {
                path.Add(key ?? "");
                key = null;
            }
            else if (isQuoted == false && token == "}")
            {
                if (path.Count > 0) path.RemoveAt(path.Count - 1);
                key = null;
            }
            else if (key == null)
            {
                key = token;
            }
            else
            {
                if (key.Equals("LastPlayed", StringComparison.OrdinalIgnoreCase) && path.Count >= 2 &&
                    path[^2].Equals("apps", StringComparison.OrdinalIgnoreCase) &&
                    uint.TryParse(path[^1], NumberStyles.None, CultureInfo.InvariantCulture, out var appId) &&
                    long.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var time) &&
                    time <= DateTimeOffset.MaxValue.ToUnixTimeSeconds())
                    result[appId] = time;
                key = null;
            }

        return result;
    }

    private static IEnumerable<(string Token, bool IsQuoted)> Tokens(string text)
    {
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n') i++;
            }
            else if (c is '{' or '}')
            {
                i++;
                yield return (c.ToString(), false);
            }
            else if (c == '"')
            {
                StringBuilder value = new();
                for (i++; i < text.Length && text[i] != '"'; i++)
                {
                    if (text[i] == '\\' && i + 1 < text.Length) i++;
                    value.Append(text[i]);
                }

                i++; // closing quote
                yield return (value.ToString(), true);
            }
            else
            {
                var start = i;
                while (i < text.Length && char.IsWhiteSpace(text[i]) == false &&
                       text[i] is not ('{' or '}' or '"')) i++;
                yield return (text[start..i], true);
            }
        }
    }
}