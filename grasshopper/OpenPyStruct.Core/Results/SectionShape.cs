namespace OpenPyStruct.Core.Results;

/// <summary>
/// Turns a moment of inertia into something drawable. The optimizer works in I alone (the
/// scripts never choose a section), so the plugin shows a rectangular section of fixed width
/// <c>b</c> whose depth carries the I: <c>I = b h^3 / 12  →  h = (12 I / b)^(1/3)</c>. That is a
/// visualization convention, not a design — the scripts draw line thickness ∝ I^(1/3) for the
/// same reason.
/// </summary>
public static class SectionShape
{
    public static double DepthForInertia(double I, double width)
    {
        if (width <= 0) throw new ArgumentException("width must be positive", nameof(width));
        if (I <= 0) return 0;
        return Math.Cbrt(12.0 * I / width);
    }

    public static double[] Depths(IReadOnlyList<double> I, double width)
    {
        var h = new double[I.Count];
        for (var i = 0; i < h.Length; i++) h[i] = DepthForInertia(I[i], width);
        return h;
    }

    /// <summary>Linear ramp position of a value in [min, max], clamped to 0..1.</summary>
    public static double Normalize(double value, double min, double max)
    {
        if (max <= min) return 0;
        return Math.Clamp((value - min) / (max - min), 0, 1);
    }
}
