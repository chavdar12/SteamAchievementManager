/* Copyright (c) 2024 Rick (rick 'at' gibbed 'dot' us)
 *
 * This software is provided 'as-is', without any express or implied
 * warranty. In no event will the authors be held liable for any damages
 * arising from the use of this software.
 *
 * Permission is granted to anyone to use this software for any purpose,
 * including commercial applications, and to alter it and redistribute it
 * freely, subject to the following restrictions:
 *
 * 1. The origin of this software must not be misrepresented; you must not
 *    claim that you wrote the original software. If you use this software
 *    in a product, an acknowledgment in the product documentation would
 *    be appreciated but is not required.
 *
 * 2. Altered source versions must be plainly marked as such, and must not
 *    be misrepresented as being the original software.
 *
 * 3. This notice may not be removed or altered from any source
 *    distribution.
 */

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.XPath;
using static SAM.InvariantShorthand;
using APITypes = SAM.API.Types;

namespace SAM.Picker;

internal partial class GamePicker : Form
{
    private readonly API.Client _SteamClient;

    private readonly Dictionary<uint, GameInfo> _Games;
    private readonly List<GameInfo> _FilteredGames;

    private readonly object _LogoLock;
    private readonly HashSet<string> _LogosAttempting;
    private readonly HashSet<string> _LogosAttempted;
    private readonly ConcurrentQueue<GameInfo> _LogoQueue;

    private readonly API.Callbacks.AppDataChanged _AppDataChangedCallback;

    public GamePicker(API.Client client)
    {
        _Games = new Dictionary<uint, GameInfo>();
        _FilteredGames = new List<GameInfo>();
        _LogoLock = new object();
        _LogosAttempting = new HashSet<string>();
        _LogosAttempted = new HashSet<string>();
        _LogoQueue = new ConcurrentQueue<GameInfo>();

        InitializeComponent();

        Bitmap blank = new(_LogoImageList.ImageSize.Width, _LogoImageList.ImageSize.Height);
        using (var g = Graphics.FromImage(blank))
        {
            g.Clear(Color.DimGray);
        }

        _LogoImageList.Images.Add("Blank", blank);

        _SteamClient = client;

        _AppDataChangedCallback = client.CreateAndRegisterCallback<API.Callbacks.AppDataChanged>();
        _AppDataChangedCallback.OnRun += OnAppDataChanged;

        AddGames();
    }

    private void OnAppDataChanged(APITypes.AppDataChanged param)
    {
        if (param.Result == false) return;

        if (_Games.TryGetValue(param.Id, out var game) == false) return;

        game.Name = _SteamClient.SteamApps001.GetAppData(game.Id, "name");

        AddGameToLogoQueue(game);
        DownloadNextLogo();
    }

    private void DoDownloadList(object sender, DoWorkEventArgs e)
    {
        _PickerStatusLabel.Text = "Downloading game list...";

        byte[] bytes;
        using (WebClient downloader = new())
        {
            bytes = downloader.DownloadData(new Uri("https://gib.me/sam/games.xml"));
        }

        List<KeyValuePair<uint, string>> pairs = new();
        using (MemoryStream stream = new(bytes, false))
        {
            XPathDocument document = new(stream);
            var navigator = document.CreateNavigator();
            var nodes = navigator.Select("/games/game");
            while (nodes.MoveNext() == true)
            {
                var type = nodes.Current.GetAttribute("type", "");
                if (string.IsNullOrEmpty(type) == true) type = "normal";
                pairs.Add(new KeyValuePair<uint, string>((uint)nodes.Current.ValueAsLong, type));
            }
        }

        _PickerStatusLabel.Text = "Checking game ownership...";
        foreach (var kv in pairs) AddGame(kv.Key, kv.Value);
    }

