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
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Windows.Forms;
using SAM.Game.Stats;
using static SAM.InvariantShorthand;
using APITypes = SAM.API.Types;

namespace SAM.Game;

internal partial class Manager : Form
{
    private readonly long _GameId;
    private readonly API.Client _SteamClient;

    private readonly WebClient _IconDownloader = new();

    private readonly List<Stats.AchievementInfo> _IconQueue = new();
    private readonly List<Stats.StatDefinition> _StatDefinitions = new();

    private readonly List<Stats.AchievementDefinition> _AchievementDefinitions = new();

    private readonly BindingList<Stats.StatInfo> _Statistics = new();

    private readonly API.Callbacks.UserStatsReceived _UserStatsReceivedCallback;
    private readonly API.Callbacks.UserStatsStored _UserStatsStoredCallback;

    // unlock-all mode: runs invisibly, unlocks everything it can, stores and closes
    private readonly bool _UnlockAll;
    public bool UnlockAllSucceeded { get; private set; }

    //private API.Callback<APITypes.UserStatsStored> UserStatsStoredCallback;

    public Manager(long gameId, API.Client client, bool unlockAll)
    {
        InitializeComponent();

        _MainTabControl.SelectedTab = _AchievementsTabPage;
        //this.statisticsList.Enabled = this.checkBox1.Checked;

        _AchievementImageList.Images.Add("Blank", new Bitmap(64, 64));

        _StatisticsDataGridView.AutoGenerateColumns = false;

        _StatisticsDataGridView.Columns.Add("name", "Name");
        _StatisticsDataGridView.Columns[0].ReadOnly = true;
        _StatisticsDataGridView.Columns[0].Width = 200;
        _StatisticsDataGridView.Columns[0].DataPropertyName = "DisplayName";

        _StatisticsDataGridView.Columns.Add("value", "Value");
        _StatisticsDataGridView.Columns[1].ReadOnly = _EnableStatsEditingCheckBox.Checked == false;
        _StatisticsDataGridView.Columns[1].Width = 90;
        _StatisticsDataGridView.Columns[1].DataPropertyName = "Value";

        _StatisticsDataGridView.Columns.Add("extra", "Extra");
        _StatisticsDataGridView.Columns[2].ReadOnly = true;
        _StatisticsDataGridView.Columns[2].Width = 200;
        _StatisticsDataGridView.Columns[2].DataPropertyName = "Extra";

        _StatisticsDataGridView.DataSource = new BindingSource
        {
            DataSource = _Statistics
        };

        _GameId = gameId;
        _SteamClient = client;

        _IconDownloader.DownloadDataCompleted += OnIconDownload;

        var name = _SteamClient.SteamApps001.GetAppData((uint)_GameId, "name");
        if (name != null)
            base.Text += " | " + name;
        else
            base.Text += " | " + _GameId.ToString(CultureInfo.InvariantCulture);

        _UserStatsReceivedCallback = client.CreateAndRegisterCallback<API.Callbacks.UserStatsReceived>();
        _UserStatsReceivedCallback.OnRun += OnUserStatsReceived;

        _UnlockAll = unlockAll;
        if (unlockAll == true)
        {
            Opacity = 0;
            ShowInTaskbar = false;
            _UserStatsStoredCallback = client.CreateAndRegisterCallback<API.Callbacks.UserStatsStored>();
            _UserStatsStoredCallback.OnRun += OnUserStatsStored;
        }

        RefreshStats();
    }

    private void AddAchievementIcon(Stats.AchievementInfo info, Image icon)
    {
        if (icon == null)
        {
            info.ImageIndex = 0;
        }
        else
        {
            info.ImageIndex = _AchievementImageList.Images.Count;
            _AchievementImageList.Images.Add(info.IsAchieved == true ? info.IconNormal : info.IconLocked, icon);
        }
    }

