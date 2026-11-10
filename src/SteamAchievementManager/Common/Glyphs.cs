using System.Drawing.Text;

namespace SteamAchievementManager.Common;

// icons drawn from the Windows icon font (Segoe Fluent Icons on Windows 11, Segoe MDL2 Assets on Windows 10)
internal static class Glyphs
{
    public const char Refresh = '';
    public const char Add = '';
    public const char Search = '';
    public const char Filter = '';
    public const char SelectAll = '';
    public const char ClearSelection = '';
    public const char Unlock = '';
    public const char Lock = '';
    public const char Save = '';
    public const char Reset = '';
    public const char Invert = '';
    public const char Download = '';
    public const char Open = '';
    public const char Info = '';
    public const char Warning = '';
    public const char Error = '';
    public const char Success = '';
    public const char Close = '';
    public const char CheckMark = '';
    public const char Star = '';
    public const char Chart = '';
    public const char Shield = '';
    public const char Game = '';

    private static readonly string Family = new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" }
        .FirstOrDefault(name => FontFamily.Families.Any(f => f.Name == name));

    public static Bitmap Image(char glyph, int size, Color color)
    {
        Bitmap bitmap = new(size, size);
        using var g = Graphics.FromImage(bitmap);
        Draw(g, glyph, new Rectangle(0, 0, size, size), color);
        return bitmap;
    }

    // centers the glyph in bounds; size is the glyph's em size in pixels (default: fill the bounds)
    public static void Draw(Graphics g, char glyph, RectangleF bounds, Color color, float size = 0)
    {
        if (Family == null) return;

        using Font font = new(Family, size > 0 ? size : Math.Min(bounds.Width, bounds.Height), GraphicsUnit.Pixel);
        using SolidBrush brush = new(color);
        using StringFormat format = new(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };

        var hint = g.TextRenderingHint;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.DrawString(glyph.ToString(), font, brush, bounds, format);
        g.TextRenderingHint = hint;
    }
}
