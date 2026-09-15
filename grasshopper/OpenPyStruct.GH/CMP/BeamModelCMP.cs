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
/// A straight beam from a curve: evenly discretized, supports snapped to the nearest node —
/// the continuous-beam problem of OpenPyStruct_BeamOpt.py, with the geometry drawn instead of
/// randomized. This is also the ONLY model the ML components (Generate Data / Train / Predict)
/// accept: the surrogates learn beams described by roller and load positions along x.
/// </summary>
public class BeamModelCMP : GH_BeautifulComponent
{
    public BeamModelCMP() : base("Beam Model", "Beam",
        "A straight beam discretized into equal elements, with pin/roller/fixed supports snapped "
        + "to the nearest node. Units: metres. Draw the beam horizontally (gravity is world -Z).",
        Strings.Category, Strings.Model)
    {
        UseParamUI();
    }

    public override Guid ComponentGuid => new("2B6F1A10-9C3D-4E52-8F71-0A1B2C3D4E01");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap Icon => Icons.For("Bm", Icons.ModelColor);

    protected override void RegisterInputParams(GH_InputParamManager pm)
    {
        pm.AddCurveParameter("Axis", "Axis", "The beam axis (a line). Its length is the span.", GH_ParamAccess.item);
        pm.AddIntegerParameter("Elements", "N", "Number of equal elements. The scripts use 100; "
            + "a trained surrogate only accepts the element count it was trained on.", GH_ParamAccess.item, 100);
        pm.AddPointParameter("Pins", "Pins", "Pin supports (x and y restrained). Default when nothing "
            + "is wired: a pin at the start.", GH_ParamAccess.list);
        pm[2].Optional = true;
        pm.AddPointParameter("Rollers", "Rollers", "Roller supports (y restrained). Default when nothing "
            + "is wired: a roller at the end.", GH_ParamAccess.list);
        pm[3].Optional = true;
        pm.AddPointParameter("Fixed", "Fixed", "Fixed supports (x, y and rotation restrained), e.g. a cantilever root.",
            GH_ParamAccess.list);
        pm[4].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pm)
    {
        pm.AddGenericParameter("Model", "Model", "The beam model for Load Case and the Run components.", GH_ParamAccess.item);
        pm.AddPointParameter("Nodes", "Nodes", "Node positions along the axis.", GH_ParamAccess.list);
        pm.AddPointParameter("Supports", "Supports", "Where the supports landed after snapping.", GH_ParamAccess.list);
        pm.AddTextParameter("Info", "Info", "Element count, span, supports and snap distances.", GH_ParamAccess.item);
    }

    protected override void Solve(IGH_DataAccess da)
    {
        Curve axis = null;
        var n = 100;
        var pins = new List<Point3d>(); var rollers = new List<Point3d>(); var fixeds = new List<Point3d>();
        if (!da.GetData(0, ref axis) || axis == null) { Error("Wire the beam axis."); return; }
        da.GetData(1, ref n);
        da.GetDataList(2, pins); da.GetDataList(3, rollers); da.GetDataList(4, fixeds);

        if (n < 1) { Error("Elements must be at least 1."); return; }
        var length = axis.GetLength();
        if (length <= 0) { Error("The axis has no length."); return; }
        if (!axis.IsLinear(1e-6)) Warning("The axis is not a straight line; positions are measured along its length.");

        var start = axis.PointAtStart;
        var dir = axis.PointAtEnd - start;
        dir.Unitize();
        var horizontal = new Vector3d(dir.X, dir.Y, 0);
        if (horizontal.Length < 1e-9) { Error("The beam is vertical. Draw it horizontally (gravity is -Z); use Frame Model for columns."); return; }
        horizontal.Unitize();
        var normal = Vector3d.CrossProduct(horizontal, Vector3d.ZAxis);
        normal.Unitize();
        // "Up" perpendicular to the axis within the vertical plane containing it.
        var perp = Vector3d.ZAxis - dir * (dir * Vector3d.ZAxis);
        perp.Unitize();

        double Along(Point3d p)
        {
            axis.ClosestPoint(p, out var t);
            var sub = axis.Trim(axis.Domain.Min, t);
            return sub?.GetLength() ?? 0.0;
        }

        var pinX = pins.Select(Along).ToList();
        var rollerX = rollers.Select(Along).ToList();
        var fixedX = fixeds.Select(Along).ToList();
        if (pinX.Count == 0 && rollerX.Count == 0 && fixedX.Count == 0)
        {
            pinX.Add(0.0); rollerX.Add(length);
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "No supports wired: simply supported (pin at start, roller at end).");
        }

        BeamBuilder.Result built;
        try { built = BeamBuilder.Build(length, n, pinX, rollerX, fixedX); }
        catch (ArgumentException ex) { Error(ex.Message); return; }

        var problem = BeamBuilder.StabilityProblem(built.Model);
        if (problem != null) Warning("Unstable: " + problem + ".");
        if (built.MaxSnapError > length / n / 2 + 1e-9)
            Warning($"A support was snapped {built.MaxSnapError:0.###} m to the nearest node.");

        var nodes = built.NodeX.Select(x => axis.PointAtLength(x)).ToArray();
        var def = new ModelDef
        {
            Model = built.Model,
            NodesWorld = nodes,
            ElementPerp = Enumerable.Repeat(perp, n).ToArray(),
            U = horizontal,
            Normal = normal,
            Tolerance = length / n / 2,
        };

        da.SetData(0, def);
        da.SetDataList(1, nodes);
        da.SetDataList(2, built.Model.Supports.Select(s => nodes[s.Node]));
        da.SetData(3, $"Beam: span {length:0.###} m, {n} elements ({length / n:0.###} m each)\n"
                      + "Supports: " + string.Join(", ", built.Model.Supports.Select(s => $"{s.Type}@{built.NodeX[s.Node]:0.##} m"))
                      + (problem != null ? $"\nUNSTABLE: {problem}" : ""));
        Message = $"{n} elements, {length:0.#} m";
    }
}
