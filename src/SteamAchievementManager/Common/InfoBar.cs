using System.Drawing.Drawing2D;

namespace SteamAchievementManager.Common;

internal enum Severity
{
    Info,
    Success,
    Warning,
    Error
}

// a button on an info bar; a null Click just dismisses the bar
internal sealed record InfoAction(string Text, Action Click);

// an inline message strip (like the WinUI InfoBar) that replaces message boxes:
// results, errors and confirmations show here instead of in popups
internal sealed class InfoBar : Control
{
    private readonly System.Windows.Forms.Timer _autoHide = new() { Interval = 6000 };
    private readonly ToolTip _toolTip = new();
    private readonly List<ModernButton> _buttons = [];
    private Severity _severity;
    private string _message = "";

    public InfoBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Dock = DockStyle.Top;
        Visible = false;
        _autoHide.Tick += (_, _) => Dismiss();
    }

    // success messages without buttons fade out by themselves; everything else stays until dismissed
    public void Show(Severity severity, string message, IEnumerable<InfoAction> actions = null, string details = null)
    {
        _autoHide.Stop();
        _severity = severity;
        _message = message;

        foreach (var button in _buttons)
        {
            Controls.Remove(button);
            button.Dispose();
        }

        _buttons.Clear();

        var back = Theme.For(severity).Back;
        foreach (var action in actions ?? [])
            AddButton(new ModernButton { Text = action.Text, IsPrimary = _buttons.Count == 0 }, back, action.Click);
        AddButton(new ModernButton { Glyph = Glyphs.Close, IsSubtle = true }, back, null);
        _toolTip.SetToolTip(_buttons[^1], "Dismiss");

        _toolTip.SetToolTip(this, details ?? message);
        Height = LogicalToDeviceUnits(56);
        Visible = true;
        PerformLayout();
        Invalidate();

        if (severity == Severity.Success && _buttons.Count == 1) _autoHide.Start();
    }

    public void Dismiss()
    {
        _autoHide.Stop();
        Visible = false;
    }

    private void AddButton(ModernButton button, Color back, Action click)
    {
        button.BackColor = back;
        button.Click += (_, _) =>
        {
            Dismiss();
            // run after the click finishes, so the action can show a new message (which disposes this button)
            if (click != null) BeginInvoke(click);
        };
        _buttons.Add(button);
        Controls.Add(button);
    }

    private Rectangle BarBounds => new(LogicalToDeviceUnits(12), LogicalToDeviceUnits(6),
        Width - LogicalToDeviceUnits(24), Height - LogicalToDeviceUnits(10));

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);

        var bar = BarBounds;
        var height = LogicalToDeviceUnits(30);
        var right = bar.Right - LogicalToDeviceUnits(9);
        for (var i = _buttons.Count - 1; i >= 0; i--)
        {
            var button = _buttons[i];
            var width = button.Glyph != 0 && string.IsNullOrEmpty(button.Text)
                ? height
                : TextRenderer.MeasureText(button.Text, button.Font).Width + LogicalToDeviceUnits(24);
            button.Bounds = new Rectangle(right - width, bar.Top + (bar.Height - height) / 2, width, height);
            right -= width + LogicalToDeviceUnits(6);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Background);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var (back, iconColor) = Theme.For(_severity);
        var bar = BarBounds;
        RectangleF shape = new(bar.X + 0.5f, bar.Y + 0.5f, bar.Width - 1, bar.Height - 1);
        Theme.FillRounded(g, back, shape, LogicalToDeviceUnits(6));
        Theme.DrawRounded(g, Theme.Blend(back, Theme.Text, 0.08f), shape, LogicalToDeviceUnits(6));

        var glyph = _severity switch
        {
            Severity.Success => Glyphs.Success,
            Severity.Warning => Glyphs.Warning,
            Severity.Error => Glyphs.Error,
            _ => Glyphs.Info
        };
        var iconSize = LogicalToDeviceUnits(16);
        Glyphs.Draw(g, glyph,
            new RectangleF(bar.X + LogicalToDeviceUnits(14), bar.Y + (bar.Height - iconSize) / 2f, iconSize, iconSize),
            iconColor);

        var textLeft = bar.X + LogicalToDeviceUnits(42);
        var textRight = _buttons.Count > 0 ? _buttons.Min(b => b.Left) - LogicalToDeviceUnits(10) : bar.Right;
        TextRenderer.DrawText(g, _message, Font, Rectangle.FromLTRB(textLeft, bar.Top, textRight, bar.Bottom),
            Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis |
                        TextFormatFlags.NoPrefix);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _autoHide.Dispose();
            _toolTip.Dispose();
        }

        base.Dispose(disposing);
    }
}