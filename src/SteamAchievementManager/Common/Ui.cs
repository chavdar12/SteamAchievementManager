using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32;

namespace SteamAchievementManager.Common;

// factories for the shared toolbar, status bar and header pieces
internal static class Ui
{
    // the colors are picked at startup, so a light/dark switch while running offers a restart
    public static void WatchThemeChanges(Form form, InfoBar infoBar)
    {
        var startedDark = Theme.IsDark;
        UserPreferenceChangedEventHandler handler = (_, e) =>
        {
            if (e.Category != UserPreferenceCategory.General || IsSystemDark() == startedDark ||
                form.IsHandleCreated == false || form.IsDisposed)
                return;

            // raised on a system thread
            form.BeginInvoke(() => infoBar.Show(Severity.Info,
                $"Windows switched to {(startedDark ? "light" : "dark")} mode. Restart to update the colors.",
                [new InfoAction("Restart", () => Restart(form)), new InfoAction("Later", null)]));
        };
        SystemEvents.UserPreferenceChanged += handler;
        form.Disposed += (_, _) => SystemEvents.UserPreferenceChanged -= handler;
    }

    // starts a fresh copy only once the window really closed; the unsaved-changes warning can cancel it
    private static void Restart(Form form)
    {
        form.Close();
        if (form.IsDisposed == false) return;

        try
        {
            ProcessStartInfo start = new(Environment.ProcessPath!);
            foreach (var arg in Environment.GetCommandLineArgs().Skip(1)) start.ArgumentList.Add(arg);
            Process.Start(start)?.Dispose();
        }
        catch (Win32Exception)
        {
            // the window is already gone; the user can start the app again by hand
        }
    }

