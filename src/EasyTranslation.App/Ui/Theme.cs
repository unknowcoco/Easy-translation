using System.Drawing;
using System.Drawing.Drawing2D;

namespace EasyTranslation.Ui;

/// <summary>Shared palette and measurements for the little floating surfaces.</summary>
internal static class Theme
{
    public static readonly Color Panel = Color.FromArgb(0x1F, 0x23, 0x29);
    public static readonly Color PanelHover = Color.FromArgb(0x2A, 0x31, 0x3A);
    public static readonly Color Border = Color.FromArgb(0x3A, 0x40, 0x48);
    public static readonly Color Accent = Color.FromArgb(0x4C, 0x8D, 0xFF);
    public static readonly Color Text = Color.FromArgb(0xE8, 0xEC, 0xF2);
    public static readonly Color Muted = Color.FromArgb(0x9A, 0xA3, 0xB0);
    public static readonly Color Error = Color.FromArgb(0xE5, 0x6B, 0x6B);
    public static readonly Color ErrorPanel = Color.FromArgb(0x4A, 0x22, 0x24);

    public static Font Font(float points, FontStyle style = FontStyle.Regular) =>
        new("Microsoft YaHei UI", points, style, GraphicsUnit.Point);

    public static GraphicsPath Rounded(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var d = Math.Max(1, radius * 2);
        if (bounds.Width <= d || bounds.Height <= d)
        {
            path.AddRectangle(bounds);
            return path;
        }

        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
