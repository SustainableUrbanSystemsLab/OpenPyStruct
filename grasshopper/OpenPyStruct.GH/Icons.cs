using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace OpenPyStruct.GH;

/// <summary>
/// Generated 24×24 icons: a rounded tile in the sub-tab's colour with a two-letter glyph. No
/// resource files to keep in sync; a real icon set can replace this later without touching
/// the components (they only call <see cref="For"/>).
/// </summary>
public static class Icons
{
    private static readonly ConcurrentDictionary<string, byte[]> Cache = new();

    public static Bitmap For(string glyph, Color tile) => Draw(glyph, tile);

    public static Bitmap Plugin() => Draw("OP", Color.FromArgb(52, 73, 94));

    public static readonly Color ModelColor = Color.FromArgb(41, 128, 185);
    public static readonly Color LoadsColor = Color.FromArgb(192, 57, 43);
    public static readonly Color SettingsColor = Color.FromArgb(127, 140, 141);
    public static readonly Color RunColor = Color.FromArgb(39, 174, 96);
    public static readonly Color ResultsColor = Color.FromArgb(142, 68, 173);

    private static Bitmap Draw(string glyph, Color tile)
    {
        var bmp = new Bitmap(24, 24);
        try
        {
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using var path = Rounded(new RectangleF(1, 1, 22, 22), 5);
            using var brush = new SolidBrush(tile);
            g.FillPath(brush, path);
            using var font = new Font(FontFamily.GenericSansSerif, glyph.Length > 2 ? 7f : 9f, FontStyle.Bold, GraphicsUnit.Point);
            var size = g.MeasureString(glyph, font);
            g.DrawString(glyph, font, Brushes.White, (24 - size.Width) / 2f, (24 - size.Height) / 2f);
        }
        catch
        {
            // An icon is decoration; a component must load without one.
        }
        return bmp;
    }

    private static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        var d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