    private void OnIconDownload(object sender, DownloadDataCompletedEventArgs e)
    {
        if (e.Error == null && e.Cancelled == false)
        {
            var info = (Stats.AchievementInfo)e.UserState;

            Bitmap bitmap;
            try
            {
                using (MemoryStream stream = new())
                {
                    stream.Write(e.Result, 0, e.Result.Length);
                    bitmap = new Bitmap(stream);
                }
            }
            catch (Exception)
            {
                bitmap = null;
            }

            AddAchievementIcon(info, bitmap);
            _AchievementListView.Update();
        }

        DownloadNextIcon();
    }

    private void DownloadNextIcon()
    {
        if (_IconQueue.Count == 0)
        {
            _DownloadStatusLabel.Visible = false;
            return;
        }

        if (_IconDownloader.IsBusy == true) return;

        _DownloadStatusLabel.Text = $"Downloading {_IconQueue.Count} icons...";
        _DownloadStatusLabel.Visible = true;

        var info = _IconQueue[0];
        _IconQueue.RemoveAt(0);


        _IconDownloader.DownloadDataAsync(
            new Uri(_(
                $"https://cdn.steamstatic.com/steamcommunity/public/images/apps/{_GameId}/{(info.IsAchieved == true ? info.IconNormal : info.IconLocked)}")),
            info);
    }

    private static string TranslateError(int id)
    {
        return id switch
        {
            2 => "generic error -- this usually means you don't own the game",
            _ => _($"{id}")
        };
    }

    private static string GetLocalizedString(KeyValue kv, string language, string defaultValue)
    {
        var name = kv[language].AsString("");
        if (string.IsNullOrEmpty(name) == false) return name;

        if (language != "english")
        {
            name = kv["english"].AsString("");
            if (string.IsNullOrEmpty(name) == false) return name;
        }

        name = kv.AsString("");
        if (string.IsNullOrEmpty(name) == false) return name;

        return defaultValue;
    }

