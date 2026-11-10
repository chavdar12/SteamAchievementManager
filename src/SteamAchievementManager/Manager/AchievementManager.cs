using System.ComponentModel;
using System.Globalization;
using SteamAchievementManager.Common;
using SteamAchievementManager.Schema;
using SteamAchievementManager.Steam;

namespace SteamAchievementManager.Manager;

// views and edits one game's achievements and stats
internal sealed partial class AchievementManager : Form
{
    private readonly uint _gameId;
    private readonly SteamClient _steam;

    // unlock-all mode: runs invisibly, unlocks everything it can, stores and closes
    private readonly bool _unlockAll;

    private readonly BindingList<StatInfo> _statistics = [];
    private readonly Queue<ListViewItem> _iconQueue = new();
    private GameSchema _schema;
    private bool _isDownloadingIcons;
    private bool _isUpdatingAchievementList;

    public AchievementManager(uint gameId, SteamClient steam, bool unlockAll)
    {
        InitializeComponent();

        _AchievementImageList.Images.Add("Blank", new Bitmap(64, 64));
        _StatisticsDataGridView.AutoGenerateColumns = false;
        _StatisticsDataGridView.DataSource = new BindingSource { DataSource = _statistics };

        _gameId = gameId;
        _steam = steam;
        _unlockAll = unlockAll;

        Text += " | " + (steam.Apps.GetAppData(gameId, "name") ?? gameId.ToString(CultureInfo.InvariantCulture));

        steam.OnCallback<UserStatsReceived>(OnUserStatsReceived);
        if (unlockAll)
        {
            Opacity = 0;
            ShowInTaskbar = false;
            steam.OnCallback<UserStatsStored>(OnUserStatsStored);
        }

        RefreshStats();
    }

    public bool UnlockAllSucceeded { get; private set; }

    private void RefreshStats()
    {
        _AchievementListView.Items.Clear();
        _statistics.Clear();

        // the result arrives as the UserStatsReceived callback
        if (_steam.UserStats.RequestUserStats(_steam.SteamId) == 0)
        {
            Dialogs.Error(this, "Failed.");
            return;
        }

        _GameStatusLabel.Text = "Retrieving stat information...";
        EnableInput(false);
    }

    private void OnUserStatsReceived(UserStatsReceived param)
    {
        var loaded = LoadStats(param.Result);
        if (_unlockAll == false) return;

        if (loaded == false)
        {
            Close();
            return;
        }

        // protected achievements cannot be set; skip them instead of prompting
        foreach (ListViewItem item in _AchievementListView.Items)
            if (item.Tag is AchievementInfo { Definition.IsProtected: false })
                item.Checked = true;

        var count = StoreAchievements();
        if (count == 0)
        {
            UnlockAllSucceeded = true;
            Close();
        }
        else if (count < 0 || StoreStats() == false)
        {
            Close();
        }

        // otherwise close in OnUserStatsStored, so exiting does not cut off the store
    }

    private void OnUserStatsStored(UserStatsStored param)
    {
        UnlockAllSucceeded = param.Result == 1;
        Close();
    }

    private bool LoadStats(int result)
    {
        EnableInput(true);

        if (result != 1)
        {
            var reason = result == 2 ? "generic error -- this usually means you don't own the game" : $"{result}";
            _GameStatusLabel.Text = $"Error while retrieving stats: {reason}";
            return false;
        }

        _schema = GameSchema.Load(_gameId, _steam.Apps.GetCurrentGameLanguage());
        if (_schema == null)
        {
            _GameStatusLabel.Text = "Failed to load schema.";
            return false;
        }

        try
        {
            ShowAchievements();
            ShowStatistics();
        }
        catch (Exception e)
        {
            _GameStatusLabel.Text = "Error when handling stats retrieval.";
            Dialogs.Error(this, "Error when handling stats retrieval:\n" + e);
            return false;
        }

        _GameStatusLabel.Text =
            $"Retrieved {_AchievementListView.Items.Count} achievements and {_statistics.Count} statistics.";
        return true;
    }

