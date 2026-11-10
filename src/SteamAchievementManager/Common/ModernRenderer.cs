using System.Drawing.Drawing2D;

namespace SteamAchievementManager.Common;

// flat toolbar look: rounded hover/pressed backgrounds, an accent fill for primary actions
// (items tagged "primary") and an accent underline for checked toggle buttons
internal sealed class ModernRenderer() : ToolStripProfessionalRenderer(new ModernColors())
{
    public static bool IsPrimary(ToolStripItem item) => item.Tag is "primary";

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        if (e.ToolStrip is ToolStripDropDown)
        {
            base.OnRenderToolStripBackground(e);
            return;
        }

        using SolidBrush brush = new(Theme.Background);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        if (e.ToolStrip is ToolStripDropDown)
        {
            base.OnRenderToolStripBorder(e);
            return;
        }

        // a hairline under the toolbar, above the status bar
        using Pen pen = new(Theme.Border);
        var bounds = e.ToolStrip.ClientRectangle;
        var y = e.ToolStrip is StatusStrip ? 0 : bounds.Bottom - 1;
        e.Graphics.DrawLine(pen, 0, y, bounds.Right, y);
    }

    protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e) =>
        DrawItemBackground(e.Graphics, e.Item, e.Item is ToolStripButton { Checked: true });

    protected override void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e) =>
        DrawItemBackground(e.Graphics, e.Item, false);

    private static void DrawItemBackground(Graphics g, ToolStripItem item, bool isChecked)
    {
        var radius = item.Height / 6f;
        RectangleF bounds = new(0.5f, 0.5f, item.Width - 1, item.Height - 1);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (IsPrimary(item))
        {
            var fill = item.Enabled == false ? Theme.ControlPressed
                : item.Pressed ? Theme.AccentPressed
                : item.Selected ? Theme.AccentHover
                : Theme.Accent;
            Theme.FillRounded(g, fill, bounds, radius);
            return;
        }

        if (item.Enabled && item.Pressed)
            Theme.FillRounded(g, Theme.ControlPressed, bounds, radius);
        else if (item.Enabled && item.Selected)
            Theme.FillRounded(g, Theme.ControlHover, bounds, radius);
        else if (isChecked)
            Theme.FillRounded(g, Theme.Surface, bounds, radius);

        if (isChecked)
        {
            // a short accent bar under the text, like a selected tab
            var width = item.Width / 3f;
            var height = Math.Max(2f, item.Height / 12f);
            Theme.FillRounded(g, Theme.Accent,
                new RectangleF((item.Width - width) / 2, item.Height - height - 1, width, height), height / 2);
        }
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled == false ? Theme.DisabledText
            : IsPrimary(e.Item) ? Theme.OnAccent
            : e.Item is ToolStripStatusLabel ? Theme.SubtleText
            : Theme.Text;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = e.Item?.Enabled == false ? Theme.DisabledText : Theme.SubtleText;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        if (e.Vertical == false)
        {
            base.OnRenderSeparator(e);
            return;
        }

        var bounds = e.Item.ContentRectangle;
        var x = bounds.Width / 2;
        using Pen pen = new(Theme.Border);
        e.Graphics.DrawLine(pen, x, bounds.Height / 4, x, bounds.Height * 3 / 4);
    }

    private sealed class ModernColors : ProfessionalColorTable
    {
        public ModernColors() => UseSystemColors = false;

        public override Color ToolStripDropDownBackground => Theme.Surface;
        public override Color ImageMarginGradientBegin => Theme.Surface;
        public override Color ImageMarginGradientMiddle => Theme.Surface;
        public override Color ImageMarginGradientEnd => Theme.Surface;
        public override Color MenuBorder => Theme.Border;
        public override Color MenuItemBorder => Theme.ControlHover;
        public override Color MenuItemSelected => Theme.ControlHover;
        public override Color MenuItemSelectedGradientBegin => Theme.ControlHover;
        public override Color MenuItemSelectedGradientEnd => Theme.ControlHover;
        public override Color MenuItemPressedGradientBegin => Theme.ControlPressed;
        public override Color MenuItemPressedGradientEnd => Theme.ControlPressed;
        public override Color CheckBackground => Theme.SurfaceSelected;
        public override Color CheckSelectedBackground => Theme.SurfaceSelected;
        public override Color CheckPressedBackground => Theme.SurfaceSelected;
        public override Color ButtonSelectedBorder => Theme.SurfaceSelected;
        public override Color SeparatorDark => Theme.Border;
        public override Color SeparatorLight => Theme.Border;
        public override Color StatusStripGradientBegin => Theme.Background;
        public override Color StatusStripGradientEnd => Theme.Background;
    }
}
