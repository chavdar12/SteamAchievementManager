using System.ComponentModel;
using System.Diagnostics;
using System.Xml.XPath;
using SteamAchievementManager.Common;
using SteamAchievementManager.Steam;

namespace SteamAchievementManager.Picker;

// lists the owned games; each game is managed in its own process,
// because Steam binds a process to a single app ID
internal sealed partial class GamePicker : Form
{
    private const string GameListUrl = "https://gib.me/sam/games.xml";
    private const uint DefaultGameId = 480; // Spacewar

    private readonly SteamClient _steam;
    private readonly Dictionary<uint, GameInfo> _games = [];
    private readonly List<GameInfo> _filteredGames = [];

    private readonly Queue<GameInfo> _logoQueue = new();
    private readonly HashSet<string> _logosQueued = [];
    private readonly HashSet<string> _logosAttempted = [];
    private bool _isDownloadingLogos;

    public GamePicker(SteamClient steam)
    {
        InitializeComponent();

        Bitmap blank = new(_LogoImageList.ImageSize.Width, _LogoImageList.ImageSize.Height);
        using (var g = Graphics.FromImage(blank)) g.Clear(Color.DimGray);
        _LogoImageList.Images.Add("Blank", blank);

        _steam = steam;
        _steam.OnCallback<AppDataChanged>(OnAppDataChanged);

        LoadGames();
    }

    private void OnAppDataChanged(AppDataChanged param)
    {
        if (param.Result == false || _games.TryGetValue(param.AppId, out var game) == false) return;

        game.Name = _steam.Apps.GetAppData(game.Id, "name");
        QueueLogo(game);
        DownloadLogos();
    }

