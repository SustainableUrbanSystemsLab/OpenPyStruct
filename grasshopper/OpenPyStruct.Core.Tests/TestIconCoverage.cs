using System.Text.RegularExpressions;
using NUnit.Framework;

namespace OpenPyStruct.Core.Tests;

/// <summary>
/// Guards the one thing in the icon system that breaks silently: a component whose display name
/// has no glyph. Nothing maps components to icons except the file name, and a miss does not throw —
/// <c>Icons.For</c> returns null and Grasshopper draws its default box, which is invisible in every
/// build and every other test.
///
/// <para>The check is file-based on purpose. The resolver lives in the .gha, which needs Grasshopper
/// (and so Rhino) to load, and CI has neither; the component names are therefore read out of the
/// plugin sources and matched against the exported PNGs, which is the same contract the resolver
/// applies at runtime.</para>
/// </summary>
[TestFixture]
public class TestIconCoverage
{
    /// <summary>The set's own mark: the ribbon tab and assembly icon, with no component behind it.</summary>
    private const string PluginGlyph = "OpenPyStruct";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "pyproject.toml")))
            dir = dir.Parent;
        Assert.That(dir, Is.Not.Null, "could not locate the repository root above " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string IconDir() => Path.Combine(RepoRoot(), "grasshopper", "icons", "png24");

    /// <summary>Every component's display Name, read from its <c>base("Name", ...)</c> call.</summary>
    private static IEnumerable<(string Name, string File)> Components()
    {
        var cmp = Path.Combine(RepoRoot(), "grasshopper", "OpenPyStruct.GH", "CMP");
        foreach (var file in Directory.EnumerateFiles(cmp, "*.cs"))
        foreach (Match m in Regex.Matches(File.ReadAllText(file), @":\s*base\(""([^""]+)""\s*,\s*""[^""]*""\s*,"))
            yield return (m.Groups[1].Value, Path.GetFileName(file));
    }

    /// <summary>The transform in Icons.ResourceKey, mirrored so the test asks the same question.</summary>
    private static string ResourceKey(string name)
    {
        var s = name.Trim().Replace("+", "Plus");
        s = Regex.Replace(s, @"[\s/\-]", "_");
        s = Regex.Replace(s, @"[^A-Za-z0-9_]", "");
        return Regex.Replace(s, "_+", "_").TrimEnd('_');
    }

    [Test]
    public void EveryComponentHasAGlyph()
    {
        var components = Components().ToList();
        Assert.That(components, Is.Not.Empty, "the source scan found no components — the scan is broken, not the icons");

        var missing = components
            .Where(c => !File.Exists(Path.Combine(IconDir(), ResourceKey(c.Name) + ".png")))
            .Select(c => $"{c.Name} ({c.File}) -> {ResourceKey(c.Name)}.png")
            .ToList();

        Assert.That(missing, Is.Empty,
            "components with no glyph. Add a def() in grasshopper/icons/src/ops-vec-set.js and run "
            + "emit.js:\n  " + string.Join("\n  ", missing));
    }

    [Test]
    public void NoGlyphIsOrphaned()
    {
        var wanted = Components().Select(c => ResourceKey(c.Name)).Append(PluginGlyph).ToHashSet(StringComparer.Ordinal);
        var orphans = Directory.EnumerateFiles(IconDir(), "*.png")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => n != null && !wanted.Contains(n))
            .ToList();

        Assert.That(orphans, Is.Empty,
            "glyphs no component asks for (a rename usually leaves these). Delete the def() and run "
            + "`node emit.js`, which prunes all four artefacts: " + string.Join(", ", orphans));
    }

    [Test]
    public void ManifestCoversTheExportedGlyphs()
    {
        var manifest = Path.Combine(RepoRoot(), "grasshopper", "icons", "manifest.csv");
        Assert.That(File.Exists(manifest), Is.True, "manifest.csv is missing — run emit.js");

        var rows = File.ReadAllLines(manifest).Skip(1).Where(l => l.Trim().Length > 0)
            .Select(l => l.Split(',')).ToList();
        var named = rows.Select(r => r[0]).ToHashSet(StringComparer.Ordinal);
        var exported = Directory.EnumerateFiles(IconDir(), "*.png").Select(Path.GetFileNameWithoutExtension!).ToList();

        Assert.That(exported.Where(n => !named.Contains(n!)), Is.Empty, "PNGs with no manifest row");
        Assert.That(rows.Select(r => r[3]).Distinct().OrderBy(x => x),
            Is.EquivalentTo(new[] { "#6b7580", "#7a5af5", "#b5821f" }),
            "the set uses three families: Tooling grey, Prediction purple and Structure amber");
    }

    [Test]
    public void PngsAre24Square()
    {
        foreach (var file in Directory.EnumerateFiles(IconDir(), "*.png"))
        {
            // PNG IHDR: width and height are big-endian at bytes 16..23.
            var head = new byte[24];
            using (var fs = File.OpenRead(file)) Assert.That(fs.Read(head, 0, 24), Is.EqualTo(24), file);
            var w = (head[16] << 24) | (head[17] << 16) | (head[18] << 8) | head[19];
            var h = (head[20] << 24) | (head[21] << 16) | (head[22] << 8) | head[23];
            Assert.That((w, h), Is.EqualTo((24, 24)), Path.GetFileName(file) + " is not 24x24");
        }
    }
}
