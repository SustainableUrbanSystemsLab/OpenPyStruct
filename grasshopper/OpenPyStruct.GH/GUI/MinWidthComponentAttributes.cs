// Adapted from Eddy3D (https://github.com/Eddy3D-Dev/Eddy3D), GUI/MinWidthComponentAttributes.cs.
// Copyright (c) Eddy3D Authors. See GUI/README.md in this folder for provenance and licensing.

using Grasshopper.GUI;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;

namespace OpenPyStruct.GH.GUI;

/// <summary>
/// Keeps a component at least wide enough for a given piece of text.
/// <para>
/// Grasshopper sizes a component from its parameter names and icon — which says nothing about the
/// MESSAGE strip drawn underneath it. A component whose message is wider than its body gets that
/// message wrapped onto a second line, which reads as a rendering glitch rather than a layout
/// consequence. Widening the body is the fix.
/// </para>
/// <para>
/// The minimum is expressed as the TEXT that must fit rather than a pixel count, so it stays correct
/// across font and DPI changes instead of being a magic number tuned on one machine. Pass the
/// longest message the component can produce.
/// </para>
/// </summary>
public class MinWidthComponentAttributes : GH_ComponentAttributes
{
    private readonly string _mustFit;
    private readonly int _padding;

    /// <param name="mustFit">The widest string the component needs to display under itself.</param>
    /// <param name="padding">Slack either side of that string, in pixels.</param>
    public MinWidthComponentAttributes(IGH_Component owner, string mustFit, int padding = 12)
        : base(owner)
    {
        _mustFit = mustFit ?? "";
        _padding = padding;
    }

    /// <summary>
    /// Pixels the standard canvas font needs for <paramref name="text"/>, plus slack.
    /// <para>
    /// Shared with <see cref="GH_ComponentUIAttributes"/>, which needs the same measurement but
    /// cannot name <c>GH_FontServer</c> itself: that file's <c>using Grasshopper;</c> collides with
    /// this assembly's own <c>GUI</c> namespace and the lookup fails. Measuring here keeps one
    /// reference to the font server instead of two workarounds.
    /// </para>
    /// </summary>
    internal static float RequiredWidth(string text, int padding = 12) =>
        GH_FontServer.StringWidth(text ?? "", GH_FontServer.Standard) + padding;

    protected override void Layout()
    {
        base.Layout();

        if (string.IsNullOrEmpty(_mustFit)) return;

        var needed = GH_FontServer.StringWidth(_mustFit, GH_FontServer.Standard) + _padding;
        var extra = needed - Bounds.Width;
        if (extra <= 0) return;

        // Grow the BODY, not the parameter zones. Inputs are laid out to the left of the inner
        // bounds and keep their position; the outputs have to be re-laid against the new right edge,
        // because widening Bounds on its own would leave them floating mid-component.
        var bounds = Bounds;
        bounds.Width += extra;
        Bounds = bounds;

        m_innerBounds.Width += extra;
        LayoutOutputParams(Owner, m_innerBounds);
    }
}
