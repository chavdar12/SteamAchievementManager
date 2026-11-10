using System.ComponentModel;
using System.Globalization;
using SteamAchievementManager.Common;
using SteamAchievementManager.Schema;
using SteamAchievementManager.Steam;

namespace SteamAchievementManager.Manager;

// views and edits one game's achievements and stats
internal sealed class AchievementManager : Form
{
    private enum StateFilter
    {
        All,
        Locked,
        Unlocked
    }

    private readonly uint _gameId;
    private readonly SteamClient _steam;
    private GameSchema _schema;
    private bool _isLoaded;

    // Steam can send stats without being asked (e.g. after a store); those must not wipe unsaved ticks
    private bool _isAwaitingStats;

    private readonly List<AchievementInfo> _achievements = [];
    private readonly List<AchievementInfo> _shown = [];
    private readonly BindingList<StatInfo> _statistics = [];
    private StateFilter _show = StateFilter.All;

    private readonly Dictionary<string, Image> _icons = [];
    private readonly Queue<string> _iconQueue = new();
    private readonly HashSet<string> _iconsQueued = [];
    private readonly HashSet<string> _iconsAttempted = [];
    private bool _isDownloadingIcons;

    private readonly ToolStripButton _achievementsTab, _statisticsTab;
    private readonly ToolStripTextBox _searchBox;
    private readonly ToolStripDropDownButton _showButton;
    private readonly ToolStripMenuItem _showAll, _showLocked, _showUnlocked;
    private readonly ToolStripItem[] _achievementTools;
    private readonly ToolStripButton _saveButton, _refreshButton, _resetButton;
    private readonly Ui.Header _header;
    private readonly InfoBar _infoBar = new();
    private readonly ModernList _list = new();
    private readonly Panel _statsPanel;
    private readonly DataGridView _statsGrid;
    private readonly ToolStripStatusLabel _statusLabel = Ui.StatusLabel(true);
    private readonly ToolStripStatusLabel _downloadLabel = Ui.StatusLabel(false);

