using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Xml.XPath;
using SteamAchievementManager.Common;
using SteamAchievementManager.Steam;

namespace SteamAchievementManager.Picker;

// lists the owned games; each game is managed in its own process,
// because Steam binds a process to a single app ID
internal sealed class GamePicker : Form
{
    private const string GameListUrl = "https://gib.me/sam/games.xml";
    private const uint DefaultGameId = 480; // Spacewar
    private const int OpenWithoutAsking = 8;

    private readonly SteamClient _steam;
    private readonly Dictionary<uint, GameInfo> _games = [];
    private readonly List<GameInfo> _filteredGames = [];

    private readonly Queue<GameInfo> _logoQueue = new();
    private readonly HashSet<string> _logosQueued = [];
    private readonly HashSet<string> _logosAttempted = [];
    private readonly Dictionary<string, Image> _logos = [];
    private bool _isDownloadingLogos;

    private readonly ToolStripTextBox _searchBox;
    private readonly ToolStripMenuItem _showGames, _showDemos, _showMods, _showJunk, _onlyWithAchievements;
    private readonly ToolStripMenuItem _sortByName, _sortByRecent;
    private readonly ToolStripButton _refreshButton, _openButton, _unlockButton;
    private readonly ToolStripTextBox _addBox;
    private readonly Ui.Header _header;
    private readonly InfoBar _infoBar = new();
    private readonly ModernList _list = new();
    private readonly ToolStripStatusLabel _statusLabel = Ui.StatusLabel(true);
    private readonly ToolStripStatusLabel _downloadLabel = Ui.StatusLabel(false);

    private bool _isLoading;
    private bool _isUnlocking;
    private bool _stopUnlocking;

    public GamePicker(SteamClient steam)
    {
        _steam = steam;

        Text = "Steam Achievement Manager";
        Icon = Assets.App;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(LogicalToDeviceUnits(980), LogicalToDeviceUnits(660));
        MinimumSize = new Size(LogicalToDeviceUnits(720), LogicalToDeviceUnits(440));
        KeyPreview = true;

        var toolbar = Ui.Toolbar(this);
        _searchBox = Ui.SearchBox(this, "Search games", 200);
        _searchBox.TextChanged += (_, _) => RefreshGames();
        _searchBox.KeyDown += OnSearchKeyDown;

        var settings = Settings.Load();
        var filter = Ui.DropDown(this, "Show", Glyphs.Filter, "Choose which apps are listed and their order");
        _showGames = FilterItem("Games", settings.ShowGames);
        _showDemos = FilterItem("Demos", settings.ShowDemos);
        _showMods = FilterItem("Mods", settings.ShowMods);
        _showJunk = FilterItem("Other apps", settings.ShowJunk);
        _onlyWithAchievements = FilterItem("Only games with achievement data", settings.OnlyWithAchievements);
        _onlyWithAchievements.ToolTipText =
            "Games Steam has achievement data for on this PC; launch a game once to add it";
        _sortByName = SortItem("Sort by name");
        _sortByRecent = SortItem("Sort by recently played");
        (settings.GameSort == "recent" ? _sortByRecent : _sortByName).Checked = true;
        filter.DropDownItems.AddRange([
            _showGames, _showDemos, _showMods, _showJunk, new ToolStripSeparator(), _onlyWithAchievements,
            new ToolStripSeparator(), _sortByName, _sortByRecent
        ]);

        _addBox = Ui.SearchBox(this, "App ID", 70);
        _addBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            OnAddGame(null, EventArgs.Empty);
        };

        _unlockButton = Ui.Button(this, "Unlock all achievements", Glyphs.Unlock, OnUnlockAllClicked,
            "Unlock every achievement in the ticked games", true);
        _openButton = Ui.Button(this, "Open", Glyphs.Open, OnOpenClicked, "Manage the ticked games one by one");
        _refreshButton = Ui.Button(this, "Refresh", Glyphs.Refresh, (_, _) => LoadGames(),
            "Reload the game list (F5)", iconOnly: true);
        foreach (var item in new ToolStripItem[] { _unlockButton, _openButton, _refreshButton })
            item.Alignment = ToolStripItemAlignment.Right;

