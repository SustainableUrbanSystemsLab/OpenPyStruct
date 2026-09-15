using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper.Kernel;
using OpenPyStruct.Core.Contract;
using OpenPyStruct.Core.Geometry;
using OpenPyStruct.GH.GUI;
using OpenPyStruct.GH.Types;
using Rhino.Geometry;

namespace OpenPyStruct.GH.CMP;

/// <summary>
/// One load case: point loads at nodes and a uniform load on members. Wire several into
/// Optimize to design for all of them at once (energies are summed across cases), or into
/// Predict as the multi-case input the surrogates were trained on.
/// </summary>
public class LoadCaseCMP : GH_BeautifulComponent
{
    public LoadCaseCMP() : base("Load Case", "Loads",
        "Point loads (N, as vectors: -Z is gravity) snapped to the nearest node, plus a uniform "
        + "load (N/m, negative = downward) on the chosen members — all horizontal members of a frame, "
        + "or the whole beam, when none are chosen.",
        Strings.Category, Strings.Loads)
    {
        UseParamUI();
    }

    public override Guid ComponentGuid => new("2B6F1A10-9C3D-4E52-8F71-0A1B2C3D4E03");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap Icon => Icons.For("LC", Icons.LoadsColor);

    protected override void RegisterInputParams(GH_InputParamManager pm)
    {
        pm.AddGenericParameter("Model", "Model", "From Beam Model or Frame Model.", GH_ParamAccess.item);
        pm.AddPointParameter("Points", "Points", "Where point loads act; each snaps to its nearest node.", GH_ParamAccess.list);
        pm[1].Optional = true;
        pm.AddVectorParameter("Forces", "Forces", "Force per point in newtons, e.g. {0,0,-355857} for an "
            + "80 kip semi. One vector is reused for every point.", GH_ParamAccess.list);
        pm[2].Optional = true;
        pm.AddNumberParameter("UDL", "UDL", "Uniform load in N/m, negative downward (the scripts use -5000 "
            + "on a beam, -10000 on frame beams).", GH_ParamAccess.item, 0.0);
        pm[3].Optional = true;
        pm.AddCurveParameter("Members", "Members", "Members the UDL applies to (matched by nearest element). "
            + "Unwired: every element of a beam, every horizontal member of a frame.", GH_ParamAccess.list);
        pm[4].Optional = true;
        pm.AddTextParameter("Name", "Name", "Label for this case.", GH_ParamAccess.item, "LC1");
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pm)
    {
        pm.AddGenericParameter("Load Case", "LC", "For Optimize / Predict.", GH_ParamAccess.item);
        pm.AddLineParameter("Arrows", "Arrows", "Force arrows scaled to the model, for a quick check.", GH_ParamAccess.list);
        pm.AddTextParameter("Info", "Info", "What was applied where.", GH_ParamAccess.item);
    }

    protected override void Solve(IGH_DataAccess da)
    {
        ModelDef model = null;
        var points = new List<Point3d>(); var forces = new List<Vector3d>(); var members = new List<Curve>();
        var udl = 0.0; var name = "LC1";
        if (!da.GetData(0, ref model) || model == null) { Error("Wire a model."); return; }
        da.GetDataList(1, points); da.GetDataList(2, forces); da.GetData(3, ref udl);
        da.GetDataList(4, members); da.GetData(5, ref name);

        if (points.Count > 0 && forces.Count == 0) { Error("Points need Forces."); return; }
        if (forces.Count > 1 && forces.Count != points.Count) { Error($"{points.Count} points but {forces.Count} forces."); return; }

        var lc = new LoadCase { Name = string.IsNullOrWhiteSpace(name) ? "LC" : name };
        var arrows = new List<Line>();
        var info = new List<string>();
        var extent = Math.Max(model.Extent(), 1e-6);
        var maxF = forces.Count > 0 ? forces.Max(f => f.Length) : 0;
        var outOfPlane = 0.0;
        for (var i = 0; i < points.Count; i++)
        {
            var f = forces[forces.Count == 1 ? 0 : i];
            var node = model.NearestNode(points[i], out var d);
            if (d > model.Tolerance * 2) Warning($"Point {i} was snapped {d:0.###} m to node {node}.");
            var (fx, fy, oop) = model.Project(f);
            outOfPlane = Math.Max(outOfPlane, Math.Abs(oop));
            lc.PointLoads.Add(new PointLoad { Node = node, Fx = fx, Fy = fy });
            info.Add($"node {node}: Fx {fx:0.#} N, Fy {fy:0.#} N");
            if (maxF > 0)
            {
                var tip = model.NodesWorld[node];
                var dir = f; dir.Unitize();
                arrows.Add(new Line(tip - dir * (0.12 * extent * f.Length / maxF), tip));
            }
        }
        if (outOfPlane > 1e-9 * Math.Max(maxF, 1))
            Warning($"Forces have an out-of-plane component (up to {outOfPlane:0.#} N) that a 2D analysis cannot carry; it was dropped.");

        if (udl != 0.0)
        {
            IEnumerable<int> elements;
            if (members.Count > 0)
            {
                var set = new SortedSet<int>();
                foreach (var c in members)
                {
                    if (c == null) continue;
                    var mid = c.PointAt(c.Domain.Mid);
                    var e = model.NearestElement(mid, out var d);
                    if (d > model.Tolerance * 2) Warning($"A UDL member is {d:0.###} m from the nearest element; it was still applied to element {e}.");
                    set.Add(e);
                }
                elements = set;
            }
            else if (model.IsBeam || model.Kinds == null)
                elements = Enumerable.Range(0, model.ElementCount);
            else
                elements = Enumerable.Range(0, model.ElementCount).Where(e => model.Kinds[e] == FrameBuilder.MemberKind.Beam);

            var list = elements.ToList();
            foreach (var e in list) lc.ElementLoads.Add(new ElementLoad { Element = e, Wy = udl });
            info.Add($"UDL {udl:0.#} N/m on {list.Count} element(s)");
            if (list.Count == 0) Warning("The UDL applies to no element (a frame with no horizontal members?).");
        }

        if (lc.PointLoads.Count == 0 && lc.ElementLoads.Count == 0) Warning("This load case is empty.");

        da.SetData(0, new LoadCaseDef { Load = lc, Model = model });
        da.SetDataList(1, arrows);
        da.SetData(2, $"{lc.Name}: " + (info.Count == 0 ? "no loads" : string.Join("; ", info)));
        Message = $"{lc.PointLoads.Count} pt, {(udl != 0 ? "UDL" : "no UDL")}";
    }
}
