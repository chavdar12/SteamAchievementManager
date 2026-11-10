using System.Text.Json;

namespace SteamAchievementManager.Common;

internal static class UpdateCheck
{
    private const string LatestRelease =
        "https://api.github.com/repos/chavdar12/SteamAchievementManager/releases/latest";

    // the newer release's version and page, or null when up to date or the check fails
    public static async Task<(Version Version, string Url)?> FindNewerAsync()
    {
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, LatestRelease);
            request.Headers.UserAgent.ParseAdd("SteamAchievementManager"); // GitHub rejects requests without one
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await Http.Client.SendAsync(request);
            if (response.IsSuccessStatusCode == false) return null;

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var tag = json.RootElement.GetProperty("tag_name").GetString()?.TrimStart('v');
            var url = json.RootElement.GetProperty("html_url").GetString();
            if (Version.TryParse(tag, out var latest) == false || url == null) return null;

            var current = typeof(UpdateCheck).Assembly.GetName().Version;
            return current != null && Normalize(latest) > Normalize(current) ? (latest, url) : null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or
                                      KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    // "7.1" and "7.1.0.0" are the same version
    private static Version Normalize(Version v)
    {
        return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
    }
}