    public AchievementManager(uint gameId, SteamClient steam)
    {
        _gameId = gameId;
        _steam = steam;

        var gameName = steam.Apps.GetAppData(gameId, "name") ?? gameId.ToString(CultureInfo.InvariantCulture);
        Text = $"{gameName} — Steam Achievement Manager";
        Icon = Assets.App;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(LogicalToDeviceUnits(980), LogicalToDeviceUnits(700));
        MinimumSize = new Size(LogicalToDeviceUnits(760), LogicalToDeviceUnits(460));

        var toolbar = Ui.Toolbar(this);
        _achievementsTab = Ui.Button(this, "Achievements", Glyphs.Star, (_, _) => ShowPage(false));
        _statisticsTab = Ui.Button(this, "Statistics", Glyphs.Chart, (_, _) => ShowPage(true));

        _searchBox = Ui.SearchBox(this, "Search achievements", 180);
        _searchBox.TextChanged += (_, _) => ShowAchievements();

        _showButton = Ui.DropDown(this, "All", Glyphs.Filter, "Filter by state");
        _showAll = ShowItem("All achievements", StateFilter.All);
        _showLocked = ShowItem("Locked only", StateFilter.Locked);
        _showUnlocked = ShowItem("Unlocked only", StateFilter.Unlocked);
        _showButton.DropDownItems.AddRange([_showAll, _showLocked, _showUnlocked]);
        _showAll.Checked = true;

        _saveButton = Ui.Button(this, "Save changes", Glyphs.Save, OnSave,
            "Store the ticked achievements and edited statistics on Steam (Ctrl+S)", true);
        _resetButton = Ui.Button(this, "Reset", Glyphs.Reset, OnReset, "Reset this game's statistics");
        _refreshButton = Ui.Button(this, "Refresh", Glyphs.Refresh, (_, _) => RefreshStats(),
            "Reload from Steam, dropping unsaved changes (F5)", iconOnly: true);
        foreach (var item in new ToolStripItem[] { _saveButton, _resetButton, _refreshButton })
            item.Alignment = ToolStripItemAlignment.Right;

        _achievementTools =
        [
            Ui.Separator(this),
            Ui.Icon(this, Glyphs.Search), _searchBox, _showButton,
            Ui.Separator(this),
            Ui.Button(this, "Unlock all", Glyphs.Unlock, (_, _) => SetAll(_ => true), "Tick every listed achievement"),
            Ui.Button(this, "Lock all", Glyphs.Lock, (_, _) => SetAll(_ => false), "Untick every listed achievement"),
            Ui.Button(this, "Invert", Glyphs.Invert, (_, _) => SetAll(a => !a.Checked), "Flip every listed achievement",
                iconOnly: true)
        ];

        toolbar.Items.AddRange([_achievementsTab, _statisticsTab]);
        toolbar.Items.AddRange(_achievementTools);
        toolbar.Items.AddRange([_saveButton, _resetButton, _refreshButton]);

        _header = new Ui.Header(this) { Title = { Text = gameName } };
        _header.Progress.Visible = true;

        _list.Dock = DockStyle.Fill;
        _list.RowHeight = LogicalToDeviceUnits(70);
        _list.EmptyText = "Loading achievements…";
        _list.IsChecked = i => i < _shown.Count && _shown[i].Checked;
        _list.CanCheck = i => i < _shown.Count && _shown[i].Definition.IsProtected == false;
        _list.ToggleRequested += OnToggleAchievements;
        _list.PaintRow += OnPaintAchievementRow;
        _list.RetrieveVirtualItem += (_, e) =>
        {
            // the list can ask for a stale index while its size is being updated
            e.Item = e.ItemIndex < _shown.Count
                ? new ListViewItem(_shown[e.ItemIndex].DisplayName) { ToolTipText = _shown[e.ItemIndex].Description }
                : new ListViewItem();
        };

        (_statsPanel, _statsGrid) = CreateStatsPage();
        _statsPanel.Visible = false;

        _downloadLabel.Image = Glyphs.Image(Glyphs.Download, LogicalToDeviceUnits(14), Theme.SubtleText);
        _downloadLabel.Visible = false;

        // docking runs from the last added control inwards
        Controls.Add(_list);
        Controls.Add(_statsPanel);
        Controls.Add(_infoBar);
        Controls.Add(_header);
        Controls.Add(toolbar);
        Controls.Add(Ui.StatusBar(this, _statusLabel, _downloadLabel));

        ShowPage(false);
        steam.OnCallback<UserStatsReceived>(OnUserStatsReceived);
        RefreshStats();
    }

    private ToolStripMenuItem ShowItem(string text, StateFilter show)
    {
        ToolStripMenuItem item = new(text);
        item.Click += (_, _) =>
        {
            _show = show;
            _showAll.Checked = show == StateFilter.All;
            _showLocked.Checked = show == StateFilter.Locked;
            _showUnlocked.Checked = show == StateFilter.Unlocked;
            _showButton.Text = show switch
            {
                StateFilter.Locked => "Locked", StateFilter.Unlocked => "Unlocked", _ => "All"
            };
            _showButton.HideDropDown();
            ShowAchievements();
        };
        return item;
    }

    private (Panel, DataGridView) CreateStatsPage()
    {
        var padding = LogicalToDeviceUnits(12);
        Panel panel = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Background,
            Padding = new Padding(padding, LogicalToDeviceUnits(4), padding, padding)
        };

