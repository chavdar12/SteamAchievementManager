using System.Runtime.InteropServices;

namespace SteamAchievementManager.Common;

[StructLayout(LayoutKind.Sequential)]
internal struct LvItem
{
    public uint mask;
    public int iItem;
    public int iSubItem;
    public uint state;
    public uint stateMask;
    public IntPtr pszText;
    public int cchTextMax;
    public int iImage;
    public IntPtr lParam;
}

// a flicker-free ListView with the native Explorer look
internal sealed class BufferedListView : ListView
{
    private const int LvmSetItemState = 0x102B;
    private const uint LvisSelected = 2;

    public BufferedListView()
    {
        DoubleBuffered = true;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
#pragma warning disable WFO5001
        SetWindowTheme(Handle, Application.IsDarkModeEnabled ? "DarkMode_Explorer" : "Explorer", null);
#pragma warning restore WFO5001
    }

    public void SelectAll()
    {
        // index -1 applies the state to every item, which is fast in virtual mode
        LvItem item = new() { stateMask = LvisSelected, state = LvisSelected };
        SendMessage(Handle, LvmSetItemState, new IntPtr(-1), ref item);
    }

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string subAppName, string subIdList);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref LvItem lParam);
}
