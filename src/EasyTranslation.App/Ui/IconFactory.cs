using System.Drawing;
using System.Drawing.Drawing2D;

namespace EasyTranslation.Ui;

public static class IconFactory
{
    /// <summary>Fallback tray icon used when the executable icon cannot be read.</summary>
    public static Icon CreateTrayIcon()
    {
        try
        {
            var embedded = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (embedded is not null) return embedded;
        }
        catch
        {
            // fall through to the drawn icon
        }

        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using var path = Theme.Rounded(new Rectangle(1, 1, 30, 30), 8);
            using var brush = new LinearGradientBrush(
                new Rectangle(0, 0, 32, 32),
                Color.FromArgb(0x3B, 0x7B, 0xF0),
                Color.FromArgb(0x1B, 0x3F, 0x8F),
                45f);
            g.FillPath(brush, path);
            using var font = new Font("Segoe UI", 17f, FontStyle.Bold, GraphicsUnit.Pixel);
            const string glyph = "L";
            var size = g.MeasureString(glyph, font);
            g.DrawString(glyph, font, Brushes.White, (32 - size.Width) / 2f, (32 - size.Height) / 2f - 1);
        }

        return Icon.FromHandle(bitmap.GetHicon());
    }
}
