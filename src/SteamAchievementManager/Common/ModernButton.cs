using System.Drawing.Drawing2D;

namespace SteamAchievementManager.Common;

// a flat rounded button: accent filled (primary), outlined (default) or borderless (subtle)
internal sealed class ModernButton : Button
{
    private bool _isHot;
    private bool _isPressed;

    public ModernButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer, true);
        FlatStyle = FlatStyle.Flat;
        Cursor = Cursors.Hand;
    }

    public bool IsPrimary { get; set; }
    public bool IsSubtle { get; set; }

    // shown instead of text when Text is empty
    public char Glyph { get; set; }

    protected override void OnMouseEnter(EventArgs e)
    {
        _isHot = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _isHot = _isPressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _isPressed = e.Button == MouseButtons.Left;
        Invalidate();
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _isPressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        RectangleF shape = new(0.5f, 0.5f, Width - 1, Height - 1);
        var radius = LogicalToDeviceUnits(4);
        Color fore;
        if (IsPrimary)
        {
            Theme.FillRounded(g, _isPressed ? Theme.AccentPressed : _isHot ? Theme.AccentHover : Theme.Accent,
                shape, radius);
            fore = Theme.OnAccent;
        }
        else
        {
            var overlay = _isPressed ? 0.14f : _isHot ? 0.08f : IsSubtle ? 0f : 0.03f;
            if (overlay > 0) Theme.FillRounded(g, Theme.Blend(BackColor, Theme.Text, overlay), shape, radius);
            if (IsSubtle == false) Theme.DrawRounded(g, Theme.Blend(BackColor, Theme.Text, 0.18f), shape, radius);
            fore = Theme.Text;
        }

        if (Enabled == false) fore = Theme.DisabledText;

        if (string.IsNullOrEmpty(Text) && Glyph != 0)
            Glyphs.Draw(g, Glyph, ClientRectangle, fore, LogicalToDeviceUnits(10));
        else
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis);

        if (Focused && ShowFocusCues)
        {
            RectangleF focus = new(1.5f, 1.5f, Width - 3, Height - 3);
            Theme.DrawRounded(g, Theme.Text, focus, radius, 1.5f);
        }
    }
}
