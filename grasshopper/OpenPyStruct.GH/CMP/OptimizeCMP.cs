using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper.Kernel;
using OpenPyStruct.Core.Contract;
using OpenPyStruct.GH.Types;

namespace OpenPyStruct.GH.CMP;

/// <summary>Gradient-based I optimization of a beam or frame under one or more load cases.</summary>
public class OptimizeCMP : RunComponentBase
{
    public OptimizeCMP() : base("Optimize", "Optimize",
        "Minimize ΣI + bending + shear energy over every element (OpenPyStruct_BeamOpt / FrameOpt) "
        + "for the wired load cases, in the container. Returns I per element and the final forces and "
        + "deflections of every case.")
    {
    }

    public override Guid ComponentGuid => new("B19D97ED-36F4-4056-987F-7B1FE140F04A");
    protected override Bitmap Icon => Icons.For("Opt", Icons.RunColor);
    protected override string Task => "optimize";
    protected override string ProgressLabel => "Optimizing";

    protected override void RegisterInputParams(GH_InputParamManager pm)
    {
        pm.AddGenericParameter("Model", "Model", "From Beam Model or Frame Model.", GH_ParamAccess.item);
        pm.AddGenericParameter("Loads", "Loads", "One or more Load Cases. Energies are summed across cases.", GH_ParamAccess.list);
        pm.AddGenericParameter("Material", "Material", "From Material. Unwired: the scripts' steel.", GH_ParamAccess.item);
        pm[2].Optional = true;
        pm.AddGenericParameter("Settings", "Settings", "From Optimizer Settings. Unwired: the beam script's defaults.", GH_ParamAccess.item);
        pm[3].Optional = true;
        pm.AddGenericParameter("Engine", "Engine", "From Engine. Unwired: image 'openpystruct', auto-detected CLI.", GH_ParamAccess.item);
        pm[4].Optional = true;
        pm.AddTextParameter("Folder", "Folder", "Run folder. Unwired: a time-stamped folder under the engine's runs root.", GH_ParamAccess.item);
        pm[5].Optional = true;
        AddRunInput(pm);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pm)
    {
        AddCommonOutputs(pm);
        pm.AddNumberParameter("I", "I", "Optimized moment of inertia per element, m⁴.", GH_ParamAccess.list);
        pm.AddTextParameter("Summary", "Summary", "Epochs, losses, ΣI and the peak forces.", GH_ParamAccess.item);
    }

    protected override bool TryBuildCase(IGH_DataAccess da, out CaseDocument doc, out EngineDef engine,
        out string folder, out ModelDef model, out int key)
    {
        doc = null; engine = null; folder = null; model = null; key = 0;
        var loads = new List<LoadCaseDef>();
        MaterialDef material = null; OptimizerDef settings = null;
        if (!da.GetData(0, ref model) || model == null) { Error("Wire a model."); return false; }
        da.GetDataList(1, loads);
        loads = loads.Where(l => l != null).ToList();
        if (loads.Count == 0) { Error("Wire at least one Load Case."); return false; }
        da.GetData(2, ref material); da.GetData(3, ref settings); da.GetData(4, ref engine);
        da.GetData(5, ref folder);
        foreach (var lc in loads)
            if (!ReferenceEquals(lc.Model, model))
                Warning($"Load case '{lc.Load.Name}' was built on a different model; node numbers may not match.");

        engine ??= DefaultEngine();
        doc = new CaseDocument
        {
            Task = Task,
            Model = model.Model,
            Material = material?.Material ?? new Material(),
            Optimizer = settings?.Settings ?? new OptimizerSettings(),
            LoadCases = loads.Select(l => l.Load).ToList(),
        };
        key = Key(model, loads.Cast<object>(), material, settings, engine, folder ?? "");
        return true;
    }

    protected override void SetOutputs(IGH_DataAccess da, RunResult r)
    {
        var res = r.Result;
        da.SetDataList("I", res.I);
        da.SetData("Summary", Summaries.Optimize(r));
    }
}

/// <summary>Surrogate inference with a trained FNN/PINN bundle, followed by an FE check of the prediction.</summary>
public class PredictCMP : RunComponentBase
{
    public PredictCMP() : base("Predict", "Predict",
        "Predict the optimized I of a beam with a trained FNN or PINN model (from Train), then run the "
        + "FE analysis with that I so the result carries checkable forces and deflections. The beam "
        + "must have the element count the model was trained on; fewer load cases than the model's "
        + "cases-per-sample are repeated.")
    {
    }

    public override Guid ComponentGuid => new("E9ABE1CA-457E-4F68-871C-F451AD88433D");
    protected override Bitmap Icon => Icons.For("Prd", Icons.RunColor);
    protected override string Task => "predict";
    protected override string ProgressLabel => "Predicting";
    private string _modelFile;

