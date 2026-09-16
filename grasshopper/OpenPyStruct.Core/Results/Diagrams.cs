using OpenPyStruct.Core.Contract;

namespace OpenPyStruct.Core.Results;

/// <summary>
/// Per-element end values → per-element polylines for a force diagram. Between the two ends a
/// moment is linear under point loads and parabolic under a UDL; the engine reports only the
/// ends, so each element is drawn as a straight segment M_i→M_j (shear likewise). With the
/// 20–100 elements the scripts use, that IS the curve.
/// </summary>
public static class Diagrams
{
    /// <summary>[ [v_i, v_j] per element ] for the given quantity of a case result.</summary>
    public static double[][] EndValues(CaseResult c, Quantity q)
    {
        var (a, b) = q switch
        {
            Quantity.Moment => (c.MomentI, c.MomentJ),
            Quantity.Shear => (c.ShearI, c.ShearJ),
            Quantity.Axial => (c.Axial, c.Axial),
            _ => throw new ArgumentOutOfRangeException(nameof(q)),
        };
        var rows = new double[a.Length][];
        for (var e = 0; e < a.Length; e++) rows[e] = new[] { a[e], b[e] };
        return rows;
    }

    public static double AbsMax(CaseResult c, Quantity q)
    {
        var m = 0.0;
        foreach (var row in EndValues(c, q))
            foreach (var v in row) m = Math.Max(m, Math.Abs(v));
        return m;
    }

    /// <summary>Largest |uy| over the nodes — for a sensible default deflection scale.</summary>
    public static double MaxDeflection(CaseResult c)
    {
        var m = 0.0;
        foreach (var d in c.Displacements) if (d.Length > 1) m = Math.Max(m, Math.Abs(d[1]));
        return m;
    }
}

public enum Quantity { Moment, Shear, Axial }