        toolbar.Items.AddRange([
            Ui.Icon(this, Glyphs.Search), _searchBox, filter,
            Ui.Separator(this),
            Ui.Button(this, "Select all", Glyphs.SelectAll, OnSelectAll, "Tick every listed game"),
            Ui.Button(this, "Clear", Glyphs.ClearSelection, OnClearSelection, "Untick all games"),
            Ui.Separator(this),
            _addBox, Ui.Button(this, "Add", Glyphs.Add, OnAddGame, "Show only the game with this app ID"),
            _unlockButton, _openButton, _refreshButton
        ]);

        _header = new Ui.Header(this) { Title = { Text = "Your games" } };

        _list.Dock = DockStyle.Fill;
        _list.RowHeight = LogicalToDeviceUnits(76);
        _list.IsChecked = i => i < _filteredGames.Count && _filteredGames[i].Checked;
        _list.ToggleRequested += OnToggleGames;
        _list.RowClick = OnRowClick;
        _list.PaintRow += OnPaintGameRow;
        _list.RetrieveVirtualItem += OnRetrieveVirtualItem;
        _list.SearchForVirtualItem += OnSearchForVirtualItem;
        _list.ItemActivate += OnActivateGame;
        _list.SelectedIndexChanged += (_, _) => UpdateSummary();
        _list.VirtualItemsSelectionRangeChanged += (_, _) => UpdateSummary();

        _downloadLabel.Image = Glyphs.Image(Glyphs.Download, LogicalToDeviceUnits(14), Theme.SubtleText);
        _downloadLabel.Visible = false;

        // docking runs from the last added control inwards
        Controls.Add(_list);
        Controls.Add(_infoBar);
        Controls.Add(_header);
        Controls.Add(toolbar);
        Controls.Add(Ui.StatusBar(this, _statusLabel, _downloadLabel));

        Settings.Restore(this, settings.Picker);
        Ui.WatchThemeChanges(this, _infoBar);
        _steam.OnCallback<AppDataChanged>(OnAppDataChanged);
        LoadGames();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (e.Cancel) return;

