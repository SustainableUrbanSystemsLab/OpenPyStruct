using OpenPyStruct.Core.Contract;

namespace OpenPyStruct.Core.Geometry;

/// <summary>
/// Turns a straight beam axis into the engine's node/element model. Positions along the beam
/// are given as distances from the start (metres); supports and loads snap to the nearest node,
/// which is what the original scripts do (they pick nodes, not coordinates).
/// </summary>
public static class BeamBuilder
{
    public sealed record Snap(double Requested, int Node, double Actual);

    public sealed class Result
    {
        public required StructuralModel Model { get; init; }
        public required double[] NodeX { get; init; }
        public required List<Snap> Snaps { get; init; }
        public double MaxSnapError => Snaps.Count == 0 ? 0 : Snaps.Max(s => Math.Abs(s.Actual - s.Requested));
    }

    /// <summary>
    /// Build a beam of <paramref name="length"/> with <paramref name="elements"/> equal elements.
    /// Pins restrain x and y, rollers y, fixed all three. The 2D model runs along +x at y = 0.
    /// </summary>
    public static Result Build(double length, int elements, IEnumerable<double> pins,
        IEnumerable<double> rollers, IEnumerable<double> fixedSupports)
    {
        if (length <= 0) throw new ArgumentException("beam length must be positive", nameof(length));
        if (elements < 1) throw new ArgumentException("at least one element", nameof(elements));

        var x = new double[elements + 1];
        for (var i = 0; i <= elements; i++) x[i] = length * i / elements;

        var model = new StructuralModel { Kind = "beam" };
        foreach (var xi in x) model.Nodes.Add(new[] { xi, 0.0 });
        for (var e = 0; e < elements; e++) model.Elements.Add(new[] { e, e + 1 });

        var snaps = new List<Snap>();
        var seen = new Dictionary<int, string>();
        void Add(IEnumerable<double> positions, string type)
        {
            foreach (var p in positions)
            {
                var n = NearestNode(x, p);
                snaps.Add(new Snap(p, n, x[n]));
                if (seen.TryGetValue(n, out var existing))
                {
                    // A pin and a roller on one node is a pin; fixed beats both.
                    if (Rank(type) > Rank(existing))
                    {
                        seen[n] = type;
                        model.Supports.First(s => s.Node == n).Type = type;
                    }
                    continue;
                }
                seen[n] = type;
                model.Supports.Add(new Support { Node = n, Type = type });
            }
        }
        Add(fixedSupports, Support.Fixed);
        Add(pins, Support.Pin);
        Add(rollers, Support.Roller);
        model.Supports.Sort((a, b) => a.Node.CompareTo(b.Node));

        return new Result { Model = model, NodeX = x, Snaps = snaps };
    }

    public static int NearestNode(double[] x, double position)
    {
        var best = 0; var bestD = double.MaxValue;
        for (var i = 0; i < x.Length; i++)
        {
            var d = Math.Abs(x[i] - position);
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    private static int Rank(string type) => type switch
    {
        Support.Fixed => 3, Support.Pin => 2, Support.Roller => 1, _ => 0,
    };

    /// <summary>Is the support set enough to hold a beam? (Stops a mechanism before the engine does.)</summary>
    public static string? StabilityProblem(StructuralModel model)
    {
        if (model.Supports.Count == 0) return "no supports";
        var hasX = model.Supports.Any(s => s.Type is Support.Pin or Support.Fixed);
        if (!hasX) return "no support restrains the beam axially: add a pin or a fixed support";
        var distinctY = model.Supports.Select(s => s.Node).Distinct().Count();
        var anyFixed = model.Supports.Any(s => s.Type == Support.Fixed);
        if (!anyFixed && distinctY < 2) return "a beam on one pin rotates freely: add a roller or make it fixed";
        return null;
    }
}
