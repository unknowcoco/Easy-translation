using System.Drawing;
using System.Drawing.Drawing2D;
using EasyTranslation.Interop;

namespace EasyTranslation.Ui;

/// <summary>Small non-activating status bubble for "translating…", success and errors.</summary>
public sealed class ToastForm : Form
{
    private readonly System.Windows.Forms.Timer _hideTimer = new() { Interval = 2600 };
    private string _text = string.Empty;
    private bool _error;
    private bool _busy;

    public ToastForm()
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
            cp.ExStyle |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW | 0x00000008;
            return cp;
        }
    }

    public void ShowToast(string text, bool error, Point at, bool busy = false)
    {
        _text = text;
        _error = error;
        _busy = busy;

        var scale = DeviceDpi / 96f;
        using var font = Theme.Font(9f * scale);
        var maxWidth = (int)(360 * scale);
        var measured = TextRenderer.MeasureText(
            text,
            font,
            new Size(maxWidth, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl);
        var width = Math.Max((int)(120 * scale), measured.Width + (int)(30 * scale));
        var height = Math.Max((int)(34 * scale), measured.Height + (int)(18 * scale));

        var screen = Screen.FromPoint(at).WorkingArea;
        var x = Math.Clamp(at.X - width / 2, screen.Left + 4, screen.Right - width - 4);
        var y = at.Y - height - (int)(10 * scale);
        if (y < screen.Top + 4) y = Math.Min(at.Y + (int)(22 * scale), screen.Bottom - height - 4);

        SetBounds(x, y, width, height);
        using var path = Theme.Rounded(new Rectangle(0, 0, width, height), (int)(8 * scale));
        Region = new Region(path);
        Invalidate();

        if (!Visible) Show();
        else BringToFront();
        _hideTimer.Stop();
        _hideTimer.Interval = error ? 4200 : busy ? 60000 : 2600;
        _hideTimer.Start();
    }

    public void HideToast()
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
            using var fill = new SolidBrush(_error ? Theme.ErrorPanel : Theme.Panel);
            g.FillPath(fill, path);
            using var pen = new Pen(_error ? Theme.Error : _busy ? Theme.Accent : Theme.Border, Math.Max(1f, scale));
            g.DrawPath(pen, path);
        }

        using var font = Theme.Font(9f * scale);
        using var brush = new SolidBrush(_error ? Color.FromArgb(0xFF, 0xDD, 0xDD) : Theme.Text);
        var textRect = new Rectangle((int)(12 * scale), (int)(8 * scale), Width - (int)(24 * scale), Height - (int)(16 * scale));
        TextRenderer.DrawText(
            g,
            _text,
            font,
            textRect,
            _error ? Color.FromArgb(0xFF, 0xDD, 0xDD) : Theme.Text,
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _hideTimer.Dispose();
        base.Dispose(disposing);
    }
}
