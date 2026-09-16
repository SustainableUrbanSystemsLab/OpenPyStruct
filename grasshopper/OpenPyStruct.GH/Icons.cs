using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Text;

namespace OpenPyStruct.GH;

/// <summary>
/// The single source of component icons: the vector set in <c>grasshopper/icons/</c>, embedded as
/// 24x24 PNGs and resolved by the component's DISPLAY NAME.
///
/// <para>Why by name: the set is authored per component (<c>icons/manifest.csv</c> maps name to
/// ribbon tab, family and accent) and every glyph file is that name, so the drawing and the
/// component that shows it stay in lockstep with no third mapping table to drift. A component with
/// no glyph resolves to null, which Grasshopper draws as its default box — invisible in every
/// build, which is why <c>TestIconCoverage</c> checks the set covers every component.</para>
/// </summary>
public static class Icons
{
    private const string ResourcePrefix = "OpenPyStruct.Icons.";

    /// <summary>The plugin's own mark: the ribbon tab icon and the assembly icon.</summary>
    public const string PluginGlyph = "OpenPyStruct";

    // Cache the raw PNG bytes, not Bitmaps: callers get a fresh Bitmap each time, because
    // Grasshopper takes ownership of what it is handed and a shared instance would be disposed
    // out from under the next caller.
    private static readonly ConcurrentDictionary<string, byte[]?> Cache = new(StringComparer.Ordinal);

    /// <summary>
    /// The icon for <paramref name="componentName"/> (a component's <c>Name</c>), or null when the
    /// set has no glyph for it. Never throws: an icon is decoration, and a component that fails to
    /// draw one must still load.
    /// </summary>
    public static Bitmap? For(string componentName)
    {
        var bytes = BytesFor(componentName);
        if (bytes == null) return null;
        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            return new Bitmap(ms);
        }
        catch
        {
            return null;
        }
    }

    public static Bitmap? Plugin() => For(PluginGlyph);

    /// <summary>Raw PNG bytes for a component name, or null. Exposed for tests.</summary>
    public static byte[]? BytesFor(string componentName)
    {
        var key = ResourceKey(componentName);
        if (key == null) return null;

        return Cache.GetOrAdd(key, static k =>
        {
            var asm = typeof(Icons).Assembly;
            using var stream = asm.GetManifestResourceStream(ResourcePrefix + k + ".png");
            if (stream == null) return null;
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        });
    }

    /// <summary>
    /// Component display name to glyph file stem: spaces, <c>-</c> and <c>/</c> become <c>_</c>,
    /// <c>+</c> becomes <c>Plus</c>, other punctuation is dropped and <c>_</c> runs collapse.
    /// This transform IS the wiring between a component and its drawing.
    /// </summary>
    public static string? ResourceKey(string componentName)
    {
        if (string.IsNullOrWhiteSpace(componentName)) return null;

        var sb = new StringBuilder(componentName.Length);
        foreach (var ch in componentName.Trim())
        {
            if (ch == '+') sb.Append("Plus");
            else if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (ch == '_' || char.IsWhiteSpace(ch) || ch == '/' || ch == '-') sb.Append('_');
        }

        var collapsed = new StringBuilder(sb.Length);
        var lastWasSep = false;
        foreach (var ch in sb.ToString())
        {
            if (ch == '_')
            {
                if (collapsed.Length > 0 && !lastWasSep) collapsed.Append('_');
                lastWasSep = true;
            }
            else
            {
                collapsed.Append(ch);
                lastWasSep = false;
            }
        }

        var key = collapsed.ToString().TrimEnd('_');
        return key.Length == 0 ? null : key;
    }

    /// <summary>Every glyph embedded in this assembly. For tests and tooling.</summary>
    public static string[] AvailableNames()
    {
        var asm = typeof(Icons).Assembly;
        var result = new List<string>();
        foreach (var n in asm.GetManifestResourceNames())
            if (n.StartsWith(ResourcePrefix, StringComparison.Ordinal) && n.EndsWith(".png", StringComparison.Ordinal))
                result.Add(n.Substring(ResourcePrefix.Length, n.Length - ResourcePrefix.Length - 4));
        result.Sort(StringComparer.Ordinal);
        return result.ToArray();
    }
}