    private void ShowAchievements()
    {
        var search = _MatchingStringTextBox.Text;
        var wantLocked = _DisplayLockedOnlyButton.Checked;
        var wantUnlocked = _DisplayUnlockedOnlyButton.Checked;

        _isUpdatingAchievementList = true;
        _iconQueue.Clear();
        _AchievementListView.BeginUpdate();
        _AchievementListView.Items.Clear();

        foreach (var def in _schema?.Achievements ?? [])
        {
            if (string.IsNullOrEmpty(def.Id) ||
                _steam.UserStats.GetAchievement(def.Id, out var isAchieved, out var unlockTime) == false)
                continue;

            if ((wantLocked || wantUnlocked) && (isAchieved ? wantUnlocked : wantLocked) == false) continue;

            if (def.Name.Contains(search, StringComparison.OrdinalIgnoreCase) == false &&
                def.Description.Contains(search, StringComparison.OrdinalIgnoreCase) == false)
                continue;

            AchievementInfo info = new(def, isAchieved,
                isAchieved && unlockTime > 0 ? DateTimeOffset.FromUnixTimeSeconds(unlockTime).LocalDateTime : null);

            // names starting with '#' are untranslated tokens; show the ID instead
            var hasName = def.Name.StartsWith('#') == false;
            ListViewItem item = new(hasName ? def.Name : def.Id)
            {
                Checked = isAchieved,
                Tag = info,
                ImageIndex = 0,
                ForeColor = def.IsProtected ? SystemColors.GrayText : SystemColors.WindowText
            };
            item.SubItems.Add(hasName ? def.Description : "");
            item.SubItems.Add(info.UnlockTime?.ToString() ?? "");
            _AchievementListView.Items.Add(item);

            if (_unlockAll == false) QueueIcon(item);
        }

        _AchievementListView.EndUpdate();
        _isUpdatingAchievementList = false;

        DownloadIcons();
    }

    private void ShowStatistics()
    {
        _statistics.Clear();
        foreach (var def in _schema.Stats)
            if (string.IsNullOrEmpty(def.Id) == false &&
                _steam.UserStats.GetStat(def.Id, def.IsFloat, out var value))
                _statistics.Add(new StatInfo(def, value));
    }

    // uses the cached icon when it was already downloaded; returns false if it needs downloading
    private bool TrySetCachedIcon(ListViewItem item)
    {
        var index = _AchievementImageList.Images.IndexOfKey(((AchievementInfo)item.Tag).Icon);
        if (index < 0) return false;

        item.ImageIndex = index;
        return true;
    }

    private void QueueIcon(ListViewItem item)
    {
        if (string.IsNullOrEmpty(((AchievementInfo)item.Tag).Icon) == false && TrySetCachedIcon(item) == false)
            _iconQueue.Enqueue(item);
    }

    // downloads queued icons one at a time
    private async void DownloadIcons()
    {
        if (_isDownloadingIcons) return;

        _isDownloadingIcons = true;
        while (_iconQueue.TryDequeue(out var item))
        {
            // another achievement may have downloaded the same icon meanwhile
            if (TrySetCachedIcon(item)) continue;

            _DownloadStatusLabel.Text = $"Downloading {1 + _iconQueue.Count} icons...";
            _DownloadStatusLabel.Visible = true;

            var icon = ((AchievementInfo)item.Tag).Icon;
            var bitmap = await Http.TryGetImageAsync(
                $"https://cdn.steamstatic.com/steamcommunity/public/images/apps/{_gameId}/{icon}");
            if (IsDisposed) return;
            if (bitmap == null) continue;

            item.ImageIndex = _AchievementImageList.Images.Count;
            _AchievementImageList.Images.Add(icon, bitmap);
        }

        _DownloadStatusLabel.Visible = false;
        _isDownloadingIcons = false;
    }