    protected override void RegisterInputParams(GH_InputParamManager pm)
    {
        pm.AddGenericParameter("Model", "Model", "From Beam Model (surrogates are trained on beams).", GH_ParamAccess.item);
        pm.AddGenericParameter("Loads", "Loads", "Load Cases; at most the model's cases-per-sample.", GH_ParamAccess.list);
        pm.AddTextParameter("Weights", "Weights", "Path to a model bundle (.pt) written by Train.", GH_ParamAccess.item);
        pm.AddGenericParameter("Material", "Material", "From Material, for the FE check. Unwired: defaults.", GH_ParamAccess.item);
        pm[3].Optional = true;
        pm.AddGenericParameter("Engine", "Engine", "From Engine. Unwired: defaults.", GH_ParamAccess.item);
        pm[4].Optional = true;
        pm.AddTextParameter("Folder", "Folder", "Run folder. Unwired: time-stamped under the runs root.", GH_ParamAccess.item);
        pm[5].Optional = true;
        AddRunInput(pm);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pm)
    {
        AddCommonOutputs(pm);
        pm.AddNumberParameter("I", "I", "Predicted moment of inertia per element, m⁴.", GH_ParamAccess.list);
        pm.AddTextParameter("Summary", "Summary", "Model kind, ΣI and the FE check's peaks.", GH_ParamAccess.item);
    }

    protected override bool TryBuildCase(IGH_DataAccess da, out CaseDocument doc, out EngineDef engine,
        out string folder, out ModelDef model, out int key)
    {
        doc = null; engine = null; folder = null; model = null; key = 0;
        var loads = new List<LoadCaseDef>();
        MaterialDef material = null; string weights = null;
        if (!da.GetData(0, ref model) || model == null) { Error("Wire a beam model."); return false; }
        if (!model.IsBeam) { Error("Predict works on beams only; wire Beam Model."); return false; }
        da.GetDataList(1, loads);
        loads = loads.Where(l => l != null).ToList();
        if (loads.Count == 0) { Error("Wire at least one Load Case."); return false; }
        if (!da.GetData(2, ref weights) || string.IsNullOrWhiteSpace(weights)) { Error("Wire the path to a trained model (.pt)."); return false; }
        if (!System.IO.File.Exists(weights)) { Error($"Weights file not found: {weights}"); return false; }
        da.GetData(3, ref material); da.GetData(4, ref engine); da.GetData(5, ref folder);

        engine ??= DefaultEngine();
        _modelFile = weights;
        doc = new CaseDocument
        {
            Task = Task,
            Model = model.Model,
            Material = material?.Material ?? new Material(),
            LoadCases = loads.Select(l => l.Load).ToList(),
        };
        key = Key(model, loads.Cast<object>(), weights, material, engine, folder ?? "");
        return true;
    }

    protected override void Prepare(Core.Engine.EngineSettings settings, string runFolder, CaseDocument doc)
    {
        doc.TaskParams["model"] = Core.Engine.ContainerRunner.ExposeFile(settings, runFolder, _modelFile);
    }

    protected override void SetOutputs(IGH_DataAccess da, RunResult r)
    {
        da.SetDataList("I", r.Result.I);
        da.SetData("Summary", Summaries.Optimize(r));
    }
}

internal static class Summaries
{
    public static string Optimize(RunResult r)
    {
        var res = r.Result;
        var I = res.I ?? Array.Empty<double>();
        var lines = new List<string>
        {
            $"Task: {r.Task}" + (res.ModelKind != null ? $" ({res.ModelKind.ToUpperInvariant()} surrogate)" : ""),
            $"Elements: {I.Length}, ΣI = {I.Sum():0.###e0} m⁴, I ∈ [{(I.Length > 0 ? I.Min() : 0):0.###e0}, {(I.Length > 0 ? I.Max() : 0):0.###e0}]",
        };
        if (res.Epochs is { } ep)
            lines.Add($"Epochs: {ep}" + (res.StoppedEarly == true ? " (stopped early)" : "")
                      + (res.BestLoss is { } bl ? $", best loss {bl:0.###e0}" : ""));
        foreach (var c in res.Cases)
        {
            var mMax = c.MomentI.Concat(c.MomentJ).Select(Math.Abs).DefaultIfEmpty(0).Max();
            var vMax = c.ShearI.Concat(c.ShearJ).Select(Math.Abs).DefaultIfEmpty(0).Max();
            var dMax = c.Displacements.Select(d => d.Length > 1 ? Math.Abs(d[1]) : 0).DefaultIfEmpty(0).Max();
            lines.Add($"{c.Name}: |M|max {mMax / 1e3:0.#} kN·m, |V|max {vMax / 1e3:0.#} kN, |uy|max {dMax * 1e3:0.#} mm");
        }
        lines.Add($"Engine {res.EngineVersion}, {r.Elapsed.TotalSeconds:0.#} s, folder {r.Folder}");
        lines.Add("Forces come from a frozen-force gradient (the scripts' method): I is a design proposal, not a code check.");
        return string.Join("\n", lines);
    }
}