    private static bool IsSystemDark()
    {
        return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "AppsUseLightTheme", 1) is 0;
    }

    public static ToolStrip Toolbar(Control owner)
    {
        return new ToolStrip
        {
            Renderer = new ModernRenderer(),
            GripStyle = ToolStripGripStyle.Hidden,
            Padding = new Padding(owner.LogicalToDeviceUnits(10), owner.LogicalToDeviceUnits(8),
                owner.LogicalToDeviceUnits(10), owner.LogicalToDeviceUnits(8)),
            ImageScalingSize = new Size(owner.LogicalToDeviceUnits(16), owner.LogicalToDeviceUnits(16)),
            BackColor = Theme.Background,
            ForeColor = Theme.Text
        };
    }

    public static ToolStripButton Button(Control owner, string text, char glyph, EventHandler click,
        string toolTip = null, bool primary = false, bool iconOnly = false)
    {
        ToolStripButton button = new(text)
        {
            Image = Glyphs.Image(glyph, owner.LogicalToDeviceUnits(16), primary ? Theme.OnAccent : Theme.Text),
            DisplayStyle = iconOnly ? ToolStripItemDisplayStyle.Image : ToolStripItemDisplayStyle.ImageAndText,
            TextImageRelation = TextImageRelation.ImageBeforeText,
            Padding = new Padding(owner.LogicalToDeviceUnits(iconOnly ? 6 : 8), owner.LogicalToDeviceUnits(5),
                owner.LogicalToDeviceUnits(iconOnly ? 6 : 10), owner.LogicalToDeviceUnits(5)),
            Margin = new Padding(owner.LogicalToDeviceUnits(2), 0, owner.LogicalToDeviceUnits(2), 0),
            AutoToolTip = false,
            ToolTipText = toolTip ?? text,
            Tag = primary ? "primary" : null
        };
        if (click != null) button.Click += click;
        return button;
    }

    public static ToolStripDropDownButton DropDown(Control owner, string text, char glyph, string toolTip)
    {
        ToolStripDropDownButton button = new(text)
        {
            Image = Glyphs.Image(glyph, owner.LogicalToDeviceUnits(16), Theme.Text),
            DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
            Padding = new Padding(owner.LogicalToDeviceUnits(8), owner.LogicalToDeviceUnits(5),
                owner.LogicalToDeviceUnits(4), owner.LogicalToDeviceUnits(5)),
            Margin = new Padding(owner.LogicalToDeviceUnits(2), 0, owner.LogicalToDeviceUnits(2), 0),
            AutoToolTip = false,
            ToolTipText = toolTip
        };
        // keep the menu open while toggling several options
        button.DropDown.Closing += (_, e) =>
        {
            if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked) e.Cancel = true;
        };
        return button;
    }

    public static ToolStripTextBox SearchBox(Control owner, string placeholder, int width)
    {
        ToolStripTextBox box = new()
        {
            BorderStyle = BorderStyle.FixedSingle,
            Size = new Size(owner.LogicalToDeviceUnits(width), owner.LogicalToDeviceUnits(26)),
            Margin = new Padding(owner.LogicalToDeviceUnits(2), 0, owner.LogicalToDeviceUnits(6), 0)
        };
        box.TextBox.PlaceholderText = placeholder;
        return box;
    }

    public static ToolStripLabel Icon(Control owner, char glyph)
    {
        return new ToolStripLabel
        {
            Image = Glyphs.Image(glyph, owner.LogicalToDeviceUnits(16), Theme.SubtleText),
            DisplayStyle = ToolStripItemDisplayStyle.Image,
            Margin = new Padding(owner.LogicalToDeviceUnits(4), 0, 0, 0)
        };
    }

    public static ToolStripSeparator Separator(Control owner)
    {
        return new ToolStripSeparator
        {
            Margin = new Padding(owner.LogicalToDeviceUnits(4), 0, owner.LogicalToDeviceUnits(4), 0)
        };
    }

    public static StatusStrip StatusBar(Control owner, params ToolStripItem[] items)
    {
        StatusStrip status = new()
        {
            Renderer = new ModernRenderer(),
            SizingGrip = false,
            BackColor = Theme.Background,
            Padding = new Padding(owner.LogicalToDeviceUnits(12), owner.LogicalToDeviceUnits(2),
                owner.LogicalToDeviceUnits(12), owner.LogicalToDeviceUnits(2)),
            ImageScalingSize = new Size(owner.LogicalToDeviceUnits(14), owner.LogicalToDeviceUnits(14))
        };
        status.Items.AddRange(items);
        return status;
    }

    public static ToolStripStatusLabel StatusLabel(bool spring)
    {
        return new ToolStripStatusLabel
        {
            Spring = spring,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Theme.SubtleText
        };
    }

    // a title with a subtitle under it and an optional progress line, docked at the top
    public sealed class Header : Panel
    {
        public Header(Control owner)
        {
            Dock = DockStyle.Top;
            BackColor = Theme.Background;

            var left = owner.LogicalToDeviceUnits(22);
            Title = new Label
            {
                AutoSize = false, AutoEllipsis = true, Font = Theme.TitleFont, ForeColor = Theme.Text,
                Location = new Point(left, owner.LogicalToDeviceUnits(12)),
                Height = owner.LogicalToDeviceUnits(32), UseMnemonic = false
            };
            Subtitle = new Label
            {
                AutoSize = false, AutoEllipsis = true, ForeColor = Theme.SubtleText,
                Location = new Point(left, owner.LogicalToDeviceUnits(44)),
                Height = owner.LogicalToDeviceUnits(20), UseMnemonic = false
            };
            Progress = new ProgressLine
            {
                BackColor = Theme.Background, Visible = false,
                Location = new Point(left, owner.LogicalToDeviceUnits(68)),
                Size = new Size(owner.LogicalToDeviceUnits(280), owner.LogicalToDeviceUnits(4))
            };
            Controls.AddRange([Title, Subtitle, Progress]);
            Height = owner.LogicalToDeviceUnits(78); // after the labels exist: resizing lays them out
        }

        public Label Title { get; }
        public Label Subtitle { get; }
        public ProgressLine Progress { get; }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Title.Width = Subtitle.Width = Math.Max(0, Width - Title.Left * 2);
        }
    }
}