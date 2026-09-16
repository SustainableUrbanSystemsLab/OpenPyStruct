using OpenPyStruct.Core.Contract;
using OpenPyStruct.Core.Engine;
using OpenPyStruct.Core.Geometry;
using Rhino.Geometry;

namespace OpenPyStruct.GH.Types;

/// <summary>
/// A structural model plus how to draw it back in Rhino. The engine sees only
/// <see cref="Model"/>; the world-space nodes, per-element perpendicular and the plane normal
/// are what the Results components need to put forces and sections where the geometry is.
/// </summary>
public sealed class ModelDef
{
    public required StructuralModel Model { get; init; }
    public required Point3d[] NodesWorld { get; init; }
    /// <summary>In-plane unit vector perpendicular to each element (the "up" side of a beam).</summary>
    public required Vector3d[] ElementPerp { get; init; }
    /// <summary>Unit horizontal direction the structure runs in (engine +x).</summary>
    public required Vector3d U { get; init; }
    /// <summary>Unit normal of the analysis plane (section width direction).</summary>
    public required Vector3d Normal { get; init; }
    public FrameBuilder.MemberKind[]? Kinds { get; init; }
    public List<string> Warnings { get; init; } = new();
    public double Tolerance { get; init; } = 1e-3;

    public bool IsBeam => Model.Kind == "beam";
    public int NodeCount => Model.NodeCount;
    public int ElementCount => Model.ElementCount;

    public Point3d ElementStart(int e) => NodesWorld[Model.Elements[e][0]];
    public Point3d ElementEnd(int e) => NodesWorld[Model.Elements[e][1]];

    /// <summary>World displacement vector for engine (ux, uy).</summary>
    public Vector3d ToWorld(double ux, double uy) => U * ux + Vector3d.ZAxis * uy;

    /// <summary>Engine (fx, fy) of a world force vector, and the part that fell out of the plane.</summary>
    public (double fx, double fy, double outOfPlane) Project(Vector3d f) =>
        (f * U, f.Z, f * Normal);

    public int NearestNode(Point3d p, out double distance)
    {
        var best = 0; distance = double.MaxValue;
        for (var i = 0; i < NodesWorld.Length; i++)
        {
            var d = NodesWorld[i].DistanceTo(p);
            if (d < distance) { distance = d; best = i; }
        }
        return best;
    }

    public int NearestElement(Point3d p, out double distance)
    {
        var best = 0; distance = double.MaxValue;
        for (var e = 0; e < ElementCount; e++)
        {
            var d = new Line(ElementStart(e), ElementEnd(e)).DistanceTo(p, true);
            if (d < distance) { distance = d; best = e; }
        }
        return best;
    }

    public double Extent()
    {
        var bb = new BoundingBox(NodesWorld);
        return bb.Diagonal.Length;
    }

    public override string ToString() =>
        $"OpenPyStruct {Model.Kind}: {NodeCount} nodes, {ElementCount} elements, {Model.Supports.Count} supports";
}

public sealed class LoadCaseDef
{
    public required LoadCase Load { get; init; }
    public required ModelDef Model { get; init; }
    public List<string> Warnings { get; init; } = new();

    public override string ToString() =>
        $"Load case '{Load.Name}': {Load.PointLoads.Count} point loads, {Load.ElementLoads.Count} member loads";
}

public sealed class MaterialDef
{
    public Material Material { get; init; } = new();
    public override string ToString() => $"E={Material.E:0.###e0} Pa, nu={Material.Nu}, A={Material.A} m², I0={Material.I0} m⁴, k={Material.K}";
}

public sealed class OptimizerDef
{
    public OptimizerSettings Settings { get; init; } = new();
    public override string ToString() =>
        $"{Settings.Epochs} epochs, lr {Settings.LearningRate}, α_M {Settings.AlphaMoment}, α_V {Settings.AlphaShear}, patience {Settings.Patience}";
}

public sealed class EngineDef
{
    public EngineSettings Settings { get; init; } = new();
    public override string ToString() => Settings.ToString();
}

/// <summary>What a run produced, as ONE item: the case that went in, the result that came back.</summary>
public sealed class RunResult
{
    public required string Task { get; init; }
    public required CaseDocument Case { get; init; }
    public required ResultDocument Result { get; init; }
    public required string Folder { get; init; }
    public required TimeSpan Elapsed { get; init; }
    public ModelDef? Model { get; init; }
    public EngineSettings Engine { get; init; } = new();

    public override string ToString()
    {
        var s = $"OpenPyStruct {Task} ({Elapsed.TotalSeconds:0.#} s)";
        if (Result.I is { } I) s += $": {I.Length} elements, ΣI = {I.Sum():0.###e0} m⁴";
        if (Result.Nelem is { } n && Task == "train") s += $": model for {n} elements";
        if (Result.Samples is { } k && Task == "generate_data") s += $": {k} samples";
        return s;
    }
}