    private bool LoadUserGameStatsSchema()
    {
        string path;
        try
        {
            var fileName = _($"UserGameStatsSchema_{_GameId}.bin");
            path = API.Steam.GetInstallPath();
            path = Path.Combine(path, "appcache", "stats", fileName);
            if (File.Exists(path) == false) return false;
        }
        catch (Exception)
        {
            return false;
        }

        var kv = KeyValue.LoadAsBinary(path);
        if (kv == null) return false;

        var currentLanguage = _SteamClient.SteamApps008.GetCurrentGameLanguage();

        _AchievementDefinitions.Clear();
        _StatDefinitions.Clear();

        var stats = kv[_GameId.ToString(CultureInfo.InvariantCulture)]["stats"];
        if (stats.Valid == false || stats.Children == null) return false;

        foreach (var stat in stats.Children)
        {
            if (stat.Valid == false) continue;

            APITypes.UserStatType type;

            // schema in the new format?
            var typeNode = stat["type"];
            if (typeNode.Valid == true && typeNode.Type == KeyValueType.String)
            {
                if (Enum.TryParse((string)typeNode.Value, true, out type) == false)
                    type = APITypes.UserStatType.Invalid;
            }
            else
            {
                type = APITypes.UserStatType.Invalid;
            }

            // schema in the old format?
            if (type == APITypes.UserStatType.Invalid)
            {
                var typeIntNode = stat["type_int"];
                var rawType = typeIntNode.Valid == true
                    ? typeIntNode.AsInteger(0)
                    : typeNode.AsInteger(0);
                type = (APITypes.UserStatType)rawType;
            }

            switch (type)
            {
                case APITypes.UserStatType.Invalid:
                {
                    break;
                }

                case APITypes.UserStatType.Integer:
                {
                    var id = stat["name"].AsString("");
                    var name = GetLocalizedString(stat["display"]["name"], currentLanguage, id);

                    _StatDefinitions.Add(new Stats.IntegerStatDefinition
                    {
                        Id = stat["name"].AsString(""),
                        DisplayName = name,
                        MinValue = stat["min"].AsInteger(int.MinValue),
                        MaxValue = stat["max"].AsInteger(int.MaxValue),
                        MaxChange = stat["maxchange"].AsInteger(0),
                        IncrementOnly = stat["incrementonly"].AsBoolean(false),
                        SetByTrustedGameServer = stat["bSetByTrustedGS"].AsBoolean(false),
                        DefaultValue = stat["default"].AsInteger(0),
                        Permission = stat["permission"].AsInteger(0)
                    });
                    break;
                }

                case APITypes.UserStatType.Float:
                case APITypes.UserStatType.AverageRate:
                {
                    var id = stat["name"].AsString("");
                    var name = GetLocalizedString(stat["display"]["name"], currentLanguage, id);

                    _StatDefinitions.Add(new Stats.FloatStatDefinition
                    {
                        Id = stat["name"].AsString(""),
                        DisplayName = name,
                        MinValue = stat["min"].AsFloat(float.MinValue),
                        MaxValue = stat["max"].AsFloat(float.MaxValue),
                        MaxChange = stat["maxchange"].AsFloat(0.0f),
                        IncrementOnly = stat["incrementonly"].AsBoolean(false),
                        DefaultValue = stat["default"].AsFloat(0.0f),
                        Permission = stat["permission"].AsInteger(0)
                    });
                    break;
                }

                case APITypes.UserStatType.Achievements:
                case APITypes.UserStatType.GroupAchievements:
                {
                    if (stat.Children != null)
                        foreach (var bits in stat.Children.Where(b =>
                                     string.Compare(b.Name, "bits", StringComparison.InvariantCultureIgnoreCase) == 0))
                        {
                            if (bits.Valid == false || bits.Children == null) continue;

                            foreach (var bit in bits.Children)
                            {
                                var id = bit["name"].AsString("");
                                var name = GetLocalizedString(bit["display"]["name"], currentLanguage, id);
                                var desc = GetLocalizedString(bit["display"]["desc"], currentLanguage, "");

                                _AchievementDefinitions.Add(new AchievementDefinition
                                {
                                    Id = id,
                                    Name = name,
                                    Description = desc,
                                    IconNormal = bit["display"]["icon"].AsString(""),
                                    IconLocked = bit["display"]["icon_gray"].AsString(""),
                                    IsHidden = bit["display"]["hidden"].AsBoolean(false),
                                    Permission = bit["permission"].AsInteger(0)
                                });
                            }
                        }

                    break;
                }

                default:
                {
                    throw new InvalidOperationException("invalid stat type");
                }
            }
        }

        return true;
    }

    private void OnUserStatsReceived(APITypes.UserStatsReceived param)
    {
        var loaded = HandleUserStatsReceived(param);
        if (_UnlockAll == false) return;

        if (loaded == false)
        {
            Close();
            return;
        }

        foreach (ListViewItem item in _AchievementListView.Items)
            // protected achievements cannot be set; skip them instead of prompting
            if (item.Tag is Stats.AchievementInfo info && (info.Permission & 3) == 0)
                item.Checked = true;

        var count = StoreAchievements();
        if (count == 0)
        {
            UnlockAllSucceeded = true;
            Close();
            return;
        }

        if (count < 0 || Store() == false) Close();

        // otherwise close in OnUserStatsStored, so exiting does not cut off the store
    }

    private void OnUserStatsStored(APITypes.UserStatsStored param)
    {
        UnlockAllSucceeded = param.Result == 1;
        Close();
    }

