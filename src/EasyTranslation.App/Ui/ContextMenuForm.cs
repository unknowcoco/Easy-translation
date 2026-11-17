using System.Drawing;
using System.Drawing.Drawing2D;
using EasyTranslation.Interop;

namespace EasyTranslation.Ui;

public sealed record MenuEntry(string Label, bool Enabled, Action? Action)
{
    public static MenuEntry Separator() => new(string.Empty, false, null);

    public bool IsSeparator => Action is null && Label.Length == 0;
}

/// <summary>
/// Opt-in "take over the context menu" surface: a self drawn menu shown instead of the native one.
/// Clicks are routed through the mouse hook (the form never activates).
/// </summary>
public sealed class ContextMenuForm : Form
{
    private readonly List<MenuEntry> _entries = new();
    private int _rowHeight;
    private int _padding;
    private int _hoverIndex = -1;

    public ContextMenuForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Theme.Panel;
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        Visible = false;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW | 0x00000008;
            return cp;
        }
    }

    public int HitTest(Point screenPoint)
    {
        if (!Visible || !Bounds.Contains(screenPoint)) return -1;
        return RowAt(screenPoint.Y - Top);
    }

    public void ShowMenu(Point cursor, IReadOnlyList<MenuEntry> entries)
    {
        _entries.Clear();
        _entries.AddRange(entries);

        var scale = DeviceDpi / 96f;
        _rowHeight = (int)(30 * scale);
        _padding = (int)(6 * scale);
        using var font = Theme.Font(9f * scale);
        var width = (int)(150 * scale);
        foreach (var entry in _entries)
        {
            if (entry.IsSeparator) continue;
            var size = TextRenderer.MeasureText(entry.Label, font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPrefix);
            width = Math.Max(width, size.Width + (int)(46 * scale));
        }

        var height = _padding * 2;
        foreach (var entry in _entries) height += entry.IsSeparator ? (int)(7 * scale) : _rowHeight;

        var screen = Screen.FromPoint(cursor).WorkingArea;
        var x = cursor.X;
        var y = cursor.Y;
        if (x + width > screen.Right - 4) x = screen.Right - width - 4;
        if (y + height > screen.Bottom - 4) y = screen.Bottom - height - 4;
        x = Math.Max(screen.Left + 4, x);
        y = Math.Max(screen.Top + 4, y);

        SetBounds(x, y, width, height);
        using var path = Theme.Rounded(new Rectangle(0, 0, width, height), (int)(8 * scale));
        Region = new Region(path);
        _hoverIndex = -1;
        Invalidate();
        if (!Visible) Show();
        else BringToFront();
    }

    public void MarkHover(Point screenPoint)
    {
        var index = HitTest(screenPoint);
        if (index == _hoverIndex) return;
        _hoverIndex = index;
        Invalidate();
    }

    public void HideMenu()
    {
        _hoverIndex = -1;
        Hide();
    }

    public Action? ActionAt(int index) =>
        index >= 0 && index < _entries.Count && _entries[index].Enabled ? _entries[index].Action : null;

    private int RowAt(int y)
    {
        var current = _padding;
        for (var i = 0; i < _entries.Count; i++)
        {
            var height = _entries[i].IsSeparator ? (int)(7 * (DeviceDpi / 96f)) : _rowHeight;
            if (y >= current && y < current + height) return _entries[i].IsSeparator ? -1 : i;
            current += height;
        }

        return -1;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var scale = DeviceDpi / 96f;
        using (var path = Theme.Rounded(new Rectangle(0, 0, Width, Height), (int)(8 * scale)))
        {
            using var fill = new SolidBrush(Theme.Panel);
            g.FillPath(fill, path);
            using var pen = new Pen(Theme.Border, Math.Max(1f, scale));
            g.DrawPath(pen, path);
        }

        using var font = Theme.Font(9f * scale);
        var y = _padding;
        for (var i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            if (entry.IsSeparator)
            {
                using var line = new Pen(Theme.Border, Math.Max(1f, scale));
                g.DrawLine(line, (int)(10 * scale), y + (int)(3 * scale), Width - (int)(10 * scale), y + (int)(3 * scale));
                y += (int)(7 * scale);
                continue;
            }

            if (i == _hoverIndex && entry.Enabled)
            {
                using var hover = new SolidBrush(Theme.PanelHover);
                g.FillRectangle(hover, (int)(4 * scale), y, Width - (int)(8 * scale), _rowHeight);
            }

            var color = !entry.Enabled ? Theme.Muted : i == 0 ? Theme.Accent : Theme.Text;
            TextRenderer.DrawText(
                g,
                entry.Label,
                font,
                new Rectangle((int)(14 * scale), y, Width - (int)(24 * scale), _rowHeight),
                color,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
            y += _rowHeight;
        }
    }
}
