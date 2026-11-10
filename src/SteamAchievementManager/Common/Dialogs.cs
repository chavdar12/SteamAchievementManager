namespace SteamAchievementManager.Common;

internal static class Dialogs
{
    public static void Error(IWin32Window owner, string text) =>
        MessageBox.Show(owner, text, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);

    public static void Info(IWin32Window owner, string text, string caption = "Information") =>
        MessageBox.Show(owner, text, caption, MessageBoxButtons.OK, MessageBoxIcon.Information);

    public static void Warning(IWin32Window owner, string text, string caption) =>
        MessageBox.Show(owner, text, caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);

    public static bool Confirm(IWin32Window owner, string text, string caption,
        MessageBoxIcon icon = MessageBoxIcon.Warning) =>
        MessageBox.Show(owner, text, caption, MessageBoxButtons.YesNo, icon) == DialogResult.Yes;
}
