using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper.Kernel;
using OpenPyStruct.Core.Geometry;
using OpenPyStruct.GH.GUI;
using OpenPyStruct.GH.Types;
using Rhino.Geometry;

namespace OpenPyStruct.GH.CMP;

/// <summary>
/// A planar frame from member lines — any topology, not only the bays × stories grid of
/// OpenPyStruct_FrameOpt_Discrete_Beta.py. Endpoints within the tolerance merge into nodes; the
/// analysis plane is fitted through the geometry (vertical, since gravity is world -Z).
/// </summary>
public class FrameModelCMP : GH_BeautifulComponent
{
    public FrameModelCMP() : base("Frame Model", "Frame",
        "A 2D frame from member lines (columns, beams, braces). Endpoints closer than the tolerance "
        + "become one node. Supports snap to the nearest node; with none given, every node on the "
        + "lowest level is fixed. Units: metres. Gravity is world -Z.",
        Strings.Category, Strings.Model)
    {
        UseParamUI();
    }

    public override Guid ComponentGuid => new("2B6F1A10-9C3D-4E52-8F71-0A1B2C3D4E02");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap Icon => Icons.For("Fr", Icons.ModelColor);

    protected override void RegisterInputParams(GH_InputParamManager pm)
    {
        pm.AddCurveParameter("Members", "Members", "Member axes: lines or polylines (each segment becomes an element).",
            GH_ParamAccess.list);
        pm.AddPointParameter("Fixed", "Fixed", "Fixed support points.", GH_ParamAccess.list);
        pm[1].Optional = true;
        pm.AddPointParameter("Pins", "Pins", "Pinned support points.", GH_ParamAccess.list);
        pm[2].Optional = true;
        pm.AddPointParameter("Rollers", "Rollers", "Roller support points (vertical restraint only).", GH_ParamAccess.list);
        pm[3].Optional = true;
        pm.AddNumberParameter("Tolerance", "Tol", "Node merge distance in metres.", GH_ParamAccess.item, 0.001);
        pm.AddPlaneParameter("Plane", "Plane", "Analysis plane: origin and X axis define where x = 0 and "
            + "which way it runs. Leave unwired to fit one through the members.", GH_ParamAccess.item);
        pm[5].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pm)
    {
        pm.AddGenericParameter("Model", "Model", "The frame model for Load Case and Optimize.", GH_ParamAccess.item);
        pm.AddPointParameter("Nodes", "Nodes", "Merged node positions (index = node number).", GH_ParamAccess.list);
        pm.AddPointParameter("Supports", "Supports", "Supported nodes.", GH_ParamAccess.list);
        pm.AddLineParameter("Elements", "Elements", "One line per element, in element order.", GH_ParamAccess.list);
        pm.AddTextParameter("Kinds", "Kinds", "column / beam / brace per element.", GH_ParamAccess.list);
        pm.AddTextParameter("Info", "Info", "Counts, plane and warnings.", GH_ParamAccess.item);
    }

    protected override void Solve(IGH_DataAccess da)
    {
        var members = new List<Curve>();
        var fixeds = new List<Point3d>(); var pins = new List<Point3d>(); var rollers = new List<Point3d>();
        var tol = 0.001;
        var plane = Plane.Unset;
        if (!da.GetDataList(0, members) || members.Count == 0) { Error("Wire the member lines."); return; }
        da.GetDataList(1, fixeds); da.GetDataList(2, pins); da.GetDataList(3, rollers);
        da.GetData(4, ref tol);
        var hasPlane = da.GetData(5, ref plane) && plane.IsValid;
        if (tol <= 0) { Error("Tolerance must be positive."); return; }

        var segments = new List<FrameBuilder.Segment>();
        foreach (var c in members)
        {
            if (c == null) continue;
            if (c.TryGetPolyline(out var pl))
            {
                for (var i = 0; i + 1 < pl.Count; i++) segments.Add(new FrameBuilder.Segment(V(pl[i]), V(pl[i + 1])));
            }
            else
            {
                if (!c.IsLinear(tol)) Warning("A curved member was replaced by the chord between its ends.");
                segments.Add(new FrameBuilder.Segment(V(c.PointAtStart), V(c.PointAtEnd)));
            }
        }

        AnalysisPlane analysisPlane = null;
        if (hasPlane)
        {
            try { analysisPlane = new AnalysisPlane(V(plane.Origin), V(plane.XAxis)); }
            catch (ArgumentException ex) { Error("Plane: " + ex.Message); return; }
        }

        FrameBuilder.Result built;
        try
        {
            built = FrameBuilder.Build(segments, fixeds.Select(V).ToList(), pins.Select(V).ToList(),
                rollers.Select(V).ToList(), tol, analysisPlane);
        }
        catch (ArgumentException ex) { Error(ex.Message); return; }

        foreach (var w in built.Warnings) Warning(w);

        var u = new Vector3d(built.Plane.U.X, built.Plane.U.Y, built.Plane.U.Z);
        var normal = new Vector3d(built.Plane.Normal.X, built.Plane.Normal.Y, built.Plane.Normal.Z);
        var nodes = built.NodesWorld.Select(p => new Point3d(p.X, p.Y, p.Z)).ToArray();
        var perp = new Vector3d[built.Model.ElementCount];
        var lines = new Line[built.Model.ElementCount];
        for (var e = 0; e < perp.Length; e++)
        {
            var a = nodes[built.Model.Elements[e][0]]; var b = nodes[built.Model.Elements[e][1]];
            lines[e] = new Line(a, b);
            var d = b - a; d.Unitize();
            // local y = (-s, c) in the plane -> world
            var pa = built.Plane.ToPlane(V(a)); var pb = built.Plane.ToPlane(V(b));
            var L = pa.DistanceTo(pb);
            var cs = (pb.X - pa.X) / L; var sn = (pb.Y - pa.Y) / L;
            perp[e] = u * -sn + Vector3d.ZAxis * cs;
        }

        var def = new ModelDef
        {
            Model = built.Model, NodesWorld = nodes, ElementPerp = perp, U = u, Normal = normal,
            Kinds = built.Kinds, Warnings = built.Warnings, Tolerance = tol,
        };
        var kinds = built.Kinds.Select(k => k.ToString().ToLowerInvariant()).ToList();
        da.SetData(0, def);
        da.SetDataList(1, nodes);
        da.SetDataList(2, built.Model.Supports.Select(s => nodes[s.Node]));
        da.SetDataList(3, lines);
        da.SetDataList(4, kinds);
        da.SetData(5, $"Frame: {built.Model.NodeCount} nodes, {built.Model.ElementCount} elements "
                      + $"({kinds.Count(k => k == "column")} columns, {kinds.Count(k => k == "beam")} beams, {kinds.Count(k => k == "brace")} braces)\n"
                      + $"Supports: {string.Join(", ", built.Model.Supports.Select(s => $"{s.Type}@n{s.Node}"))}\n"
                      + $"Plane: origin ({built.Plane.Origin.X:0.##}, {built.Plane.Origin.Y:0.##}, {built.Plane.Origin.Z:0.##}), x along ({u.X:0.##}, {u.Y:0.##}, 0)"
                      + (built.Warnings.Count > 0 ? "\n" + string.Join("\n", built.Warnings) : ""));
        Message = $"{built.Model.ElementCount} elements";
    }

    private static Vec3 V(Point3d p) => new(p.X, p.Y, p.Z);
    private static Vec3 V(Vector3d v) => new(v.X, v.Y, v.Z);
}