    // returns the number of changed achievements, or -1 on failure
    private int StoreAchievements()
    {
        var changed = _AchievementListView.Items.Cast<ListViewItem>()
            .Where(item => item.Tag is AchievementInfo info && info.IsAchieved != item.Checked).ToList();

        foreach (var item in changed)
        {
            var info = (AchievementInfo)item.Tag;
            if (_steam.UserStats.SetAchievement(info.Definition.Id, item.Checked) == false)
            {
                Dialogs.Error(this, $"An error occurred while setting the state for {info.Definition.Id}, aborting store.");
                return -1;
            }

            info.IsAchieved = item.Checked;
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
                Dialogs.Error(this, $"An error occurred while setting the value for {stat.Definition.Id}, aborting store.");
                return -1;
            }

        return changed.Count;
    }

    // commits the set achievements and stats to Steam
    private bool StoreStats()
    {
        if (_steam.UserStats.StoreStats()) return true;

        Dialogs.Error(this, "An error occurred while storing, aborting.");
        return false;
    }

    private void EnableInput(bool enabled)
    {
        _ReloadButton.Enabled = enabled;
        _StoreButton.Enabled = enabled;
    }

    private void OnStore(object sender, EventArgs e)
    {
        var achievements = StoreAchievements();
        var stats = achievements < 0 ? -1 : StoreStatistics();
        if (stats >= 0 && StoreStats())
            Dialogs.Info(this, $"Stored {achievements} achievements and {stats} statistics.");

        RefreshStats();
    }

    private void OnRefresh(object sender, EventArgs e)
    {
        RefreshStats();
    }

    private void OnResetAllStats(object sender, EventArgs e)
    {
        if (Dialogs.Confirm(this, "Are you absolutely sure you want to reset stats?", "Warning") == false) return;

        var achievementsToo = Dialogs.Confirm(this, "Do you want to reset achievements too?", "Question",
            MessageBoxIcon.Question);

        if (Dialogs.Confirm(this, "Really really sure?", "Warning", MessageBoxIcon.Error) == false) return;

        if (_steam.UserStats.ResetAllStats(achievementsToo) == false)
        {
            Dialogs.Error(this, "Failed.");
            return;
        }

        RefreshStats();
    }

    private void OnLockAll(object sender, EventArgs e) => SetAllChecked(_ => false);

    private void OnInvertAll(object sender, EventArgs e) => SetAllChecked(isChecked => !isChecked);

    private void OnUnlockAll(object sender, EventArgs e) => SetAllChecked(_ => true);

    private void SetAllChecked(Func<bool, bool> change)
    {
        foreach (ListViewItem item in _AchievementListView.Items) item.Checked = change(item.Checked);
    }

    private void OnCheckAchievement(object sender, ItemCheckEventArgs e)
    {
        if (_isUpdatingAchievementList ||
            _AchievementListView.Items[e.Index].Tag is not AchievementInfo { Definition.IsProtected: true })
            return;

        Dialogs.Error(this,
            "Sorry, but this is a protected achievement and cannot be managed with Steam Achievement Manager.");
        e.NewValue = e.CurrentValue;
    }

    // "locked" and "unlocked" are exclusive
    private void OnDisplayFilterChanged(object sender, EventArgs e)
    {
        var other = sender == _DisplayLockedOnlyButton ? _DisplayUnlockedOnlyButton : _DisplayLockedOnlyButton;
        if (((ToolStripButton)sender).Checked) other.Checked = false;

        ShowAchievements();
    }

    private void OnSearchChanged(object sender, EventArgs e)
    {
        ShowAchievements();
    }

    private void OnStatDataError(object sender, DataGridViewDataErrorEventArgs e)
    {
        if (e.Context != DataGridViewDataErrorContexts.Commit) return;

        e.ThrowException = false;
        e.Cancel = true;
        _StatisticsDataGridView.Rows[e.RowIndex].ErrorText = e.Exception is StatIsProtectedException
            ? "Stat is protected! -- you can't modify it"
            : "Invalid value";
    }

    private void OnStatCellEndEdit(object sender, DataGridViewCellEventArgs e)
    {
        _StatisticsDataGridView.Rows[e.RowIndex].ErrorText = "";
    }

    private void OnStatAgreementChecked(object sender, EventArgs e)
    {
        _StatValueColumn.ReadOnly = _EnableStatsEditingCheckBox.Checked == false;
    }
}
