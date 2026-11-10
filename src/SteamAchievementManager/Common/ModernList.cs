using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace SteamAchievementManager.Common;

internal sealed class RowPaintEventArgs(Graphics graphics, Rectangle bounds, int index, bool isSelected) : EventArgs
{
    public Graphics Graphics { get; } = graphics;

    // the row's content area, right of the checkbox
    public Rectangle Bounds { get; } = bounds;
    public int Index { get; } = index;
    public bool IsSelected { get; } = isSelected;
}

// a virtual, owner-drawn list of card rows with checkboxes; the owner keeps the items and their
// checked state, paints the row content (PaintRow) and flips checks on request (ToggleRequested)
internal sealed class ModernList : ListView
{
    private const int WmPaint = 0x000F;
    private const int WmLButtonDown = 0x0201;
    private const int WmLButtonDblClk = 0x0203;
    private const int WmMouseWheel = 0x020A;
    private const int WmVScroll = 0x0115;

    private int _hotIndex = -1;
    private string _emptyText;

    public ModernList()
    {
        DoubleBuffered = true;
        OwnerDraw = true;
        VirtualMode = true;
        View = View.Details;
        HeaderStyle = ColumnHeaderStyle.None;
        FullRowSelect = true;
        HideSelection = false;
        MultiSelect = true;
        BorderStyle = BorderStyle.None;
        ShowItemToolTips = true;
        UseCompatibleStateImageBehavior = false;
        Columns.Add("", 100);
    }

    public event EventHandler<RowPaintEventArgs> PaintRow;

    // the indices whose checkboxes were clicked, or the selection when Space was pressed
    public event Action<IReadOnlyList<int>> ToggleRequested;

    public Func<int, bool> IsChecked { get; set; }

    // false draws the checkbox disabled
    public Func<int, bool> CanCheck { get; set; }

    // shown centered when the list is empty
    public string EmptyText
    {
        get => _emptyText;
        set
        {
            _emptyText = value;
            Invalidate();
        }
    }

    public int RowHeight
    {
        set
        {
            // a ListView's row height follows its small image size
            var old = SmallImageList;
            SmallImageList = new ImageList { ImageSize = new Size(1, Math.Min(256, value)) };
            old?.Dispose();
        }
    }

    public int Count => VirtualListSize;

    public int HotIndex => _hotIndex;

    // a left click inside a row's content; return true to swallow it (for buttons painted in the row)
    public Func<int, Point, bool> RowClick { get; set; }

    // the area passed to PaintRow as RowPaintEventArgs.Bounds
    public Rectangle ContentBounds(int index)
    {
        return ContentBounds(GetItemRect(index));
    }

    private Rectangle ContentBounds(Rectangle row)
    {
        return Rectangle.FromLTRB(
            CheckBoxBounds(row).Right + LogicalToDeviceUnits(14), CardBounds(row).Top,
            CardBounds(row).Right - LogicalToDeviceUnits(14), CardBounds(row).Bottom);
    }

    public void SetCount(int count)
    {
        SelectedIndices.Clear();
        _hotIndex = -1;
        VirtualListSize = count;
        FitColumn();
        Invalidate();
    }

    public void InvalidateRow(int index)
    {
        if (index >= 0 && index < Count && IsHandleCreated) Invalidate(GetItemRect(index));
    }

    public void SelectAllRows()
    {
        // index -1 applies the state to every item, which is fast in virtual mode
        LvItem item = new() { stateMask = 2, state = 2 };
        SendMessage(Handle, 0x102B, new IntPtr(-1), ref item);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        SetWindowTheme(Handle, Theme.IsDark ? "DarkMode_Explorer" : "Explorer", null);
        FitColumn();
    }

    protected override void OnClientSizeChanged(EventArgs e)
    {
        base.OnClientSizeChanged(e);
        FitColumn();
    }

    private void FitColumn()
    {
        if (Columns.Count > 0 && Columns[0].Width != ClientSize.Width) Columns[0].Width = ClientSize.Width;
    }

    private Rectangle CardBounds(Rectangle row)
    {
        return Rectangle.FromLTRB(
            row.Left + LogicalToDeviceUnits(12), row.Top + LogicalToDeviceUnits(2),
            ClientSize.Width - LogicalToDeviceUnits(12), row.Bottom - LogicalToDeviceUnits(2));
    }

    private Rectangle CheckBoxBounds(Rectangle row)
    {
        var size = LogicalToDeviceUnits(20);
        return new Rectangle(CardBounds(row).Left + LogicalToDeviceUnits(14), row.Top + (row.Height - size) / 2, size,
            size);
    }

    protected override void OnDrawItem(DrawListViewItemEventArgs e)
    {
        // painted in OnDrawSubItem instead: Windows draws its own selection/hover highlight after this
        // stage, which would cover the row with a translucent overlay
    }

    protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e)
    {
        if (e.ColumnIndex == 0) PaintRowAt(e.Graphics, e.Bounds, e.ItemIndex, e.Item.Selected);
    }

    private void PaintRowAt(Graphics g, Rectangle bounds, int index, bool isSelected)
    {
        var row = bounds with { X = 0, Width = ClientSize.Width };
        using (SolidBrush back = new(Theme.Background))
        {
            g.FillRectangle(back, row);
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;
        var isHot = index == _hotIndex;

        var card = CardBounds(row);
        RectangleF shape = new(card.X + 0.5f, card.Y + 0.5f, card.Width - 1, card.Height - 1);
        var radius = LogicalToDeviceUnits(6);
        Theme.FillRounded(g, isSelected || isHot ? Theme.SurfaceHover : Theme.Surface, shape, radius);
        Theme.DrawRounded(g, Theme.Border, shape, radius);

        if (isSelected)
        {
            // a small accent bar marks the keyboard selection; ticking is done with the checkboxes
            var pillHeight = card.Height * 0.4f;
            Theme.FillRounded(g, Theme.Accent,
                new RectangleF(card.X + LogicalToDeviceUnits(3), card.Y + (card.Height - pillHeight) / 2,
                    LogicalToDeviceUnits(3), pillHeight), LogicalToDeviceUnits(2));
        }

        var box = CheckBoxBounds(row);
        DrawCheckBox(g, box, IsChecked?.Invoke(index) == true, CanCheck?.Invoke(index) != false);

        PaintRow?.Invoke(this, new RowPaintEventArgs(g, ContentBounds(row), index, isSelected));
    }

    private void DrawCheckBox(Graphics g, Rectangle box, bool isChecked, bool isEnabled)
    {
        RectangleF shape = new(box.X + 0.5f, box.Y + 0.5f, box.Width - 1, box.Height - 1);
        var radius = LogicalToDeviceUnits(4);
        if (isChecked)
        {
            Theme.FillRounded(g, isEnabled ? Theme.Accent : Theme.DisabledText, shape, radius);
            Glyphs.Draw(g, Glyphs.CheckMark, box, Theme.OnAccent, LogicalToDeviceUnits(12));
        }
        else
        {
            Theme.FillRounded(g, Theme.Surface, shape, radius);
            Theme.DrawRounded(g, isEnabled ? Theme.SubtleText : Theme.DisabledText, shape, radius);
        }
    }


    protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
    {
        e.DrawDefault = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        SetHot(Count > 0 ? GetItemAt(e.X, e.Y)?.Index ?? -1 : -1);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        SetHot(-1);
    }

    private void SetHot(int index)
    {
        if (index == _hotIndex) return;

        InvalidateRow(_hotIndex);
        _hotIndex = index;
        InvalidateRow(index);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space && e.Modifiers == Keys.None && SelectedIndices.Count > 0)
        {
            ToggleRequested?.Invoke(SelectedIndices.Cast<int>().ToArray());
            Invalidate();
            e.Handled = e.SuppressKeyPress = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    // the row whose checkbox (or the margin around it) is at the point, or -1
    private int CheckBoxHit(Point point)
    {
        if (Count == 0) return -1;

        var item = GetItemAt(point.X, point.Y);
        if (item == null) return -1;

        var box = CheckBoxBounds(GetItemRect(item.Index));
        return point.X >= box.Left - LogicalToDeviceUnits(10) && point.X <= box.Right + LogicalToDeviceUnits(8)
            ? item.Index
            : -1;
    }

    protected override void WndProc(ref Message m)
    {
        // a click on a checkbox toggles it without changing the selection or opening the row
        if (m.Msg is WmLButtonDown or WmLButtonDblClk)
        {
            var lParam = m.LParam.ToInt64();
            Point point = new((short)(lParam & 0xFFFF), (short)((lParam >> 16) & 0xFFFF));
            var index = CheckBoxHit(point);
            if (index >= 0)
            {
                Focus();
                ToggleRequested?.Invoke([index]);
                InvalidateRow(index);
                return;
            }

            var row = Count > 0 ? GetItemAt(point.X, point.Y)?.Index ?? -1 : -1;
            if (m.Msg == WmLButtonDown && row >= 0 && RowClick?.Invoke(row, point) == true) return;
        }

        base.WndProc(ref m);

        // scrolling moves rows under a still mouse, so the hovered row has to be recomputed
        if (m.Msg is WmMouseWheel or WmVScroll)
        {
            var mouse = PointToClient(Cursor.Position);
            SetHot(Count > 0 && ClientRectangle.Contains(mouse) ? GetItemAt(mouse.X, mouse.Y)?.Index ?? -1 : -1);
        }

        if (m.Msg == WmPaint && Count == 0 && string.IsNullOrEmpty(_emptyText) == false)
        {
            using var g = CreateGraphics();
            TextRenderer.DrawText(g, _emptyText, Font, ClientRectangle, Theme.SubtleText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LvItem
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

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string subAppName, string subIdList);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref LvItem lParam);
}