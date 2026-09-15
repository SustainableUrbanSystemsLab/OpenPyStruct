using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper.Kernel;
using OpenPyStruct.Core.Results;
using OpenPyStruct.GH.GUI;
using OpenPyStruct.GH.Types;
using Rhino.Geometry;

namespace OpenPyStruct.GH.CMP;

/// <summary>
/// The result as geometry: every element as a rectangular section whose depth carries its I
/// (the scripts draw line thickness ∝ I^(1/3) for the same reason), coloured by I; the moment and
/// shear diagrams as offset polygons on the member; the deflected shape.
/// </summary>
public class VisualizeResultCMP : GH_BeautifulComponent
{
    public VisualizeResultCMP() : base("Visualize Result", "VizResult",
        "Sections sized by I (rectangle of the given width, depth = (12·I/b)^(1/3)) and coloured by I, "
        + "plus moment and shear diagrams and the deflected shape of one load case. Scales of 0 fit "
        + "the diagram to a tenth of the model's size.",
        Strings.Category, Strings.Results)
    {
        UseParamUI();
    }

    public override Guid ComponentGuid => new("2B6F1A10-9C3D-4E52-8F71-0A1B2C3D4E21");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap Icon => Icons.For("Vz", Icons.ResultsColor);

    protected override void RegisterInputParams(GH_InputParamManager pm)
    {
        pm.AddGenericParameter("Result", "Result", "From Optimize or Predict.", GH_ParamAccess.item);
        pm.AddIntegerParameter("Case", "Case", "Load case to draw diagrams for (0-based).", GH_ParamAccess.item, 0);
        pm.AddNumberParameter("Width", "b", "Section width for the depth-from-I drawing, m.", GH_ParamAccess.item, 0.3);
        pm.AddNumberParameter("Diagram scale", "Scale", "Metres of offset per N·m (moment) and per N (shear). 0 = auto.", GH_ParamAccess.item, 0.0);
        pm.AddNumberParameter("Deflection scale", "DefScale", "Displacement magnification. 0 = auto.", GH_ParamAccess.item, 0.0);
        pm.AddIntervalParameter("Range", "Range", "I range mapped onto the colour ramp. Unwired: fit to the data. "
            + "Pin it to compare two runs on one scale.", GH_ParamAccess.item);
        pm[5].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pm)
    {
        pm.AddMeshParameter("Sections", "Sections", "One box per element, depth from I, vertex-coloured by I.", GH_ParamAccess.list);
        pm.AddColourParameter("Colors", "Colors", "Colour per element.", GH_ParamAccess.list);
        pm.AddNumberParameter("Depths", "Depths", "Section depth per element, m.", GH_ParamAccess.list);
        pm.AddCurveParameter("Moment", "Moment", "Moment diagram, one closed polyline per element (sagging drawn on the 'up' side).", GH_ParamAccess.list);
        pm.AddCurveParameter("Shear", "Shear", "Shear diagram, one closed polyline per element.", GH_ParamAccess.list);
        pm.AddCurveParameter("Deflected", "Deflected", "Deflected shape, one line per element.", GH_ParamAccess.list);
        pm.AddIntervalParameter("Range", "Range", "The I range used for the colours, for a legend.", GH_ParamAccess.item);
        pm.AddTextParameter("Scales", "Scales", "The diagram and deflection scales actually used.", GH_ParamAccess.item);
    }