        DataGridView grid = new()
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ColumnHeadersHeight = LogicalToDeviceUnits(36),
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            EnableHeadersVisualStyles = false,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            BackgroundColor = Theme.Surface,
            GridColor = Theme.Border
        };
        Padding cellPadding = new(LogicalToDeviceUnits(10), 0, LogicalToDeviceUnits(10), 0);
        grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Theme.Surface, ForeColor = Theme.Text, SelectionBackColor = Theme.SurfaceSelected,
            SelectionForeColor = Theme.Text, Padding = cellPadding
        };
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Theme.Surface, ForeColor = Theme.SubtleText, SelectionBackColor = Theme.Surface,
            SelectionForeColor = Theme.SubtleText, Font = Theme.SmallBoldFont, Padding = cellPadding
        };
        grid.RowTemplate.Height = LogicalToDeviceUnits(34);

        DataGridViewTextBoxColumn value = new()
        {
            DataPropertyName = "Value", HeaderText = "Value", ReadOnly = true, FillWeight = 25
        };
        grid.Columns.AddRange(
            new DataGridViewTextBoxColumn
            {
                DataPropertyName = "DisplayName", HeaderText = "Name", ReadOnly = true, FillWeight = 50
            },
            value,
            new DataGridViewTextBoxColumn
            {
                DataPropertyName = "Extra", HeaderText = "Flags", ReadOnly = true, FillWeight = 25,
                DefaultCellStyle = { ForeColor = Theme.SubtleText }
            });
        grid.DataSource = new BindingSource { DataSource = _statistics };
        grid.CellFormatting += (_, e) =>
        {
            if (e.Value is "None") e.Value = "";
        };
        grid.DataError += OnStatDataError;
        grid.CellEndEdit += (_, _) => UpdateHeader();

        CheckBox allow = new()
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            ForeColor = Theme.SubtleText,
            Padding = new Padding(0, LogicalToDeviceUnits(10), 0, 0),
            Text = "Let me edit values. I understand changing stats can break things and only I am to blame."
        };
        allow.CheckedChanged += (_, _) => value.ReadOnly = allow.Checked == false;

        panel.Controls.Add(grid);
        panel.Controls.Add(allow);
        return (panel, grid);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.S:
                if (_saveButton.Enabled) OnSave(null, EventArgs.Empty);
                return true;
            case Keys.F5:
                if (_refreshButton.Enabled) RefreshStats();
                return true;
            case Keys.Control | Keys.F:
                ShowPage(false);
                _searchBox.Focus();
                _searchBox.SelectAll();
                return true;
            case Keys.Control | Keys.A when _list.Focused:
                _list.SelectAllRows();
                return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void ShowPage(bool statistics)
    {
        _achievementsTab.Checked = statistics == false;
        _statisticsTab.Checked = statistics;
        foreach (var item in _achievementTools) item.Visible = statistics == false;
        _list.Visible = statistics == false;
        _statsPanel.Visible = statistics;
        (statistics ? (Control)_statsGrid : _list).Focus();
    }

    private void RefreshStats()
    {
        _isLoaded = false;
        _isAwaitingStats = true;
        _achievements.Clear();
        _statistics.Clear();
        ShowAchievements();
        _list.EmptyText = "Loading achievements…";
        EnableInput(false);

        // the result arrives as the UserStatsReceived callback
        if (_steam.UserStats.RequestUserStats(_steam.SteamId) == 0)
        {
            _infoBar.Show(Severity.Error, "Couldn't request stats from Steam.",
                [new InfoAction("Try again", RefreshStats)]);
            _list.EmptyText = "Couldn't load achievements.";
            _refreshButton.Enabled = true;
            return;
        }

        _statusLabel.Text = "Retrieving achievements and statistics…";
        UpdateHeader();
    }

    private void OnUserStatsReceived(UserStatsReceived param)
    {
        if (_isAwaitingStats == false) return;
        _isAwaitingStats = false;

        _refreshButton.Enabled = true;
        _statusLabel.Text = "";

        if (param.Result != 1)
        {
            ShowLoadError(param.Result == 2
                ? "Steam returned no stats — this usually means you don't own the game."
                : $"Steam returned error {param.Result} while retrieving stats.");
            return;
        }

        _schema = GameSchema.Load(_gameId, _steam.Apps.GetCurrentGameLanguage());
        if (_schema == null)
        {
            ShowLoadError("This game has no achievement data in Steam's cache. Launch it once, then refresh.");
            return;
        }

        try
        {
            LoadAchievements();
            LoadStatistics();
        }
        catch (Exception e)
        {
            ShowLoadError($"Error while reading stats: {e.Message}", e.ToString());
            return;
        }

        _isLoaded = true;
        EnableInput(true);
        _list.EmptyText = _achievements.Count == 0 ? "This game has no achievements." : "No achievements match.";
        ShowAchievements();
    }

    private void ShowLoadError(string message, string details = null)
    {
        _list.EmptyText = "Couldn't load achievements.";
        _list.Invalidate();
        _infoBar.Show(Severity.Error, message, [new InfoAction("Try again", RefreshStats)], details);
        UpdateHeader();
    }

    private void LoadAchievements()
    {
        _achievements.Clear();
        foreach (var def in _schema.Achievements)
        {
            if (string.IsNullOrEmpty(def.Id) ||
                _steam.UserStats.GetAchievement(def.Id, out var isAchieved, out var unlockTime) == false)
                continue;

            _achievements.Add(new AchievementInfo(def, isAchieved,
                isAchieved && unlockTime > 0 ? DateTimeOffset.FromUnixTimeSeconds(unlockTime).LocalDateTime : null));
        }
    }

    private void LoadStatistics()
    {
        _statistics.Clear();
        foreach (var def in _schema.Stats)
            if (string.IsNullOrEmpty(def.Id) == false &&
                _steam.UserStats.GetStat(def.Id, def.IsFloat, out var value))
                _statistics.Add(new StatInfo(def, value));
    }

    private void ShowAchievements()
    {
        var search = _searchBox.Text.Trim();
        _shown.Clear();
        _shown.AddRange(_achievements.Where(a =>
            (_show == StateFilter.All || a.IsAchieved == (_show == StateFilter.Unlocked)) &&
            (a.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
             a.Description.Contains(search, StringComparison.OrdinalIgnoreCase))));
        _list.SetCount(_shown.Count);
        UpdateHeader();
    }

    private void UpdateHeader()
    {
        if (_isLoaded == false)
        {
            _header.Subtitle.Text = "Loading…";
            _header.Progress.Value = 0;
            return;
        }

        var unlocked = _achievements.Count(a => a.IsAchieved);
        var changes = _achievements.Count(a => a.IsModified) + _statistics.Count(s => s.IsModified);
        var text = _achievements.Count == 0
            ? "No achievements"
            : $"{unlocked} of {_achievements.Count} achievements unlocked ({100 * unlocked / _achievements.Count}%)";
        if (changes > 0) text += $"  ·  {changes} unsaved change{(changes == 1 ? "" : "s")}";
        _header.Subtitle.Text = text;
        _header.Progress.Value = _achievements.Count == 0 ? 0 : (float)unlocked / _achievements.Count;
    }

    private void EnableInput(bool enabled)
    {
        _saveButton.Enabled = enabled;
        _resetButton.Enabled = enabled;
        _refreshButton.Enabled = enabled;
    }

    private void OnToggleAchievements(IReadOnlyList<int> indices)
    {
        var picked = indices.Where(i => i < _shown.Count).Select(i => _shown[i]).ToList();
        var editable = picked.Where(a => a.Definition.IsProtected == false).ToList();
        var value = editable.Any(a => a.Checked == false);
        foreach (var achievement in editable) achievement.Checked = value;

        var locked = picked.Count - editable.Count;
        if (locked > 0)
            _infoBar.Show(Severity.Warning, locked == 1 && picked.Count == 1
                ? $"\"{picked[0].DisplayName}\" is protected: only the game's servers can change it."
                : $"Skipped {locked} protected achievement{(locked == 1 ? "" : "s")}: only the game's servers can change them.");

        _list.Invalidate();
        UpdateHeader();
    }

    private void SetAll(Func<AchievementInfo, bool> change)
    {
        var skipped = 0;
        foreach (var achievement in _shown)
        {
            var value = change(achievement);
            if (achievement.Definition.IsProtected)
            {
                if (value != achievement.Checked) skipped++;
                continue;
            }

            achievement.Checked = value;
        }

        if (skipped > 0)
            _infoBar.Show(Severity.Info,
                $"Skipped {skipped} protected achievement{(skipped == 1 ? "" : "s")}: only the game's servers can change them.");

        _list.Invalidate();
        UpdateHeader();
    }

    private void OnPaintAchievementRow(object sender, RowPaintEventArgs e)
    {
        if (e.Index >= _shown.Count) return;

        var a = _shown[e.Index];
        var g = e.Graphics;
        var bounds = e.Bounds;

        var iconSize = LogicalToDeviceUnits(44);
        Rectangle iconRect = new(bounds.X, bounds.Y + (bounds.Height - iconSize) / 2, iconSize, iconSize);
        var radius = LogicalToDeviceUnits(6);
        if (string.IsNullOrEmpty(a.Icon) == false && _icons.TryGetValue(a.Icon, out var icon))
        {
            Theme.DrawRoundedImage(g, icon, iconRect, radius);
        }
        else
        {
            Theme.FillRounded(g, Theme.Placeholder, iconRect, radius);
            Glyphs.Draw(g, Glyphs.Star, iconRect, Theme.DisabledText, LogicalToDeviceUnits(18));
            QueueIcon(a.Icon);
        }

        // right column: pending change, unlock date or lock state, and the protected badge
        const TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis |
                                      TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        var smallHeight = TextRenderer.MeasureText(g, "Ag", Theme.SmallFont).Height;
        var rightWidth = LogicalToDeviceUnits(170);
        var right = Rectangle.FromLTRB(bounds.Right - rightWidth, bounds.Top, bounds.Right, bounds.Bottom);
        var lines = a.Definition.IsProtected ? 2 : 1;
        var lineGap = LogicalToDeviceUnits(6);
        var pillHeight = smallHeight + LogicalToDeviceUnits(6);
        var y = bounds.Y + (bounds.Height - pillHeight * lines - lineGap * (lines - 1)) / 2;

        if (a.IsModified)
        {
            var color = a.Checked ? Theme.Accent : Theme.Danger;
            DrawPill(g, a.Checked ? "Will unlock" : "Will lock", color, right.Right, y, pillHeight);
        }
        else
        {
            var status = a.IsAchieved
                ? a.UnlockTime is { } time ? time.ToString("d MMM yyyy, HH:mm", CultureInfo.CurrentCulture) : "Unlocked"
                : "Locked";
            TextRenderer.DrawText(g, status, Theme.SmallFont,
                new Rectangle(right.X, y, right.Width, pillHeight),
                a.IsAchieved ? Theme.SubtleText : Theme.DisabledText,
                flags | TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }

        if (a.Definition.IsProtected)
            DrawPill(g, "Protected", Theme.SubtleText, right.Right, y + pillHeight + lineGap, pillHeight,
                Glyphs.Shield);

        // name and description
        var textRight = right.Left - LogicalToDeviceUnits(12);
        var left = iconRect.Right + LogicalToDeviceUnits(14);
        var nameHeight = TextRenderer.MeasureText(g, "Ag", Theme.RowTitleFont).Height;
        var hasDescription = a.Description.Length > 0;
        var top = bounds.Y +
                  (bounds.Height - nameHeight - (hasDescription ? smallHeight + LogicalToDeviceUnits(3) : 0)) / 2;
        TextRenderer.DrawText(g, a.DisplayName, Theme.RowTitleFont,
            Rectangle.FromLTRB(left, top, textRight, top + nameHeight),
            a.IsAchieved || a.Checked ? Theme.Text : Theme.SubtleText, flags);
        if (hasDescription)
            TextRenderer.DrawText(g, a.Description, Theme.SmallFont,
                Rectangle.FromLTRB(left, top + nameHeight + LogicalToDeviceUnits(3), textRight, bounds.Bottom),
                Theme.SubtleText, flags);
    }

    // a small rounded label, right-aligned at x
    private void DrawPill(Graphics g, string text, Color color, int right, int y, int height, char glyph = '\0')
    {
        var textWidth = TextRenderer.MeasureText(g, text, Theme.SmallBoldFont, Size.Empty, TextFormatFlags.NoPadding)
            .Width;
        var glyphSize = glyph == 0 ? 0 : LogicalToDeviceUnits(12);
        var gap = glyph == 0 ? 0 : LogicalToDeviceUnits(5);
        var pad = LogicalToDeviceUnits(9);
        var width = textWidth + glyphSize + gap + pad * 2;
        RectangleF pill = new(right - width, y, width, height);

        Theme.FillRounded(g, Color.FromArgb(Theme.IsDark ? 50 : 30, color), pill, height / 2f);
        if (glyph != 0)
            Glyphs.Draw(g, glyph, new RectangleF(pill.X + pad, y + (height - glyphSize) / 2f, glyphSize, glyphSize),
                color);
        TextRenderer.DrawText(g, text, Theme.SmallBoldFont,
            new Rectangle((int)pill.X + pad + glyphSize + gap, y, textWidth + 2, height), color,
            TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private void QueueIcon(string icon)
    {
        if (string.IsNullOrEmpty(icon) || _icons.ContainsKey(icon) || _iconsAttempted.Contains(icon) ||
            _iconsQueued.Add(icon) == false)
            return;

        _iconQueue.Enqueue(icon);
        BeginInvoke(new Action(DownloadIcons));
    }

    // downloads queued icons one at a time
    private async void DownloadIcons()
    {
        if (_isDownloadingIcons || IsDisposed) return;

        _isDownloadingIcons = true;
        while (_iconQueue.TryDequeue(out var icon))
        {
            _iconsQueued.Remove(icon);
            _downloadLabel.Text = $"Downloading {1 + _iconQueue.Count} icons…";
            _downloadLabel.Visible = true;

            _iconsAttempted.Add(icon);
            var bitmap = await Http.TryGetImageAsync(
                $"https://cdn.steamstatic.com/steamcommunity/public/images/apps/{_gameId}/{icon}");
            if (IsDisposed) return;
            if (bitmap == null) continue;

            _icons[icon] = bitmap;
            _list.Invalidate();
        }

        _downloadLabel.Visible = false;
        _isDownloadingIcons = false;
    }

    // returns the number of changed achievements, or -1 on failure
    private int StoreAchievements()
    {
        var changed = _achievements.Where(a => a.IsModified).ToList();
        foreach (var achievement in changed)
        {
            if (_steam.UserStats.SetAchievement(achievement.Definition.Id, achievement.Checked) == false)
            {
                _infoBar.Show(Severity.Error,
                    $"Steam refused to change \"{achievement.DisplayName}\", so nothing was saved.");
                return -1;
            }

            achievement.IsAchieved = achievement.Checked;
        }

        return changed.Count;
    }

    // returns the number of changed stats, or -1 on failure
    private int StoreStatistics()
    {
        var changed = _statistics.Where(stat => stat.IsModified).ToList();
        foreach (var stat in changed)
            if (_steam.UserStats.SetStat(stat.Definition.Id, stat.Value) == false)
            {
                _infoBar.Show(Severity.Error,
                    $"Steam refused the value for \"{stat.DisplayName}\", so nothing was saved.");
                return -1;
            }

        return changed.Count;
    }

    private void OnSave(object sender, EventArgs e)
    {
        _statsGrid.EndEdit();

        // on failure, reload so the list shows what Steam really has (some changes may be half applied)
        var achievements = StoreAchievements();
        var stats = achievements < 0 ? -1 : StoreStatistics();
        if (stats < 0)
        {
            RefreshStats();
            return;
        }

        if (achievements + stats == 0)
        {
            _infoBar.Show(Severity.Info, "There are no changes to save.");
            return;
        }

        if (_steam.UserStats.StoreStats() == false)
        {
            _infoBar.Show(Severity.Error, "Steam couldn't store the changes.");
            RefreshStats();
            return;
        }

        var parts = new List<string>();
        if (achievements > 0) parts.Add($"{achievements} achievement{(achievements == 1 ? "" : "s")}");
        if (stats > 0) parts.Add($"{stats} statistic{(stats == 1 ? "" : "s")}");
        _infoBar.Show(Severity.Success, $"Saved {string.Join(" and ", parts)}.");

        RefreshStats();
    }

    private void OnReset(object sender, EventArgs e)
    {
        _infoBar.Show(Severity.Warning,
            "Reset all statistics for this game? This can't be undone.",
            [
                new InfoAction("Reset statistics", () => ResetStats(false)),
                new InfoAction("Reset statistics and achievements", () => ResetStats(true)),
                new InfoAction("Cancel", null)
            ]);
    }

    private void ResetStats(bool achievementsToo)
    {
        if (_steam.UserStats.ResetAllStats(achievementsToo) == false)
        {
            _infoBar.Show(Severity.Error, "Steam couldn't reset the statistics.");
            return;
        }

        _infoBar.Show(Severity.Success, achievementsToo ? "Statistics and achievements reset." : "Statistics reset.");
        RefreshStats();
    }

    private void OnStatDataError(object sender, DataGridViewDataErrorEventArgs e)
    {
        if (e.Context.HasFlag(DataGridViewDataErrorContexts.Commit) == false) return;

        e.ThrowException = false;
        e.Cancel = true;
        _infoBar.Show(Severity.Warning, e.Exception is StatIsProtectedException
            ? "That statistic is protected and can't be changed."
            : "That isn't a valid value for this statistic.");
    }
}