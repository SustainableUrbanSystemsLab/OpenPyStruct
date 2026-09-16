using OpenPyStruct.Core.Contract;

namespace OpenPyStruct.Core.Geometry;

/// <summary>
/// Builds a planar frame model from line segments (columns, beams, braces — any topology) and
/// support points. Endpoints within <c>tolerance</c> of each other merge into one node, which
/// is what makes a bundle of Rhino lines a connected structure.
/// </summary>
public static class FrameBuilder
{
    public sealed record Segment(Vec3 A, Vec3 B);

    public enum MemberKind { Column, Beam, Brace }

    public sealed class Result
    {
        public required StructuralModel Model { get; init; }
        public required AnalysisPlane Plane { get; init; }
        public required Vec3[] NodesWorld { get; init; }
        public required MemberKind[] Kinds { get; init; }
        public required double MaxOutOfPlane { get; init; }
        public required List<string> Warnings { get; init; }
    }

    /// <param name="segments">Member axes in world space.</param>
    /// <param name="fixedPoints">Points that become FIXED supports (snapped to the nearest node).</param>
    /// <param name="pinnedPoints">Points that become PIN supports.</param>
    /// <param name="rollerPoints">Points that become ROLLER supports.</param>
    /// <param name="tolerance">Node merge distance, metres.</param>
    /// <param name="plane">Analysis plane, or null to fit one through the endpoints.</param>
    /// <param name="autoFixBase">When no support point is given, fix every node at the lowest level.</param>
    public static Result Build(IReadOnlyList<Segment> segments, IReadOnlyList<Vec3> fixedPoints,
        IReadOnlyList<Vec3> pinnedPoints, IReadOnlyList<Vec3> rollerPoints, double tolerance,
        AnalysisPlane? plane = null, bool autoFixBase = true)
    {
        if (segments.Count == 0) throw new ArgumentException("no members", nameof(segments));
        if (tolerance <= 0) throw new ArgumentException("tolerance must be positive", nameof(tolerance));
        var warnings = new List<string>();

        var endpoints = new List<Vec3>(segments.Count * 2);
        foreach (var s in segments) { endpoints.Add(s.A); endpoints.Add(s.B); }
        plane ??= AnalysisPlane.Fit(endpoints);

        // Merge endpoints into nodes.
        var nodes = new List<Vec3>();
        int NodeFor(Vec3 p)
        {
            for (var i = 0; i < nodes.Count; i++)
                if (nodes[i].DistanceTo(p) <= tolerance) return i;
            nodes.Add(p);
            return nodes.Count - 1;
        }

        var model = new StructuralModel { Kind = "frame" };
        var kinds = new List<MemberKind>();
        var seenPairs = new HashSet<(int, int)>();
        foreach (var s in segments)
        {
            var i = NodeFor(s.A);
            var j = NodeFor(s.B);
            if (i == j)
            {
                warnings.Add($"a member shorter than the tolerance ({tolerance} m) was dropped");
                continue;
            }
            var key = i < j ? (i, j) : (j, i);
            if (!seenPairs.Add(key))
            {
                warnings.Add("a duplicate member was dropped");
                continue;
            }
            model.Elements.Add(new[] { i, j });
            kinds.Add(Classify(plane.ToPlane(s.A), plane.ToPlane(s.B)));
        }
        if (model.Elements.Count == 0) throw new ArgumentException("every member collapsed or duplicated");

        var maxOut = 0.0;
        foreach (var n in nodes)
        {
            var p = plane.ToPlane(n);
            model.Nodes.Add(new[] { p.X, p.Y });
            maxOut = Math.Max(maxOut, Math.Abs(plane.OutOfPlane(n)));
        }
        if (maxOut > tolerance)
            warnings.Add($"geometry is up to {maxOut:0.###} m out of the analysis plane; it was projected");

        // Supports
        var supportByNode = new Dictionary<int, string>();
        void Snap(IReadOnlyList<Vec3> pts, string type)
        {
            foreach (var p in pts)
            {
                var n = Nearest(nodes, p, out var d);
                if (d > tolerance * 10)
                    warnings.Add($"support point {d:0.###} m from the nearest node was snapped to it");
                supportByNode[n] = type;
            }
        }
        Snap(rollerPoints, Support.Roller);
        Snap(pinnedPoints, Support.Pin);
        Snap(fixedPoints, Support.Fixed);

        if (supportByNode.Count == 0 && autoFixBase)
        {
            var minY = model.Nodes.Min(n => n[1]);
            for (var i = 0; i < model.Nodes.Count; i++)
                if (Math.Abs(model.Nodes[i][1] - minY) <= tolerance) supportByNode[i] = Support.Fixed;
            warnings.Add("no support points given: every node at the lowest level was fixed");
        }
        foreach (var (n, t) in supportByNode.OrderBy(kv => kv.Key))
            model.Supports.Add(new Support { Node = n, Type = t });

        return new Result
        {
            Model = model, Plane = plane, NodesWorld = nodes.ToArray(), Kinds = kinds.ToArray(),
            MaxOutOfPlane = maxOut, Warnings = warnings,
        };
    }

    public static int Nearest(IReadOnlyList<Vec3> nodes, Vec3 p, out double distance)
    {
        var best = 0; distance = double.MaxValue;
        for (var i = 0; i < nodes.Count; i++)
        {
            var d = nodes[i].DistanceTo(p);
            if (d < distance) { distance = d; best = i; }
        }
        return best;
    }

    /// <summary>Vertical within 10° = column, horizontal within 10° = beam, else brace.</summary>
    public static MemberKind Classify(Vec2 a, Vec2 b)
    {
        var dx = Math.Abs(b.X - a.X);
        var dy = Math.Abs(b.Y - a.Y);
        var angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        if (angle > 80) return MemberKind.Column;
        if (angle < 10) return MemberKind.Beam;
        return MemberKind.Brace;
    }
}