    private bool HandleUserStatsReceived(APITypes.UserStatsReceived param)
    {
        if (param.Result != 1)
        {
            _GameStatusLabel.Text = $"Error while retrieving stats: {TranslateError(param.Result)}";
            EnableInput();
            return false;
        }

        if (LoadUserGameStatsSchema() == false)
        {
            _GameStatusLabel.Text = "Failed to load schema.";
            EnableInput();
            return false;
        }

        try
        {
            GetAchievements();
        }
        catch (Exception e)
        {
            _GameStatusLabel.Text = "Error when handling achievements retrieval.";
            EnableInput();
            MessageBox.Show(
                "Error when handling achievements retrieval:\n" + e,
                "Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return false;
        }

        try
        {
            GetStatistics();
        }
        catch (Exception e)
        {
            _GameStatusLabel.Text = "Error when handling stats retrieval.";
            EnableInput();
            MessageBox.Show(
                "Error when handling stats retrieval:\n" + e,
                "Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return false;
        }

        _GameStatusLabel.Text =
            $"Retrieved {_AchievementListView.Items.Count} achievements and {_StatisticsDataGridView.Rows.Count} statistics.";
        EnableInput();
        return true;
    }

    private void RefreshStats()
    {
        _AchievementListView.Items.Clear();
        _StatisticsDataGridView.Rows.Clear();

        var steamId = _SteamClient.SteamUser.GetSteamId();

        // This still triggers the UserStatsReceived callback, in addition to the callresult.
        // No need to implement callresults for the time being.
        var callHandle = _SteamClient.SteamUserStats.RequestUserStats(steamId);
        if (callHandle == API.CallHandle.Invalid)
        {
            MessageBox.Show(this, "Failed.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        _GameStatusLabel.Text = "Retrieving stat information...";
        DisableInput();
    }

    private bool _IsUpdatingAchievementList;

    private void GetAchievements()
    {
        var textSearch = _MatchingStringTextBox.Text.Length > 0
            ? _MatchingStringTextBox.Text
            : null;

        _IsUpdatingAchievementList = true;

        _AchievementListView.Items.Clear();
        _AchievementListView.BeginUpdate();
        //this.Achievements.Clear();

        var wantLocked = _DisplayLockedOnlyButton.Checked == true;
        var wantUnlocked = _DisplayUnlockedOnlyButton.Checked == true;

        foreach (var def in _AchievementDefinitions)
        {
            if (string.IsNullOrEmpty(def.Id) == true) continue;

            if (_SteamClient.SteamUserStats.GetAchievementAndUnlockTime(
                    def.Id,
                    out var isAchieved,
                    out var unlockTime) == false)
                continue;

            var wanted = (wantLocked == false && wantUnlocked == false) || isAchieved switch
            {
                true => wantUnlocked,
                false => wantLocked
            };
            if (wanted == false) continue;

            if (textSearch != null)
                if (def.Name.IndexOf(textSearch, StringComparison.OrdinalIgnoreCase) < 0 &&
                    def.Description.IndexOf(textSearch, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

            Stats.AchievementInfo info = new()
            {
                Id = def.Id,
                IsAchieved = isAchieved,
                UnlockTime = isAchieved == true && unlockTime > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(unlockTime).LocalDateTime
                    : null,
                IconNormal = string.IsNullOrEmpty(def.IconNormal) ? null : def.IconNormal,
                IconLocked = string.IsNullOrEmpty(def.IconLocked) ? def.IconNormal : def.IconLocked,
                Permission = def.Permission,
                Name = def.Name,
                Description = def.Description
            };

            ListViewItem item = new()
            {
                Checked = isAchieved,
                Tag = info,
                Text = info.Name,
                BackColor = (def.Permission & 3) == 0 ? Color.Black : Color.FromArgb(64, 0, 0)
            };

            info.Item = item;

            if (item.Text.StartsWith("#", StringComparison.InvariantCulture) == true)
            {
                item.Text = info.Id;
                item.SubItems.Add("");
            }
            else
            {
                item.SubItems.Add(info.Description);
            }

            item.SubItems.Add(info.UnlockTime.HasValue == true
                ? info.UnlockTime.Value.ToString()
                : "");

            info.ImageIndex = 0;

            AddAchievementToIconQueue(info, false);
            _AchievementListView.Items.Add(item);
        }

        _AchievementListView.EndUpdate();
        _IsUpdatingAchievementList = false;

        DownloadNextIcon();
    }

    private void GetStatistics()
    {
        _Statistics.Clear();
        foreach (var stat in _StatDefinitions)
        {
            if (string.IsNullOrEmpty(stat.Id) == true) continue;

            if (stat is Stats.IntegerStatDefinition intStat)
            {
                if (_SteamClient.SteamUserStats.GetStatValue(intStat.Id, out int value) == false) continue;
                _Statistics.Add(new Stats.IntStatInfo
                {
                    Id = intStat.Id,
                    DisplayName = intStat.DisplayName,
                    IntValue = value,
                    OriginalValue = value,
                    IsIncrementOnly = intStat.IncrementOnly,
                    Permission = intStat.Permission
                });
            }
            else if (stat is Stats.FloatStatDefinition floatStat)
            {
                if (_SteamClient.SteamUserStats.GetStatValue(floatStat.Id, out float value) == false) continue;
                _Statistics.Add(new Stats.FloatStatInfo
                {
                    Id = floatStat.Id,
                    DisplayName = floatStat.DisplayName,
                    FloatValue = value,
                    OriginalValue = value,
                    IsIncrementOnly = floatStat.IncrementOnly,
                    Permission = floatStat.Permission
                });
            }
        }
    }

    private void AddAchievementToIconQueue(Stats.AchievementInfo info, bool startDownload)
    {
        var imageIndex = _AchievementImageList.Images.IndexOfKey(
            info.IsAchieved == true ? info.IconNormal : info.IconLocked);

        if (imageIndex >= 0)
        {
            info.ImageIndex = imageIndex;
        }
        else
        {
            _IconQueue.Add(info);

            if (startDownload == true) DownloadNextIcon();
        }
    }

    private int StoreAchievements()
    {
        if (_AchievementListView.Items.Count == 0) return 0;

        List<Stats.AchievementInfo> achievements = new();
        foreach (ListViewItem item in _AchievementListView.Items)
        {
            if (item.Tag is not Stats.AchievementInfo achievementInfo ||
                achievementInfo.IsAchieved == item.Checked)
                continue;

            achievementInfo.IsAchieved = item.Checked;
            achievements.Add(achievementInfo);
        }

        if (achievements.Count == 0) return 0;

        foreach (var info in achievements)
            if (_SteamClient.SteamUserStats.SetAchievement(info.Id, info.IsAchieved) == false)
            {
                MessageBox.Show(
                    this,
                    $"An error occurred while setting the state for {info.Id}, aborting store.",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return -1;
            }

        return achievements.Count;
    }

    private int StoreStatistics()
    {
        if (_Statistics.Count == 0) return 0;

        var statistics = _Statistics.Where(stat => stat.IsModified == true).ToList();
        if (statistics.Count == 0) return 0;

        foreach (var stat in statistics)
            if (stat is Stats.IntStatInfo intStat)
            {
                if (_SteamClient.SteamUserStats.SetStatValue(
                        intStat.Id,
                        intStat.IntValue) == false)
                {
                    MessageBox.Show(
                        this,
                        $"An error occurred while setting the value for {stat.Id}, aborting store.",
                        "Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return -1;
                }
            }
            else if (stat is Stats.FloatStatInfo floatStat)
            {
                if (_SteamClient.SteamUserStats.SetStatValue(
                        floatStat.Id,
                        floatStat.FloatValue) == false)
                {
                    MessageBox.Show(
                        this,
                        $"An error occurred while setting the value for {stat.Id}, aborting store.",
                        "Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return -1;
                }
            }
            else
            {
                throw new InvalidOperationException("unsupported stat type");
            }

        return statistics.Count;
    }

    private void DisableInput()
    {
        _ReloadButton.Enabled = false;
        _StoreButton.Enabled = false;
    }

    private void EnableInput()
    {
        _ReloadButton.Enabled = true;
        _StoreButton.Enabled = true;
    }

    private void OnTimer(object sender, EventArgs e)
    {
        _CallbackTimer.Enabled = false;
        _SteamClient.RunCallbacks(false);
        _CallbackTimer.Enabled = true;
    }

    private void OnRefresh(object sender, EventArgs e)
    {
        RefreshStats();
    }

    private void OnLockAll(object sender, EventArgs e)
    {
        foreach (ListViewItem item in _AchievementListView.Items) item.Checked = false;
    }

    private void OnInvertAll(object sender, EventArgs e)
    {
        foreach (ListViewItem item in _AchievementListView.Items) item.Checked = !item.Checked;
    }

    private void OnUnlockAll(object sender, EventArgs e)
    {
        foreach (ListViewItem item in _AchievementListView.Items) item.Checked = true;
    }

    private bool Store()
    {
        if (_SteamClient.SteamUserStats.StoreStats() == false)
        {
            MessageBox.Show(
                this,
                "An error occurred while storing, aborting.",
                "Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return false;
        }

        return true;
    }

    private void OnStore(object sender, EventArgs e)
    {
        var achievements = StoreAchievements();
        if (achievements < 0)
        {
            RefreshStats();
            return;
        }

        var stats = StoreStatistics();
        if (stats < 0)
        {
            RefreshStats();
            return;
        }

        if (Store() == false)
        {
            RefreshStats();
            return;
        }

        MessageBox.Show(
            this,
            $"Stored {achievements} achievements and {stats} statistics.",
            "Information",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
        RefreshStats();
    }

    private void OnStatDataError(object sender, DataGridViewDataErrorEventArgs e)
    {
        if (e.Context != DataGridViewDataErrorContexts.Commit) return;

        var view = (DataGridView)sender;
        if (e.Exception is Stats.StatIsProtectedException)
        {
            e.ThrowException = false;
            e.Cancel = true;
            view.Rows[e.RowIndex].ErrorText = "Stat is protected! -- you can't modify it";
        }
        else
        {
            e.ThrowException = false;
            e.Cancel = true;
            view.Rows[e.RowIndex].ErrorText = "Invalid value";
        }
    }

    private void OnStatAgreementChecked(object sender, EventArgs e)
    {
        _StatisticsDataGridView.Columns[1].ReadOnly = _EnableStatsEditingCheckBox.Checked == false;
    }

    private void OnStatCellEndEdit(object sender, DataGridViewCellEventArgs e)
    {
        var view = (DataGridView)sender;
        view.Rows[e.RowIndex].ErrorText = "";
    }

    private void OnResetAllStats(object sender, EventArgs e)
    {
        if (MessageBox.Show(
                "Are you absolutely sure you want to reset stats?",
                "Warning",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) == DialogResult.No)
            return;

        var achievementsToo = DialogResult.Yes == MessageBox.Show(
            "Do you want to reset achievements too?",
            "Question",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (MessageBox.Show(
                "Really really sure?",
                "Warning",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Error) == DialogResult.No)
            return;

        if (_SteamClient.SteamUserStats.ResetAllStats(achievementsToo) == false)
        {
            MessageBox.Show(this, "Failed.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        RefreshStats();
    }

    private void OnCheckAchievement(object sender, ItemCheckEventArgs e)
    {
        if (sender != _AchievementListView) return;

        if (_IsUpdatingAchievementList == true) return;

        if (_AchievementListView.Items[e.Index].Tag is not Stats.AchievementInfo info) return;

        if ((info.Permission & 3) != 0)
        {
            MessageBox.Show(
                this,
                "Sorry, but this is a protected achievement and cannot be managed with Steam Achievement Manager.",
                "Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            e.NewValue = e.CurrentValue;
        }
    }

    private void OnDisplayUncheckedOnly(object sender, EventArgs e)
    {
        if ((sender as ToolStripButton).Checked == true) _DisplayLockedOnlyButton.Checked = false;

        GetAchievements();
    }

    private void OnDisplayCheckedOnly(object sender, EventArgs e)
    {
        if ((sender as ToolStripButton).Checked == true) _DisplayUnlockedOnlyButton.Checked = false;

        GetAchievements();
    }

    private void OnFilterUpdate(object sender, KeyEventArgs e)
    {
        GetAchievements();
    }
}