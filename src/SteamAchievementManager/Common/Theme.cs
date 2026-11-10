using System.Drawing.Drawing2D;

namespace SteamAchievementManager.Common;

// Windows 11 style colors for the current light/dark mode, plus shared fonts and shape helpers
internal static class Theme
{
#pragma warning disable WFO5001 // dark mode is still marked experimental in WinForms
    public static bool IsDark => Application.IsDarkModeEnabled;
#pragma warning restore WFO5001

    public static Color Background => Pick(0xF3F3F3, 0x202020);
    public static Color Surface => Pick(0xFFFFFF, 0x2B2B2B);
    public static Color SurfaceHover => Pick(0xF9F9F9, 0x323232);
    public static Color SurfaceSelected => Pick(0xEAF2FB, 0x2E3A47);
    public static Color Border => Pick(0xE3E3E3, 0x383838);
    public static Color Text => Pick(0x1B1B1B, 0xFFFFFF);
    public static Color SubtleText => Pick(0x616161, 0xC5C5C5);
    public static Color DisabledText => Pick(0xA0A0A0, 0x7A7A7A);
    public static Color Accent => Pick(0x0067C0, 0x4CC2FF);
    public static Color AccentHover => Pick(0x1975C5, 0x47B1E8);
    public static Color AccentPressed => Pick(0x3183CA, 0x42A1D2);
    public static Color OnAccent => Pick(0xFFFFFF, 0x000000);
    public static Color ControlHover => Pick(0xE6E6E6, 0x383838);
    public static Color ControlPressed => Pick(0xDADADA, 0x424242);
    public static Color Placeholder => Pick(0xE9E9E9, 0x3A3A3A);
    public static Color Success => Pick(0x0F7B0F, 0x6CCB5F);
    public static Color Warning => Pick(0x9D5D00, 0xFCE100);
    public static Color Danger => Pick(0xC42B1C, 0xFF99A4);

    // background and icon color of an info bar
    public static (Color Back, Color Icon) For(Severity severity) => severity switch
    {
        Severity.Success => (Pick(0xDFF6DD, 0x393D1B), Success),
        Severity.Warning => (Pick(0xFFF4CE, 0x433519), Warning),
        Severity.Error => (Pick(0xFDE7E9, 0x442726), Danger),
        _ => (Pick(0xE6F2FB, 0x1F2D3A), Accent)
    };

    public static Font TitleFont { get; } = new("Segoe UI Semibold", 15f);
    public static Font RowTitleFont { get; } = new("Segoe UI Semibold", 10f);
    public static Font SmallFont { get; } = new("Segoe UI", 8.25f);
    public static Font SmallBoldFont { get; } = new("Segoe UI Semibold", 8.25f);

    public static Color Blend(Color color, Color over, float amount) => Color.FromArgb(
        (int)(color.R + (over.R - color.R) * amount),
        (int)(color.G + (over.G - color.G) * amount),
        (int)(color.B + (over.B - color.B) * amount));

    public static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        GraphicsPath path = new();
        var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0)
        {
            path.AddRectangle(r);
            return path;
        }

        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void FillRounded(Graphics g, Color color, RectangleF r, float radius)
    {
        using SolidBrush brush = new(color);
        using var path = RoundedRect(r, radius);
        g.FillPath(brush, path);
    }

    public static void DrawRounded(Graphics g, Color color, RectangleF r, float radius, float width = 1)
    {
        using Pen pen = new(color, width);
        using var path = RoundedRect(r, radius);
        g.DrawPath(pen, path);
    }

    // draws an image clipped to a rounded rectangle
    public static void DrawRoundedImage(Graphics g, Image image, Rectangle r, float radius)
    {
        using var path = RoundedRect(r, radius);
        var state = g.Save();
        g.SetClip(path);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(image, r);
        g.Restore(state);
    }

    private static Color Pick(int light, int dark) => Color.FromArgb(255, Color.FromArgb(IsDark ? dark : light));
}