    private void OnDownloadList(object sender, RunWorkerCompletedEventArgs e)
    {
        if (e.Error != null || e.Cancelled == true)
        {
            AddDefaultGames();
            MessageBox.Show(e.Error.ToString(), "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        RefreshGames();
        _RefreshGamesButton.Enabled = true;
        DownloadNextLogo();
    }

    private void RefreshGames()
    {
        var nameSearch = _SearchGameTextBox.Text.Length > 0
            ? _SearchGameTextBox.Text
            : null;

        var wantNormals = _FilterGamesMenuItem.Checked == true;
        var wantDemos = _FilterDemosMenuItem.Checked == true;
        var wantMods = _FilterModsMenuItem.Checked == true;
        var wantJunk = _FilterJunkMenuItem.Checked == true;

        _FilteredGames.Clear();
        foreach (var info in _Games.Values.OrderBy(gi => gi.Name))
        {
            if (nameSearch != null &&
                info.Name.IndexOf(nameSearch, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            var wanted = info.Type switch
            {
                "normal" => wantNormals,
                "demo" => wantDemos,
                "mod" => wantMods,
                "junk" => wantJunk,
                _ => true
            };
            if (wanted == false) continue;

            _FilteredGames.Add(info);
        }

        _GameListView.VirtualListSize = _FilteredGames.Count;
        _PickerStatusLabel.Text =
            $"Displaying {_GameListView.Items.Count} games. Total {_Games.Count} games.";

        if (_GameListView.Items.Count > 0)
        {
            _GameListView.Items[0].Selected = true;
            _GameListView.Select();
        }
    }

    private void OnGameListViewRetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
    {
        var info = _FilteredGames[e.ItemIndex];
        e.Item = info.Item = new ListViewItem
        {
            Text = info.Name,
            ImageIndex = info.ImageIndex
        };
    }

    private void OnGameListViewSearchForVirtualItem(object sender, SearchForVirtualItemEventArgs e)
    {
        if (e.Direction != SearchDirectionHint.Down || e.IsTextSearch == false) return;

        var count = _FilteredGames.Count;
        if (count < 2) return;

        var text = e.Text;
        var startIndex = e.StartIndex;

        Predicate<GameInfo> predicate;
        /*if (e.IsPrefixSearch == true)*/
        {
            predicate = gi => gi.Name != null && gi.Name.StartsWith(text, StringComparison.CurrentCultureIgnoreCase);
        }
        /*else
        {
            predicate = gi => gi.Name != null && string.Compare(gi.Name, text, StringComparison.CurrentCultureIgnoreCase) == 0;
        }*/

        int index;
        if (e.StartIndex >= count)
        {
            // starting from the last item in the list
            index = _FilteredGames.FindIndex(0, startIndex - 1, predicate);
        }
        else if (startIndex <= 0)
        {
            // starting from the first item in the list
            index = _FilteredGames.FindIndex(0, count, predicate);
        }
        else
        {
            index = _FilteredGames.FindIndex(startIndex, count - startIndex, predicate);
            if (index < 0) index = _FilteredGames.FindIndex(0, startIndex - 1, predicate);
        }

        e.Index = index < 0 ? -1 : index;
    }

    private void DoDownloadLogo(object sender, DoWorkEventArgs e)
    {
        var info = (GameInfo)e.Argument;

        _LogosAttempted.Add(info.ImageUrl);

        using (WebClient downloader = new())
        {
            try
            {
                var data = downloader.DownloadData(new Uri(info.ImageUrl));
                using (MemoryStream stream = new(data, false))
                {
                    Bitmap bitmap = new(stream);
                    e.Result = new LogoInfo(info.Id, bitmap);
                }
            }
            catch (Exception)
            {
                e.Result = new LogoInfo(info.Id, null);
            }
        }
    }

    private void OnDownloadLogo(object sender, RunWorkerCompletedEventArgs e)
    {
        if (e.Error != null || e.Cancelled == true) return;

        if (e.Result is LogoInfo logoInfo &&
            logoInfo.Bitmap != null &&
            _Games.TryGetValue(logoInfo.Id, out var gameInfo) == true)
        {
            _GameListView.BeginUpdate();
            var imageIndex = _LogoImageList.Images.Count;
            _LogoImageList.Images.Add(gameInfo.ImageUrl, logoInfo.Bitmap);
            gameInfo.ImageIndex = imageIndex;
            _GameListView.EndUpdate();
        }

        DownloadNextLogo();
    }

    private void DownloadNextLogo()
    {
        lock (_LogoLock)
        {
            if (_LogoWorker.IsBusy == true) return;

            GameInfo info;
            while (true)
            {
                if (_LogoQueue.TryDequeue(out info) == false)
                {
                    _DownloadStatusLabel.Visible = false;
                    return;
                }

                if (info.Item == null) continue;

                if (_FilteredGames.Contains(info) == false ||
                    info.Item.Bounds.IntersectsWith(_GameListView.ClientRectangle) == false)
                {
                    _LogosAttempting.Remove(info.ImageUrl);
                    continue;
                }

                break;
            }

            _DownloadStatusLabel.Text = $"Downloading {1 + _LogoQueue.Count} game icons...";
            _DownloadStatusLabel.Visible = true;

            _LogoWorker.RunWorkerAsync(info);
        }
    }

    private string GetGameImageUrl(uint id)
    {
        string candidate;

        var currentLanguage = _SteamClient.SteamApps008.GetCurrentGameLanguage();

        candidate = _SteamClient.SteamApps001.GetAppData(id, _($"small_capsule/{currentLanguage}"));
        if (string.IsNullOrEmpty(candidate) == false)
            return _($"https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/{id}/{candidate}");

        if (currentLanguage != "english")
        {
            candidate = _SteamClient.SteamApps001.GetAppData(id, "small_capsule/english");
            if (string.IsNullOrEmpty(candidate) == false)
                return _($"https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/{id}/{candidate}");
        }

        candidate = _SteamClient.SteamApps001.GetAppData(id, "logo");
        if (string.IsNullOrEmpty(candidate) == false)
            return _($"https://cdn.steamstatic.com/steamcommunity/public/images/apps/{id}/{candidate}.jpg");

        return null;
    }

    private void AddGameToLogoQueue(GameInfo info)
    {
        if (info.ImageIndex > 0) return;

        var imageUrl = GetGameImageUrl(info.Id);
        if (string.IsNullOrEmpty(imageUrl) == true) return;

        info.ImageUrl = imageUrl;

        var imageIndex = _LogoImageList.Images.IndexOfKey(imageUrl);
        if (imageIndex >= 0)
        {
            info.ImageIndex = imageIndex;
        }
        else if (
            _LogosAttempting.Contains(imageUrl) == false &&
            _LogosAttempted.Contains(imageUrl) == false)
        {
            _LogosAttempting.Add(imageUrl);
            _LogoQueue.Enqueue(info);
        }
    }

    private bool OwnsGame(uint id)
    {
        return _SteamClient.SteamApps008.IsSubscribedApp(id);
    }

    private void AddGame(uint id, string type)
    {
        if (_Games.ContainsKey(id) == true) return;

        if (OwnsGame(id) == false) return;

        GameInfo info = new(id, type);
        info.Name = _SteamClient.SteamApps001.GetAppData(info.Id, "name");
        _Games.Add(id, info);
    }

    private void AddGames()
    {
        _Games.Clear();
        _RefreshGamesButton.Enabled = false;
        _ListWorker.RunWorkerAsync();
    }

    private void AddDefaultGames()
    {
        AddGame(480, "normal"); // Spacewar
    }

    private void OnTimer(object sender, EventArgs e)
    {
        _CallbackTimer.Enabled = false;
        _SteamClient.RunCallbacks(false);
        _CallbackTimer.Enabled = true;
    }

    private void OnActivateGame(object sender, EventArgs e)
    {
        foreach (int index in _GameListView.SelectedIndices)
        {
            if (index < 0 || index >= _FilteredGames.Count) continue;

            var info = _FilteredGames[index];
            if (info == null) continue;

            try
            {
                Process.Start(Environment.ProcessPath, info.Id.ToString(CultureInfo.InvariantCulture));
            }
            catch (Win32Exception)
            {
                MessageBox.Show(
                    this,
                    "Failed to start the game manager.",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }
        }
    }

    private void OnRefresh(object sender, EventArgs e)
    {
        _AddGameTextBox.Text = "";
        AddGames();
    }

    private void OnAddGame(object sender, EventArgs e)
    {
        uint id;

        if (uint.TryParse(_AddGameTextBox.Text, out id) == false)
        {
            MessageBox.Show(
                this,
                "Please enter a valid game ID.",
                "Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        if (OwnsGame(id) == false)
        {
            MessageBox.Show(this, "You don't own that game.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        while (_LogoQueue.TryDequeue(out var logo) == true)
            // clear the download queue because we will be showing only one app
            _LogosAttempted.Remove(logo.ImageUrl);

        _AddGameTextBox.Text = "";
        _Games.Clear();
        AddGame(id, "normal");
        _FilterGamesMenuItem.Checked = true;
        RefreshGames();
        DownloadNextLogo();
    }

    private void OnFilterUpdate(object sender, EventArgs e)
    {
        RefreshGames();

        // Compatibility with _GameListView SearchForVirtualItemEventHandler (otherwise _SearchGameTextBox loose focus on KeyUp)
        _SearchGameTextBox.Focus();
    }

    private void OnSelectAll(object sender, EventArgs e)
    {
        _GameListView.SelectAll();
        _GameListView.Select();
    }

    private async void OnUnlockAllSelected(object sender, EventArgs e)
    {
        var games = _GameListView.SelectedIndices.Cast<int>().Select(i => _FilteredGames[i]).ToList();
        if (games.Count == 0) return;

        if (MessageBox.Show(
                this,
                $"Unlock every achievement in {games.Count} game(s)?\n\nThis is visible on your Steam profile. Protected achievements are skipped.",
                "Unlock All Achievements",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        _UnlockAllButton.Enabled = false;
        var failed = new List<string>();
        try
        {
            for (var i = 0; i < games.Count; i++)
            {
                var game = games[i];
                _PickerStatusLabel.Text = $"Unlocking {i + 1}/{games.Count}: {game.Name}";
                if (await Task.Run(() => RunUnlockAll(game.Id)) == false) failed.Add(game.Name);
            }
        }
        catch (Win32Exception)
        {
            MessageBox.Show(this, "Failed to start the game manager.", "Error", MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }
        finally
        {
            _UnlockAllButton.Enabled = true;
        }

        _PickerStatusLabel.Text = $"Unlocked {games.Count - failed.Count}/{games.Count} games.";
        MessageBox.Show(
            this,
            failed.Count == 0
                ? $"Done: {games.Count} game(s) processed."
                : $"Done: {games.Count - failed.Count}/{games.Count} succeeded. Failed:\n" +
                  string.Join("\n", failed.Take(30)),
            "Unlock All Achievements",
            MessageBoxButtons.OK,
            failed.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    // one process per game: Steam binds a process to a single app ID
    private static bool RunUnlockAll(uint appId)
    {
        using var process = Process.Start(Environment.ProcessPath, _($"{appId} --unlock-all"));
        // shortcut: fixed 60s per game, a game whose stats never arrive is killed and counted as failed
        if (process.WaitForExit(60000) == false)
        {
            process.Kill();
            return false;
        }

        return process.ExitCode == 0;
    }

    private void OnGameListViewDrawSubItem(object sender, DrawListViewSubItemEventArgs e)
    {
        e.DrawDefault = true;
    }

    private void OnGameListViewResize(object sender, EventArgs e)
    {
        if (_GameListView.Columns.Count > 0) _GameListView.Columns[0].Width = _GameListView.ClientSize.Width;
    }

    private void OnGameListViewDrawItem(object sender, DrawListViewItemEventArgs e)
    {
        e.DrawDefault = true;

        if (e.Item.Bounds.IntersectsWith(_GameListView.ClientRectangle) == false) return;

        var info = _FilteredGames[e.ItemIndex];
        if (info.ImageIndex <= 0)
        {
            AddGameToLogoQueue(info);
            DownloadNextLogo();
        }
    }
}