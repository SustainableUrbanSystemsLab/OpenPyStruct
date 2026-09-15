namespace OpenPyStruct.GH.GUI;

/// <summary>Where the "Open Documentation…" menu item goes: the README, anchored by component name.</summary>
public static class Docs
{
    public const string Repo = "https://github.com/SustainableUrbanSystemsLab/OpenPyStruct";

    public static string SearchUrl(string componentName) =>
        Repo + "/blob/main/grasshopper/README.md#" + Slug(componentName);

    private static string Slug(string name) =>
        new(name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
}
