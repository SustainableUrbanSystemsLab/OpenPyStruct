// Adapted from Eddy3D (https://github.com/Eddy3D-Dev/Eddy3D), GUI/ProgressBar.cs.
// Copyright (c) Eddy3D Authors. See GUI/README.md in this folder for provenance and licensing.

using System;
using System.Globalization;

namespace OpenPyStruct.GH.GUI;

/// <summary>
/// A progress bar for a component's Message strip — the black tag Grasshopper draws under the
/// component body.
///
/// <para><b>Why text and not a drawn rectangle.</b> The Message tag is laid out and painted by
/// Grasshopper's own <c>GH_ComponentAttributes</c>, from geometry it does not expose; painting a
/// rectangle into it means re-deriving that layout and re-deriving it again whenever Grasshopper
/// changes. A bar built out of characters is drawn by the same code that already draws the
/// message, so it survives every zoom level, both themes and a Grasshopper update — and it still
/// reads as a bar.</para>
///
/// <para><b>Why these two characters.</b> U+2588 FULL BLOCK and U+2591 LIGHT SHADE are both in
/// Unicode's Block Elements, which the fonts Rhino ships on Windows and macOS render at the same
/// advance width. That is the whole requirement: the strip must not change width as the bar
/// fills, or the component jitters once per progress update. Mixing, say, <c>=</c> and <c>.</c>
/// would do exactly that in a proportional font.</para>
///
/// <para>Size the component for the FULL bar with
/// <c>MinWidthText = ProgressBar.Widest("downloading geog_high_res_mandatory")</c>; see
/// <c>.claude/rules/component-ui.md</c> → "Size a component to the text it draws".</para>
/// </summary>
public static class ProgressBar
{
    /// <summary>U+2588 FULL BLOCK — the filled part of the bar.</summary>
    public const char Filled = '█';

    /// <summary>U+2591 LIGHT SHADE — the empty part. Same advance width as <see cref="Filled"/>.</summary>
    public const char Empty = '░';

    /// <summary>Cells in a default bar. Ten keeps the strip narrow and reads one cell per 10%.</summary>
    public const int DefaultCells = 10;

    /// <summary>
    /// The bar alone, e.g. <c>████░░░░░░ 42%</c>. A fraction outside 0..1 is clamped, and NaN
    /// reads as 0 rather than throwing — progress arrives from a worker thread that may be
    /// dividing by a total it has not learned yet.
    /// </summary>
    public static string Text(double fraction, int cells = DefaultCells)
    {
        if (cells < 1) cells = 1;
        if (double.IsNaN(fraction)) fraction = 0.0;
        if (fraction < 0.0) fraction = 0.0;
        if (fraction > 1.0) fraction = 1.0;

        // Floor rather than round, so the bar and the percentage never disagree and neither reads
        // "done" until it is. A run that stops at 99.6% should look unfinished, because it is.
        var filled = (int)(fraction * cells);
        if (filled > cells) filled = cells;
        var percent = (int)(fraction * 100.0);

        return string.Concat(
            new string(Filled, filled),
            new string(Empty, cells - filled),
            " ",
            percent.ToString(CultureInfo.InvariantCulture),
            "%");
    }

    /// <summary>The bar behind a label, e.g. <c>downloading geog ████░░░░░░ 42%</c>.</summary>
    public static string Text(string label, double fraction, int cells = DefaultCells) =>
        string.IsNullOrEmpty(label)
            ? Text(fraction, cells)
            : label + " " + Text(fraction, cells);

    /// <summary>
    /// The bar for <paramref name="done"/> of <paramref name="total"/> items, with the count kept
    /// in the label: <c>GRIB 3/5 ██████░░░░ 60%</c>. A file count is what the user can check
    /// against the folder, so it belongs on the strip alongside the percentage.
    /// </summary>
    public static string Text(string label, int done, int total, int cells = DefaultCells)
    {
        var fraction = total > 0 ? (double)done / total : 0.0;
        var counted = total > 0
            ? $"{label} {done.ToString(CultureInfo.InvariantCulture)}/{total.ToString(CultureInfo.InvariantCulture)}"
            : label;
        return Text(counted, fraction, cells);
    }

    /// <summary>
    /// The widest string this bar can produce for <paramref name="label"/> — a full bar at 100%.
    /// Feed it to <see cref="GH_BeautifulComponent.MinWidthText"/> so the component is already
    /// wide enough before the first progress update arrives.
    /// </summary>
    public static string Widest(string label, int cells = DefaultCells) =>
        Text(label, 1.0, cells);
}