    protected override void Solve(IGH_DataAccess da)
    {
        RunResult run = null;
        var caseIndex = 0; var width = 0.3; var scale = 0.0; var defScale = 0.0;
        var range = Interval.Unset;
        if (!da.GetData(0, ref run) || run == null) { Error("Wire a Result from Optimize or Predict."); return; }
        da.GetData(1, ref caseIndex); da.GetData(2, ref width); da.GetData(3, ref scale); da.GetData(4, ref defScale);
        var hasRange = da.GetData(5, ref range) && range.IsValid && range.Length > 0;

        var model = run.Model;
        var I = run.Result.I;
        if (model == null || I == null) { Error("This result has no geometry or no I to draw."); return; }
        if (I.Length != model.ElementCount) { Error($"Result has {I.Length} elements, the model {model.ElementCount}."); return; }
        if (width <= 0) { Error("Width must be positive."); return; }

        var depths = SectionShape.Depths(I, width);
        var min = hasRange ? range.Min : I.Min();
        var max = hasRange ? range.Max : I.Max();
        if (max <= min) max = min + 1e-12;

        var meshes = new List<Mesh>(I.Length);
        var colors = new List<Color>(I.Length);
        for (var e = 0; e < I.Length; e++)
        {
            var color = Ramp(SectionShape.Normalize(I[e], min, max));
            colors.Add(color);
            meshes.Add(Box(model.ElementStart(e), model.ElementEnd(e), model.ElementPerp[e], model.Normal, depths[e], width, color));
        }
        da.SetDataList(0, meshes);
        da.SetDataList(1, colors);
        da.SetDataList(2, depths);
        da.SetData(6, new Interval(min, max));

        if (run.Result.Cases.Count == 0) { Warning("No load-case analysis in this result; only sections drawn."); return; }
        if (caseIndex < 0 || caseIndex >= run.Result.Cases.Count) { Error($"Case must be 0..{run.Result.Cases.Count - 1}."); return; }
        var c = run.Result.Cases[caseIndex];
        var extent = Math.Max(model.Extent(), 1e-6);

        var mMax = Diagrams.AbsMax(c, Quantity.Moment);
        var vMax = Diagrams.AbsMax(c, Quantity.Shear);
        var mScale = scale > 0 ? scale : (mMax > 0 ? 0.1 * extent / mMax : 0);
        var vScale = scale > 0 ? scale : (vMax > 0 ? 0.1 * extent / vMax : 0);
        da.SetDataList(3, Diagram(model, Diagrams.EndValues(c, Quantity.Moment), mScale));
        da.SetDataList(4, Diagram(model, Diagrams.EndValues(c, Quantity.Shear), vScale));

        var dMax = c.Displacements.Select(d => Math.Sqrt(d[0] * d[0] + d[1] * d[1])).DefaultIfEmpty(0).Max();
        var dScale = defScale > 0 ? defScale : (dMax > 0 ? 0.1 * extent / dMax : 1);
        var deflected = new List<Curve>(model.ElementCount);
        Point3d Moved(int n) => model.NodesWorld[n] + model.ToWorld(c.Displacements[n][0], c.Displacements[n][1]) * dScale;
        for (var e = 0; e < model.ElementCount; e++)
            deflected.Add(new LineCurve(Moved(model.Model.Elements[e][0]), Moved(model.Model.Elements[e][1])));
        da.SetDataList(5, deflected);
        da.SetData(7, $"moment {mScale:0.###e0} m/(N·m), shear {vScale:0.###e0} m/N, deflection ×{dScale:0.#}; "
                      + $"|M|max {mMax / 1e3:0.#} kN·m, |V|max {vMax / 1e3:0.#} kN, |u|max {dMax * 1e3:0.#} mm");
        Message = c.Name;
    }

    private static List<Curve> Diagram(ModelDef model, double[][] ends, double scale)
    {
        var curves = new List<Curve>(ends.Length);
        for (var e = 0; e < ends.Length; e++)
        {
            var a = model.ElementStart(e); var b = model.ElementEnd(e);
            var up = model.ElementPerp[e];
            var pl = new Polyline { a, a + up * (ends[e][0] * scale), b + up * (ends[e][1] * scale), b, a };
            curves.Add(pl.ToPolylineCurve());
        }
        return curves;
    }

    private static Mesh Box(Point3d a, Point3d b, Vector3d up, Vector3d side, double depth, double width, Color color)
    {
        var h = up * (depth / 2); var w = side * (width / 2);
        var m = new Mesh();
        m.Vertices.Add(a - h - w); m.Vertices.Add(a - h + w); m.Vertices.Add(a + h + w); m.Vertices.Add(a + h - w);
        m.Vertices.Add(b - h - w); m.Vertices.Add(b - h + w); m.Vertices.Add(b + h + w); m.Vertices.Add(b + h - w);
        m.Faces.AddFace(0, 3, 2, 1); // start cap
        m.Faces.AddFace(4, 5, 6, 7); // end cap
        m.Faces.AddFace(0, 1, 5, 4); // bottom
        m.Faces.AddFace(1, 2, 6, 5); // +side
        m.Faces.AddFace(2, 3, 7, 6); // top
        m.Faces.AddFace(3, 0, 4, 7); // -side
        for (var i = 0; i < 8; i++) m.VertexColors.Add(color);
        m.Normals.ComputeNormals();
        m.Compact();
        return m;
    }

    /// <summary>Blue → cyan → green → yellow → red.</summary>
    private static Color Ramp(double t)
    {
        t = Math.Clamp(t, 0, 1);
        var hue = (1 - t) * 240.0;
        return FromHsv(hue, 0.85, 0.95);
    }

    private static Color FromHsv(double h, double s, double v)
    {
        var c = v * s; var x = c * (1 - Math.Abs(h / 60.0 % 2 - 1)); var m = v - c;
        double r, g, b;
        if (h < 60) (r, g, b) = (c, x, 0); else if (h < 120) (r, g, b) = (x, c, 0); else if (h < 180) (r, g, b) = (0, c, x);
        else if (h < 240) (r, g, b) = (0, x, c); else if (h < 300) (r, g, b) = (x, 0, c); else (r, g, b) = (c, 0, x);
        return Color.FromArgb((int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
    }
}
