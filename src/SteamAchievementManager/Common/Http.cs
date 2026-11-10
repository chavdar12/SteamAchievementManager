namespace SteamAchievementManager.Common;

internal static class Http
{
    public static HttpClient Client { get; } = new();

    // null if the download or decoding fails
    public static async Task<Bitmap> TryGetImageAsync(string url)
    {
        try
        {
            return new Bitmap(new MemoryStream(await Client.GetByteArrayAsync(url)));
        }
        catch (Exception)
        {
            return null;
        }
    }
}
