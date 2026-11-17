using System.Drawing;
using System.Drawing.Drawing2D;
using EasyTranslation.Interop;

namespace EasyTranslation.Ui;

/// <summary>
/// The little companion bar that appears next to the cursor after a right click on a selection.
/// It never activates, is topmost, and clicks on it are swallowed by the mouse hook (no WM_MOUSEACTIVATE).
/// </summary>
public sealed class ChipForm : Form
{
    private readonly System.Windows.Forms.Timer _hideTimer = new() { Interval = 4200 };
    private string _label = string.Empty;
    private bool _hover;

    public ChipForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Theme.Panel;
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        _hideTimer.Tick += (_, _) => Hide();
        Visible = false;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW | 0x00000008 /* WS_EX_TOPMOST */;
            return cp;
        }
    }

    public bool IsOver(Point screenPoint) => Visible && Bounds.Contains(screenPoint);

    public void MarkHover(bool hover)
    {
        if (_hover == hover) return;
        _hover = hover;
        Invalidate();
    }

    public void ShowAt(Point cursor, string label)
    {
        _label = label;
        var scale = DeviceDpi / 96f;
        using var font = Theme.Font(9.5f * scale, FontStyle.Bold);
        var text = TextRenderer.MeasureText(label, font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPrefix);
        var width = Math.Max((int)(172 * scale), text.Width + (int)(30 * scale));
        var height = (int)(36 * scale);
        var screen = Screen.FromPoint(cursor).WorkingArea;
        var x = cursor.X - width / 2;
        var y = cursor.Y - height - (int)(12 * scale);
        x = Math.Clamp(x, screen.Left + 4, screen.Right - width - 4);
        if (y < screen.Top + 4) y = Math.Min(cursor.Y + (int)(20 * scale), screen.Bottom - height - 4);

        SetBounds(x, y, width, height);
        using var path = Theme.Rounded(new Rectangle(0, 0, width, height), (int)(8 * scale));
        Region = new Region(path);
        _hover = false;
        Invalidate();

        if (!Visible) Show();
        else BringToFront();
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    public void HideChip()
    {
        _hideTimer.Stop();
        Hide();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var scale = DeviceDpi / 96f;
        var rect = new Rectangle(0, 0, Width, Height);
        using (var path = Theme.Rounded(rect, (int)(8 * scale)))
        {
            using var fill = new SolidBrush(_hover ? Theme.PanelHover : Theme.Panel);
            g.FillPath(fill, path);
            using var pen = new Pen(_hover ? Theme.Accent : Theme.Border, Math.Max(1f, scale));
            g.DrawPath(pen, path);
        }

        using var bar = new SolidBrush(Theme.Accent);
        g.FillRectangle(bar, (int)(10 * scale), (int)(9 * scale), Math.Max(2, (int)(3 * scale)), Height - (int)(18 * scale));

        using var font = Theme.Font(9.5f * scale, FontStyle.Bold);
        using var textBrush = new SolidBrush(Theme.Text);
        var textRect = new Rectangle((int)(20 * scale), 0, Width - (int)(28 * scale), Height);
        using var format = new StringFormat
        {
            LineAlignment = StringAlignment.Center,
            Alignment = StringAlignment.Near,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap,
        };
        g.DrawString(_label, font, textBrush, textRect, format);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _hideTimer.Dispose();
        base.Dispose(disposing);
    }
}