    private async void LoadGames()
    {
        _games.Clear();
        _RefreshGamesButton.Enabled = false;
        try
        {
            _PickerStatusLabel.Text = "Downloading game list...";
            var list = await Http.Client.GetByteArrayAsync(GameListUrl);

            _PickerStatusLabel.Text = "Checking game ownership...";
            var owned = await Task.Run(() => ParseGameList(list).Select(g => TryGetOwnedGame(g.Id, g.Type))
                .Where(g => g != null).ToList());
            foreach (var game in owned) _games.TryAdd(game.Id, game);
        }
        catch (Exception e)
        {
            AddGame(DefaultGameId, "normal");
            Dialogs.Error(this, e.ToString());
        }

        RefreshGames();
        _RefreshGamesButton.Enabled = true;
        DownloadLogos();
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

    private GameInfo TryGetOwnedGame(uint id, string type) =>
        _steam.Apps.IsSubscribedApp(id) ? new GameInfo(id, type, _steam.Apps.GetAppData(id, "name")) : null;

    private void AddGame(uint id, string type)
    {
        if (_games.ContainsKey(id) == false && TryGetOwnedGame(id, type) is { } game) _games.Add(id, game);
    }

    private void RefreshGames()
    {
        var search = _SearchGameTextBox.Text;
        var wantedTypes = new Dictionary<string, bool>
        {
            ["normal"] = _FilterGamesMenuItem.Checked,
            ["demo"] = _FilterDemosMenuItem.Checked,
            ["mod"] = _FilterModsMenuItem.Checked,
            ["junk"] = _FilterJunkMenuItem.Checked
        };

        _filteredGames.Clear();
        _filteredGames.AddRange(_games.Values
            .Where(g => g.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
            .Where(g => wantedTypes.GetValueOrDefault(g.Type, true))
            .OrderBy(g => g.Name));

        _GameListView.VirtualListSize = _filteredGames.Count;
        _PickerStatusLabel.Text = $"Displaying {_filteredGames.Count} games. Total {_games.Count} games.";

        if (_filteredGames.Count > 0)
        {
            _GameListView.Items[0].Selected = true;
            _GameListView.Select();
        }
    }

    private void OnGameListViewRetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
    {
        var game = _filteredGames[e.ItemIndex];
        e.Item = game.Item = new ListViewItem { Text = game.Name, ImageIndex = game.ImageIndex };
    }

    // type-ahead: find the next game whose name starts with the typed text
    private void OnGameListViewSearchForVirtualItem(object sender, SearchForVirtualItemEventArgs e)
    {
        if (e.Direction != SearchDirectionHint.Down || e.IsTextSearch == false) return;

        var count = _filteredGames.Count;
        if (count < 2) return;

        var start = e.StartIndex;
        Predicate<GameInfo> matches = g => g.Name.StartsWith(e.Text, StringComparison.CurrentCultureIgnoreCase);

        int index;
        if (start >= count)
        {
            index = _filteredGames.FindIndex(0, start - 1, matches);
        }
        else if (start <= 0)
        {
            index = _filteredGames.FindIndex(0, count, matches);
        }
        else
        {
            index = _filteredGames.FindIndex(start, count - start, matches);
            if (index < 0) index = _filteredGames.FindIndex(0, start - 1, matches);
        }

        e.Index = index;
    }

    private void OnGameListViewDrawItem(object sender, DrawListViewItemEventArgs e)
    {
        e.DrawDefault = true;

        if (e.Item.Bounds.IntersectsWith(_GameListView.ClientRectangle) == false) return;

        var game = _filteredGames[e.ItemIndex];
        if (game.ImageIndex <= 0)
        {
            QueueLogo(game);
            DownloadLogos();
        }
    }

    private void OnGameListViewDrawSubItem(object sender, DrawListViewSubItemEventArgs e)
    {
        e.DrawDefault = true;
    }

    private void OnGameListViewResize(object sender, EventArgs e)
    {
        if (_GameListView.Columns.Count > 0) _GameListView.Columns[0].Width = _GameListView.ClientSize.Width;
    }

    private void QueueLogo(GameInfo game)
    {
        if (game.ImageIndex > 0) return;

        game.ImageUrl = GetLogoUrl(game.Id);
        if (game.ImageUrl == null) return;

        var cached = _LogoImageList.Images.IndexOfKey(game.ImageUrl);
        if (cached >= 0)
            game.ImageIndex = cached;
        else if (_logosAttempted.Contains(game.ImageUrl) == false && _logosQueued.Add(game.ImageUrl))
            _logoQueue.Enqueue(game);
    }

    // downloads queued logos one at a time, skipping games that scrolled out of view
    private async void DownloadLogos()
    {
        if (_isDownloadingLogos) return;

        _isDownloadingLogos = true;
        while (_logoQueue.TryDequeue(out var game))
        {
            _logosQueued.Remove(game.ImageUrl);
            if (IsShown(game) == false) continue;

            _DownloadStatusLabel.Text = $"Downloading {1 + _logoQueue.Count} game icons...";
            _DownloadStatusLabel.Visible = true;

            _logosAttempted.Add(game.ImageUrl);
            var logo = await Http.TryGetImageAsync(game.ImageUrl);
            if (IsDisposed) return;
            if (logo == null) continue;

            _GameListView.BeginUpdate();
            game.ImageIndex = _LogoImageList.Images.Count;
            _LogoImageList.Images.Add(game.ImageUrl, logo);
            _GameListView.EndUpdate();
        }

        _DownloadStatusLabel.Visible = false;
        _isDownloadingLogos = false;
    }

    private bool IsShown(GameInfo game) =>
        game.Item != null &&
        _filteredGames.Contains(game) &&
        game.Item.Bounds.IntersectsWith(_GameListView.ClientRectangle);

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

    private List<GameInfo> SelectedGames =>
        _GameListView.SelectedIndices.Cast<int>().Where(i => i >= 0 && i < _filteredGames.Count)
            .Select(i => _filteredGames[i]).ToList();

    private static Process StartManager(uint appId, bool unlockAll) =>
        Process.Start(Environment.ProcessPath!, unlockAll ? $"{appId} --unlock-all" : $"{appId}");

    private void OnActivateGame(object sender, EventArgs e)
    {
        try
        {
            foreach (var game in SelectedGames) StartManager(game.Id, false)?.Dispose();
        }
        catch (Win32Exception)
        {
            Dialogs.Error(this, "Failed to start the game manager.");
        }
    }

    private void OnRefresh(object sender, EventArgs e)
    {
        _AddGameTextBox.Text = "";
        LoadGames();
    }

    private void OnAddGame(object sender, EventArgs e)
    {
        if (uint.TryParse(_AddGameTextBox.Text, out var id) == false)
        {
            Dialogs.Error(this, "Please enter a valid game ID.");
            return;
        }

        if (_steam.Apps.IsSubscribedApp(id) == false)
        {
            Dialogs.Error(this, "You don't own that game.");
            return;
        }

        // only this game will be shown, so drop the pending logo downloads
        _logoQueue.Clear();
        _logosQueued.Clear();

        _AddGameTextBox.Text = "";
        _games.Clear();
        AddGame(id, "normal");
        _FilterGamesMenuItem.Checked = true;
        RefreshGames();
        DownloadLogos();
    }

    private void OnFilterUpdate(object sender, EventArgs e)
    {
        RefreshGames();

        // keep focus in the search box; the list view's type-ahead search would take it on KeyUp
        _SearchGameTextBox.Focus();
    }

    private void OnSelectAll(object sender, EventArgs e)
    {
        _GameListView.SelectAll();
        _GameListView.Select();
    }

    private async void OnUnlockAllSelected(object sender, EventArgs e)
    {
        var games = SelectedGames;
        if (games.Count == 0) return;

        if (Dialogs.Confirm(this,
                $"Unlock every achievement in {games.Count} game(s)?\n\n" +
                "This is visible on your Steam profile. Protected achievements are skipped.",
                "Unlock All Achievements") == false)
            return;

        _UnlockAllButton.Enabled = false;
        List<string> failed = [];
        try
        {
            for (var i = 0; i < games.Count; i++)
            {
                _PickerStatusLabel.Text = $"Unlocking {i + 1}/{games.Count}: {games[i].Name}";
                var appId = games[i].Id;
                if (await Task.Run(() => RunUnlockAll(appId)) == false) failed.Add(games[i].Name);
            }
        }
        catch (Win32Exception)
        {
            Dialogs.Error(this, "Failed to start the game manager.");
            return;
        }
        finally
        {
            _UnlockAllButton.Enabled = true;
        }

        var succeeded = games.Count - failed.Count;
        _PickerStatusLabel.Text = $"Unlocked {succeeded}/{games.Count} games.";
        if (failed.Count == 0)
            Dialogs.Info(this, $"Done: {games.Count} game(s) processed.", "Unlock All Achievements");
        else
            Dialogs.Warning(this,
                $"Done: {succeeded}/{games.Count} succeeded. Failed:\n" + string.Join("\n", failed.Take(30)),
                "Unlock All Achievements");
    }

    private static bool RunUnlockAll(uint appId)
    {
        using var process = StartManager(appId, true);

        // a game whose stats never arrive is killed after 60s and counted as failed
        if (process.WaitForExit(60000) == false)
        {
            process.Kill();
            return false;
        }

        return process.ExitCode == 0;
    }
}