        Settings.Update(s =>
        {
            s.Picker = Settings.PlacementOf(this);
            s.ShowGames = _showGames.Checked;
            s.ShowDemos = _showDemos.Checked;
            s.ShowMods = _showMods.Checked;
            s.ShowJunk = _showJunk.Checked;
            s.OnlyWithAchievements = _onlyWithAchievements.Checked;
            s.GameSort = _sortByRecent.Checked ? "recent" : "name";
        });
    }

    private ToolStripMenuItem SortItem(string text)
    {
        ToolStripMenuItem item = new(text);
        item.Click += (_, _) =>
        {
            _sortByName.Checked = item == _sortByName;
            _sortByRecent.Checked = item == _sortByRecent;
            RefreshGames();
        };
        return item;
    }

    private ToolStripMenuItem FilterItem(string text, bool isChecked)
    {
        ToolStripMenuItem item = new(text) { CheckOnClick = true, Checked = isChecked };
        item.CheckedChanged += (_, _) => RefreshGames();
        return item;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.F:
                _searchBox.Focus();
                _searchBox.SelectAll();
                return true;
            case Keys.F5:
                if (_refreshButton.Enabled) LoadGames();
                return true;
            case Keys.Control | Keys.A when _list.Focused:
                _list.SelectAllRows();
                return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode is not (Keys.Down or Keys.Enter) || _filteredGames.Count == 0) return;

        e.SuppressKeyPress = true;
        _list.Focus();
        if (_list.SelectedIndices.Count == 0)
        {
            _list.SelectedIndices.Add(0);
            _list.FocusedItem = _list.Items[0];
        }
    }

    private void OnAppDataChanged(AppDataChanged param)
    {
        if (param.Result == false || _games.TryGetValue(param.AppId, out var game) == false) return;

        game.Name = _steam.Apps.GetAppData(game.Id, "name");
        game.LogoUrl = null;
        QueueLogo(game);
        DownloadLogos();
        _list.Invalidate();
    }

    private async void LoadGames()
    {
        // "Try again" / "Show all games" can be clicked while a load is already running
        if (_isLoading) return;

        _games.Clear();
        _addBox.Text = "";
        _isLoading = true;
        UpdateCommands();
        _filteredGames.Clear();
        _list.SetCount(0);
        _list.EmptyText = "Loading your games…";
        // games come from the achievement data Steam keeps on this PC, plus the community list
        // (which also knows games not launched yet and which apps are demos, mods or junk)
        var schemaIds = await Task.Run(CachedSchemaIds);
        var lastPlayed = await Task.Run(() => LocalConfig.LoadLastPlayed(_steam.SteamId));

        List<(uint Id, string Type)> listed = [];
        string listError = null;
        try
        {
            _statusLabel.Text = "Downloading game list…";
            var xml = await Http.Client.GetByteArrayAsync(GameListUrl);
            listed = ParseGameList(xml);
        }
        catch (Exception e)
        {
            listError = e.Message;
        }

        if (IsDisposed) return;

        _statusLabel.Text = "Checking which games you own…";
        var types = listed.GroupBy(g => g.Id).ToDictionary(g => g.Key, g => g.First().Type);
        var candidates = types.Keys.Union(schemaIds).ToList();
        List<GameInfo> owned = [];
        try
        {
            owned = await Task.Run(() => candidates
                .Select(id => TryGetOwnedGame(id, types.GetValueOrDefault(id, "normal"))).Where(g => g != null)
                .ToList());
        }
        catch (Exception e)
        {
            listError ??= e.Message;
        }

        if (IsDisposed) return;

        foreach (var game in owned)
        {
            game.HasAchievements = schemaIds.Contains(game.Id);
            game.LastPlayed = lastPlayed.GetValueOrDefault(game.Id);
            _games.TryAdd(game.Id, game);
        }

        if (_games.Count == 0) AddGame(DefaultGameId, "normal");

        if (listError != null)
            _infoBar.Show(Severity.Warning,
                $"Couldn't download the community game list ({listError}). Showing the games found on this PC.",
                [new InfoAction("Try again", LoadGames)]);
        else
            CheckForUpdate();

        _isLoading = false;
        _statusLabel.Text = "";
        _list.EmptyText = "No games match your search.";
        RefreshGames();
        UpdateCommands();
        DownloadLogos();
    }

    // app IDs with a UserGameStatsSchema_<id>.bin in Steam's cache
    private static HashSet<uint> CachedSchemaIds()
    {
        const string prefix = "UserGameStatsSchema_";
        try
        {
            return Directory
                .EnumerateFiles(Path.Combine(SteamLibrary.InstallPath ?? "", "appcache", "stats"), prefix + "*.bin")
                .Select(f => uint.TryParse(Path.GetFileNameWithoutExtension(f)[prefix.Length..], NumberStyles.None,
                    CultureInfo.InvariantCulture, out var id)
                    ? id
                    : 0)
                .Where(id => id != 0).ToHashSet();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private async void CheckForUpdate()
    {
        if (await UpdateCheck.FindNewerAsync() is not { } update || IsDisposed || _infoBar.Visible) return;

        _infoBar.Show(Severity.Info, $"Version {update.Version} is available.",
        [
            new InfoAction("Download", () => OpenInBrowser(update.Url)),
            new InfoAction("Later", null)
        ]);
    }

    private void OpenInBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            _infoBar.Show(Severity.Error, $"Couldn't open the browser. The download page is {url}");
        }
    }

    private static List<(uint Id, string Type)> ParseGameList(byte[] xml)
    {
        List<(uint, string)> games = [];
        var nodes = new XPathDocument(new MemoryStream(xml, false)).CreateNavigator().Select("/games/game");
        while (nodes.MoveNext())
        {
            var type = nodes.Current.GetAttribute("type", "");
            games.Add(((uint)nodes.Current.ValueAsLong, string.IsNullOrEmpty(type) ? "normal" : type));
        }

        return games;
    }

    private GameInfo TryGetOwnedGame(uint id, string type)
    {
        return _steam.Apps.IsSubscribedApp(id) ? new GameInfo(id, type, _steam.Apps.GetAppData(id, "name")) : null;
    }

    private void AddGame(uint id, string type)
    {
        if (_games.ContainsKey(id) || TryGetOwnedGame(id, type) is not { } game) return;

        game.HasAchievements = CachedSchemaIds().Contains(id);
        _games.Add(id, game);
    }

    private void RefreshGames()
    {
        if (_isLoading) return;

        var search = _searchBox.Text.Trim();
        var wantedTypes = new Dictionary<string, bool>
        {
            ["normal"] = _showGames.Checked,
            ["demo"] = _showDemos.Checked,
            ["mod"] = _showMods.Checked,
            ["junk"] = _showJunk.Checked
        };

        _filteredGames.Clear();
        _filteredGames.AddRange(_games.Values
            .Where(g => g.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                        g.Id.ToString().Equals(search, StringComparison.Ordinal))
            .Where(g => wantedTypes.GetValueOrDefault(g.Type, true))
            .Where(g => _onlyWithAchievements.Checked == false || g.HasAchievements)
            .OrderByDescending(g => _sortByRecent.Checked ? g.LastPlayed : 0)
            .ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase));

        _list.SetCount(_filteredGames.Count);
        UpdateSummary();
    }

    // games ticked in the list, or the highlighted rows if nothing is ticked
    private List<GameInfo> TargetGames()
    {
        var ticked = _games.Values.Where(g => g.Checked).OrderBy(g => g.Name).ToList();
        return ticked.Count > 0 ? ticked : HighlightedGames();
    }

    private List<GameInfo> HighlightedGames()
    {
        return _list.SelectedIndices.Cast<int>().Where(i => i >= 0 && i < _filteredGames.Count)
            .Select(i => _filteredGames[i]).ToList();
    }

    private void UpdateSummary()
    {
        if (_isLoading)
        {
            _header.Subtitle.Text = "Loading…";
            return;
        }

        var ticked = _games.Values.Count(g => g.Checked);
        var shown = _filteredGames.Count == _games.Count
            ? Plural(_games.Count, "game")
            : $"{_filteredGames.Count} of {Plural(_games.Count, "game")} shown";
        _header.Subtitle.Text = ticked > 0 ? $"{shown}  ·  {ticked} selected" : shown;
        UpdateCommands();
    }

    private void UpdateCommands()
    {
        var hasTargets = _isLoading == false && (_games.Values.Any(g => g.Checked) || _list.SelectedIndices.Count > 0);
        _refreshButton.Enabled = _isLoading == false && _isUnlocking == false;
        _openButton.Enabled = hasTargets;
        _unlockButton.Enabled = hasTargets && _isUnlocking == false;

        var count = TargetCountText();
        _unlockButton.Text = count == null ? "Unlock all achievements" : $"Unlock all achievements ({count})";
    }

    private string TargetCountText()
    {
        var ticked = _games.Values.Count(g => g.Checked);
        return ticked > 1 ? ticked.ToString() : null;
    }

    private static string Plural(int count, string word)
    {
        return count == 1 ? $"1 {word}" : $"{count:N0} {word}s";
    }

    private void OnToggleGames(IReadOnlyList<int> indices)
    {
        var games = indices.Where(i => i < _filteredGames.Count).Select(i => _filteredGames[i]).ToList();
        var value = games.Any(g => g.Checked == false);
        foreach (var game in games) game.Checked = value;
        UpdateSummary();
    }

    private void OnSelectAll(object sender, EventArgs e)
    {
        foreach (var game in _filteredGames) game.Checked = true;
        _list.Invalidate();
        UpdateSummary();
    }

    private void OnClearSelection(object sender, EventArgs e)
    {
        foreach (var game in _games.Values) game.Checked = false;
        _list.SelectedIndices.Clear();
        _list.Invalidate();
        UpdateSummary();
    }

    private void OnRetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
    {
        // the list can ask for a stale index while its size is being updated
        if (e.ItemIndex >= _filteredGames.Count)
        {
            e.Item = new ListViewItem();
            return;
        }

        var game = _filteredGames[e.ItemIndex];
        e.Item = new ListViewItem(game.Name) { ToolTipText = $"{game.Name} (app {game.Id})" };
    }

    // type-ahead: find the next game whose name starts with the typed text
    private void OnSearchForVirtualItem(object sender, SearchForVirtualItemEventArgs e)
    {
        if (e.Direction != SearchDirectionHint.Down || e.IsTextSearch == false) return;

        var count = _filteredGames.Count;
        if (count < 2) return;

        var start = Math.Clamp(e.StartIndex, 0, count);
        Predicate<GameInfo> matches = g => g.Name.StartsWith(e.Text, StringComparison.CurrentCultureIgnoreCase);
        var index = _filteredGames.FindIndex(start, count - start, matches);
        if (index < 0) index = _filteredGames.FindIndex(0, start, matches);
        e.Index = index;
    }

    private void OnPaintGameRow(object sender, RowPaintEventArgs e)
    {
        if (e.Index >= _filteredGames.Count) return;

        var game = _filteredGames[e.Index];
        var g = e.Graphics;
        var bounds = e.Bounds;

        // Steam's small capsule art is 184x69
        var logoHeight = bounds.Height - LogicalToDeviceUnits(20);
        Rectangle logo = new(bounds.X, bounds.Y + (bounds.Height - logoHeight) / 2, logoHeight * 184 / 69, logoHeight);
        var radius = LogicalToDeviceUnits(4);
        if (game.Logo != null)
        {
            Theme.DrawRoundedImage(g, game.Logo, logo, radius);
        }
        else
        {
            Theme.FillRounded(g, Theme.Placeholder, logo, radius);
            Glyphs.Draw(g, Glyphs.Game, logo, Theme.DisabledText, LogicalToDeviceUnits(22));
            QueueLogo(game);
            BeginInvoke(new Action(DownloadLogos));
        }

        // quick actions for this game, shown while the row is hovered or selected
        var textRight = bounds.Right;
        if (e.IsSelected || e.Index == _list.HotIndex)
        {
            var (unlock, lockAll) = QuickActionBounds(bounds);
            DrawQuickAction(g, unlock, "Unlock all", Glyphs.Unlock);
            DrawQuickAction(g, lockAll, "Lock all", Glyphs.Lock);
            textRight = unlock.Left - LogicalToDeviceUnits(12);
        }

        var left = logo.Right + LogicalToDeviceUnits(16);
        var nameHeight = TextRenderer.MeasureText(g, "Ag", Theme.RowTitleFont).Height;
        var detailHeight = TextRenderer.MeasureText(g, "Ag", Theme.SmallFont).Height;
        var top = bounds.Y + (bounds.Height - nameHeight - detailHeight - LogicalToDeviceUnits(2)) / 2;
        const TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis |
                                      TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

        TextRenderer.DrawText(g, game.Name, Theme.RowTitleFont,
            Rectangle.FromLTRB(left, top, textRight, top + nameHeight), Theme.Text, flags);

        var detail = $"App ID {game.Id}";
        if (game.Type != "normal") detail += "  ·  " + (game.Type == "junk" ? "Other" : Capitalize(game.Type));
        if (game.LastPlayed > 0)
            detail += "  ·  Last played " +
                      DateTimeOffset.FromUnixTimeSeconds(game.LastPlayed).LocalDateTime
                          .ToString("d MMM yyyy", CultureInfo.CurrentCulture);
        if (game.HasAchievements == false) detail += "  ·  No achievement data yet (launch the game once)";
        TextRenderer.DrawText(g, detail, Theme.SmallFont,
            Rectangle.FromLTRB(left, top + nameHeight + LogicalToDeviceUnits(2), textRight, bounds.Bottom),
            Theme.SubtleText, flags);
    }

    private (Rectangle Unlock, Rectangle Lock) QuickActionBounds(Rectangle content)
    {
        var height = LogicalToDeviceUnits(30);
        var y = content.Y + (content.Height - height) / 2;
        Rectangle lockAll = new(content.Right - LogicalToDeviceUnits(92), y, LogicalToDeviceUnits(92), height);
        Rectangle unlock = new(lockAll.Left - LogicalToDeviceUnits(108), y, LogicalToDeviceUnits(102), height);
        return (unlock, lockAll);
    }

    private void DrawQuickAction(Graphics g, Rectangle r, string text, char glyph)
    {
        var hot = r.Contains(_list.PointToClient(MousePosition));
        RectangleF shape = new(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1);
        var radius = LogicalToDeviceUnits(4);
        Theme.FillRounded(g, hot ? Theme.ControlHover : Theme.Surface, shape, radius);
        Theme.DrawRounded(g, Theme.Border, shape, radius);

        var icon = LogicalToDeviceUnits(14);
        var pad = LogicalToDeviceUnits(10);
        Glyphs.Draw(g, glyph, new RectangleF(r.X + pad, r.Y + (r.Height - icon) / 2f, icon, icon), Theme.Text);
        TextRenderer.DrawText(g, text, Font,
            Rectangle.FromLTRB(r.X + pad + icon + LogicalToDeviceUnits(6), r.Y, r.Right, r.Bottom),
            Theme.Text, TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private bool OnRowClick(int index, Point point)
    {
        // the buttons are only drawn on the hovered or selected row
        if (index >= _filteredGames.Count ||
            (index != _list.HotIndex && _list.SelectedIndices.Contains(index) == false))
            return false;

        var (unlock, lockAll) = QuickActionBounds(_list.ContentBounds(index));
        var isUnlock = unlock.Contains(point);
        if (isUnlock == false && lockAll.Contains(point) == false) return false;

        ConfirmSetAll([_filteredGames[index]], isUnlock);
        return true;
    }

    private static string Capitalize(string text)
    {
        return text.Length == 0 ? text : char.ToUpper(text[0]) + text[1..];
    }

    private void QueueLogo(GameInfo game)
    {
        if (game.Logo != null) return;

        // "" marks a game without art, so Steam isn't asked again on every repaint
        game.LogoUrl ??= GetLogoUrl(game.Id) ?? "";
        if (game.LogoUrl.Length == 0) return;

        if (_logos.TryGetValue(game.LogoUrl, out var cached))
            game.Logo = cached;
        else if (_logosAttempted.Contains(game.LogoUrl) == false && _logosQueued.Add(game.LogoUrl))
            _logoQueue.Enqueue(game);
    }

    // downloads queued logos one at a time, skipping games that scrolled out of view
    private async void DownloadLogos()
    {
        if (_isDownloadingLogos || IsDisposed) return;

        _isDownloadingLogos = true;
        while (_logoQueue.TryDequeue(out var game))
        {
            _logosQueued.Remove(game.LogoUrl);
            if (IsShown(game) == false) continue;

            _downloadLabel.Text = $"Downloading {1 + _logoQueue.Count} game images…";
            _downloadLabel.Visible = true;

            var url = game.LogoUrl;
            _logosAttempted.Add(url);
            var logo = await Http.TryGetImageAsync(url);
            if (IsDisposed) return;
            if (logo == null) continue;

            _logos[url] = logo;
            foreach (var other in _filteredGames.Where(g => g.LogoUrl == url)) other.Logo = logo;
            _list.InvalidateRow(_filteredGames.IndexOf(game));
        }

        _downloadLabel.Visible = false;
        _isDownloadingLogos = false;
    }

    private bool IsShown(GameInfo game)
    {
        var index = _filteredGames.IndexOf(game);
        return index >= 0 && _list.GetItemRect(index).IntersectsWith(_list.ClientRectangle);
    }

    // the small capsule in the current language, then in English, then the old community logo
    private string GetLogoUrl(uint id)
    {
        var language = _steam.Apps.GetCurrentGameLanguage();
        foreach (var lang in new[] { language, "english" }.Distinct())
        {
            var capsule = _steam.Apps.GetAppData(id, $"small_capsule/{lang}");
            if (string.IsNullOrEmpty(capsule) == false)
                return $"https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/{id}/{capsule}";
        }

        var logo = _steam.Apps.GetAppData(id, "logo");
        return string.IsNullOrEmpty(logo)
            ? null
            : $"https://cdn.steamstatic.com/steamcommunity/public/images/apps/{id}/{logo}.jpg";
    }

    private static Process StartManager(uint appId)
    {
        return Process.Start(Environment.ProcessPath!, $"{appId}");
    }

    // double-click or Enter opens the highlighted rows
    private void OnActivateGame(object sender, EventArgs e)
    {
        OpenGames(HighlightedGames());
    }

    private void OnOpenClicked(object sender, EventArgs e)
    {
        var games = TargetGames();
        if (games.Count <= OpenWithoutAsking)
        {
            OpenGames(games);
            return;
        }

        _infoBar.Show(Severity.Warning, $"Open {games.Count} windows, one for each selected game?",
            [new InfoAction($"Open {games.Count} windows", () => OpenGames(games)), new InfoAction("Cancel", null)]);
    }

    private void OpenGames(List<GameInfo> games)
    {
        try
        {
            foreach (var game in games) StartManager(game.Id)?.Dispose();
        }
        catch (Win32Exception e)
        {
            _infoBar.Show(Severity.Error, $"Couldn't start the achievement manager: {e.Message}");
        }
    }

    private void OnAddGame(object sender, EventArgs e)
    {
        if (uint.TryParse(_addBox.Text.Trim(), out var id) == false)
        {
            _infoBar.Show(Severity.Error, "Enter a numeric app ID, for example 480.");
            return;
        }

        if (_steam.Apps.IsSubscribedApp(id) == false)
        {
            _infoBar.Show(Severity.Warning, $"App {id} isn't in your Steam library.");
            return;
        }

        // only this game will be shown, so drop the pending logo downloads
        _logoQueue.Clear();
        _logosQueued.Clear();

        _addBox.Text = "";
        _searchBox.Text = "";
        _games.Clear();
        AddGame(id, "normal");
        _showGames.Checked = true;
        RefreshGames();
        _infoBar.Show(Severity.Info, $"Showing only app {id}. Refresh to see all your games again.",
            [new InfoAction("Show all games", LoadGames)]);
    }

    private void OnUnlockAllClicked(object sender, EventArgs e)
    {
        var games = TargetGames();
        if (games.Count == 0)
        {
            _infoBar.Show(Severity.Info, "Tick the games you want to complete first.");
            return;
        }

        ConfirmSetAll(games, true);
    }

    private void ConfirmSetAll(List<GameInfo> games, bool unlock)
    {
        if (_isUnlocking)
        {
            _infoBar.Show(Severity.Info, "Wait for the current run to finish, or stop it first.");
            return;
        }

        var verb = unlock ? "Unlock" : "Lock";
        var what = games.Count == 1 ? $"\"{games[0].Name}\"" : $"{games.Count} games";
        _infoBar.Show(Severity.Warning, unlock
                ? $"Unlock every achievement in {what}? This shows on your Steam profile; protected achievements are skipped."
                : $"Lock every achievement in {what}? This removes them from your Steam profile; protected achievements are skipped.",
            [new InfoAction($"{verb} all", () => SetAll(games, unlock)), new InfoAction("Cancel", null)]);
    }

    private async void SetAll(List<GameInfo> games, bool unlock)
    {
        var verb = unlock ? "Unlock" : "Lock";
        _isUnlocking = true;
        _stopUnlocking = false;
        UpdateCommands();
        _header.Progress.Value = 0;
        _header.Progress.Visible = true;

        List<(GameInfo Game, string Error)> failures = [];
        var unlocked = 0;
        var done = 0;
        try
        {
            foreach (var game in games)
            {
                if (_stopUnlocking) break;

                _infoBar.Show(Severity.Info, $"{verb}ing {done + 1} of {games.Count}: {game.Name}…",
                    [new InfoAction("Stop", StopUnlocking)]);
                _statusLabel.Text = $"{verb}ing achievements in {game.Name}…";

                var (count, error) = await RunSetAll(game.Id, unlock);
                if (IsDisposed) return;

                if (error == null) unlocked += count;
                else failures.Add((game, error));

                done++;
                _header.Progress.Value = (float)done / games.Count;
            }
        }
        finally
        {
            _isUnlocking = false;
            _header.Progress.Visible = false;
            _statusLabel.Text = "";
            UpdateCommands();
        }

        var achievements = Plural(unlocked, "achievement");
        var stopped = done < games.Count ? $"Stopped after {done} of {games.Count} games. " : "";
        if (failures.Count == 0)
        {
            _infoBar.Show(Severity.Success, unlocked == 0
                ? $"{stopped}Nothing to {verb.ToLowerInvariant()} — every achievement that can change already is {verb.ToLowerInvariant()}ed."
                : $"{stopped}{verb}ed {achievements} in {Plural(done, "game")}.");
            return;
        }

        var succeeded = done - failures.Count;
        var details = failures.Select(f => $"{f.Game.Name}: {f.Error}").ToList();
        var shown = string.Join(";  ", details.Take(2));
        if (failures.Count > 2) shown += $"; and {failures.Count - 2} more";
        _infoBar.Show(Severity.Warning,
            $"{stopped}{verb}ed {achievements} in {succeeded} of {Plural(done, "game")}. Failed — {shown}",
            [new InfoAction("Retry failed", () => SetAll(failures.Select(f => f.Game).ToList(), unlock))],
            "Failed:\n" + string.Join("\n", details.Take(40)));
    }

    private void StopUnlocking()
    {
        _stopUnlocking = true;
        _infoBar.Show(Severity.Info, "Stopping after the current game…");
    }

    // runs "<exe> <appId> --unlock-all|--lock-all" without a window; it prints the number of changed
    // achievements, or the reason it failed on stderr with a non-zero exit code
    // never throws: any failure becomes the game's error message
    private static async Task<(int Count, string Error)> RunSetAll(uint appId, bool unlock)
    {
        try
        {
            return await RunSetAllProcess(appId, unlock);
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException)
        {
            return (0, $"couldn't run the achievement manager ({e.Message})");
        }
    }

    private static async Task<(int Count, string Error)> RunSetAllProcess(uint appId, bool unlock)
    {
        if (Environment.ProcessPath is not { } exe) return (0, "couldn't find this program's own path");

        using Process process = new();
        process.StartInfo = new ProcessStartInfo(exe,
            $"{appId} {(unlock ? "--unlock-all" : "--lock-all")}")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        process.Start();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();

        // a game whose stats never arrive is stopped and counted as failed
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(60));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill();
            }
            catch (InvalidOperationException)
            {
                // it exited on its own just now
            }

            return (0, "Steam didn't respond in time");
        }

        var message = (await error).Trim();
        if (process.ExitCode != 0) return (0, message.Length > 0 ? message : $"failed with code {process.ExitCode}");

        return (int.TryParse((await output).Trim(), out var count) ? count : 0, null);
    }
}