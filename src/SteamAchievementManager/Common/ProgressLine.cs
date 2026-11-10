using System.Drawing.Drawing2D;

namespace SteamAchievementManager.Common;

// a thin rounded progress bar
internal sealed class ProgressLine : Control
{
    private float _value;

    public ProgressLine()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    // 0..1
    public float Value
    {
        get => _value;
        set
        {
            _value = Math.Clamp(value, 0f, 1f);
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        RectangleF track = new(0, 0, Width, Height);
        Theme.FillRounded(g, Theme.Border, track, Height / 2f);
        if (_value > 0)
            Theme.FillRounded(g, Theme.Accent, track with { Width = Math.Max(Height, Width * _value) }, Height / 2f);
    }
}