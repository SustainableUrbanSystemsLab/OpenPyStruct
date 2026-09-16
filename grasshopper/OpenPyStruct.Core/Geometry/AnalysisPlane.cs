namespace OpenPyStruct.Core.Geometry;

/// <summary>
/// The vertical plane a 2D analysis lives in: an origin, a horizontal in-plane axis U and the
/// world-up axis V. Maps Rhino-space points to engine (x, y) and back so results can be drawn
/// where the geometry was modelled.
/// <para>
/// Gravity is world -Z. That is not negotiable in this plugin: the engine's y is UP and every
/// load sign in the JSON assumes it, so the plane's V is always +Z and only U (the direction the
/// structure runs in) is fitted from the geometry.
/// </para>
/// </summary>
public sealed class AnalysisPlane
{
    public Vec3 Origin { get; }
    public Vec3 U { get; }
    public Vec3 V => Vec3.UnitZ;
    public Vec3 Normal => U.Cross(V);

    public AnalysisPlane(Vec3 origin, Vec3 u)
    {
        var horizontal = new Vec3(u.X, u.Y, 0);
        if (horizontal.Length < 1e-12)
            throw new ArgumentException("the in-plane axis must have a horizontal component", nameof(u));
        Origin = origin;
        U = horizontal.Unit();
    }

    public Vec2 ToPlane(Vec3 p)
    {
        var d = p - Origin;
        return new Vec2(d.Dot(U), d.Z);
    }

    public Vec3 ToWorld(Vec2 p) => Origin + U * p.X + V * p.Y;

    /// <summary>Signed distance of a point from the plane, to detect out-of-plane geometry.</summary>
    public double OutOfPlane(Vec3 p) => (p - Origin).Dot(Normal);

    /// <summary>
    /// Fit the plane through a set of points: origin at the lowest point, U along the dominant
    /// horizontal spread (principal direction of the XY projection). Points that all share one XY
    /// location (a single column) get U = world X.
    /// </summary>
    public static AnalysisPlane Fit(IReadOnlyList<Vec3> points)
    {
        if (points.Count == 0) throw new ArgumentException("no points", nameof(points));
        double cx = 0, cy = 0;
        foreach (var p in points) { cx += p.X; cy += p.Y; }
        cx /= points.Count; cy /= points.Count;

        // 2x2 covariance of the XY projection; its principal eigenvector is the run direction.
        double sxx = 0, sxy = 0, syy = 0;
        foreach (var p in points)
        {
            var dx = p.X - cx; var dy = p.Y - cy;
            sxx += dx * dx; sxy += dx * dy; syy += dy * dy;
        }
        Vec3 u;
        if (sxx + syy < 1e-18)
            u = Vec3.UnitX;
        else
        {
            var theta = 0.5 * Math.Atan2(2 * sxy, sxx - syy);
            u = new Vec3(Math.Cos(theta), Math.Sin(theta), 0);
            // Orient U toward +X (or +Y for a Y-running structure) so node order reads left to right.
            if (u.X < -1e-9 || (Math.Abs(u.X) <= 1e-9 && u.Y < 0)) u = u * -1;
        }

        // Origin: the smallest U coordinate, at the lowest Z — so x runs from 0 and the ground is y = 0.
        var minU = double.MaxValue; var minZ = double.MaxValue;
        foreach (var p in points) minU = Math.Min(minU, (p - points[0]).Dot(u));
        foreach (var p in points) minZ = Math.Min(minZ, p.Z);
        var o = points[0] + u * minU;
        return new AnalysisPlane(new Vec3(o.X, o.Y, minZ), u);
    }
}
