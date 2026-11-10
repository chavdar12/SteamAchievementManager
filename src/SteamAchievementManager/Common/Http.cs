using System.Security.Cryptography;
using System.Text;

namespace SteamAchievementManager.Common;

internal static class Http
{
    // Steam image URLs name files by content hash, so a cached copy never goes stale
    private static readonly string ImageCache = Path.Combine(AppData.Folder, "images");

    public static HttpClient Client { get; } = new();

    // null if the download or decoding fails; downloads are kept on disk for the next run
    public static async Task<Bitmap> TryGetImageAsync(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;

        var file = Path.Combine(ImageCache, Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(url))));
        try
        {
            if (File.Exists(file)) return new Bitmap(new MemoryStream(await File.ReadAllBytesAsync(file)));
        }
        catch (Exception)
        {
            // a damaged cache file: drop it and download again
            TryDelete(file);
        }

        try
        {
            var data = await Client.GetByteArrayAsync(url);
            Bitmap bitmap = new(new MemoryStream(data));
            try
            {
                Directory.CreateDirectory(ImageCache);
                await File.WriteAllBytesAsync(file, data);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // caching is optional
            }

            return bitmap;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}