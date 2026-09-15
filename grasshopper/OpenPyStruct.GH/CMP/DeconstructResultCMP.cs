using System;
using System.Drawing;
using System.Linq;
using Grasshopper.Kernel;
using OpenPyStruct.GH.GUI;
using OpenPyStruct.GH.Types;
using Rhino.Geometry;

namespace OpenPyStruct.GH.CMP;

/// <summary>The numbers behind a run: I per element, section forces and displacements of one load case.</summary>
public class DeconstructResultCMP : GH_BeautifulComponent
{
    public DeconstructResultCMP() : base("Deconstruct Result", "DeResult",
        "Numbers from an Optimize or Predict result. Section forces are internal forces at both "
        + "element ends: sagging-positive moment, V = dM/dx, tension-positive axial. Displacements are "
        + "world vectors per node.",
        Strings.Category, Strings.Results)
    {
        UseParamUI();
    }

    public override Guid ComponentGuid => new("8885DA1A-EE71-4E72-B3A9-42ED60777935");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap Icon => Icons.For("De", Icons.ResultsColor);

    protected override void RegisterInputParams(GH_InputParamManager pm)
    {
        pm.AddGenericParameter("Result", "Result", "From Optimize or Predict.", GH_ParamAccess.item);
        pm.AddIntegerParameter("Case", "Case", "Which load case's forces to read (0-based).", GH_ParamAccess.item, 0);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pm)
    {
        pm.AddNumberParameter("I", "I", "Moment of inertia per element, m⁴.", GH_ParamAccess.list);
        pm.AddNumberParameter("Axial", "N", "Axial force per element, N.", GH_ParamAccess.list);
        pm.AddNumberParameter("Shear start", "Vi", "Shear at the element start, N.", GH_ParamAccess.list);
        pm.AddNumberParameter("Shear end", "Vj", "Shear at the element end, N.", GH_ParamAccess.list);
        pm.AddNumberParameter("Moment start", "Mi", "Bending moment at the element start, N·m.", GH_ParamAccess.list);
        pm.AddNumberParameter("Moment end", "Mj", "Bending moment at the element end, N·m.", GH_ParamAccess.list);
        pm.AddVectorParameter("Displacements", "Disp", "Nodal displacement, m, as a world vector.", GH_ParamAccess.list);
        pm.AddNumberParameter("Rotations", "Rot", "Nodal rotation, rad.", GH_ParamAccess.list);
        pm.AddPointParameter("Nodes", "Nodes", "Node positions, for pairing with the displacements.", GH_ParamAccess.list);
        pm.AddNumberParameter("Loss", "Loss", "Total loss per epoch (Optimize only).", GH_ParamAccess.list);
        pm.AddTextParameter("Cases", "Cases", "Load case names, in index order.", GH_ParamAccess.list);
    }

    protected override void Solve(IGH_DataAccess da)
    {
        RunResult run = null;
        var caseIndex = 0;
        if (!da.GetData(0, ref run) || run == null) { Error("Wire a Result from Optimize or Predict."); return; }
        da.GetData(1, ref caseIndex);
        var res = run.Result;
        if (res.I == null) { Error($"This is a '{run.Task}' result; it carries no I."); return; }

        da.SetDataList(0, res.I);
        da.SetDataList(9, res.Losses?.Total ?? Array.Empty<double>());
        da.SetDataList(10, res.Cases.Select(c => c.Name));
        if (run.Model != null) da.SetDataList(8, run.Model.NodesWorld);

        if (res.Cases.Count == 0) { Warning("No load-case analysis in this result."); return; }
        if (caseIndex < 0 || caseIndex >= res.Cases.Count) { Error($"Case must be 0..{res.Cases.Count - 1}."); return; }
        var c = res.Cases[caseIndex];
        da.SetDataList(1, c.Axial);
        da.SetDataList(2, c.ShearI);
        da.SetDataList(3, c.ShearJ);
        da.SetDataList(4, c.MomentI);
        da.SetDataList(5, c.MomentJ);
        if (run.Model != null)
        {
            da.SetDataList(6, c.Displacements.Select(d => run.Model.ToWorld(d[0], d[1])));
        }
        else
        {
            da.SetDataList(6, c.Displacements.Select(d => new Vector3d(d[0], 0, d[1])));
            Warning("The result has no model geometry; displacements are given in the analysis plane (x, 0, y).");
        }
        da.SetDataList(7, c.Displacements.Select(d => d.Length > 2 ? d[2] : 0.0));
        Message = c.Name;
    }
}
