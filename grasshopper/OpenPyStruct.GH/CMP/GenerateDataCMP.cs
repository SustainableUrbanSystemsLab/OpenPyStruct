using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.Json.Nodes;
using Grasshopper.Kernel;
using OpenPyStruct.Core.Contract;
using OpenPyStruct.GH.GUI;
using OpenPyStruct.GH.Types;

namespace OpenPyStruct.GH.CMP;

/// <summary>Training-data generation: many random beam load cases, each optimized, into one JSON dataset.</summary>
public class GenerateDataCMP : RunComponentBase
{
    public GenerateDataCMP() : base("Generate Data", "GenData",
        "Build a training set for the surrogates (OpenPyStruct_BeamOpt_training_MultiCore): N random "
        + "point-load cases on a beam, each optimized, written as dataset.json in the run folder. "
        + "Thousands of samples take hours — start with a few hundred to check the pipeline.")
    {
    }

    public override Guid ComponentGuid => new("28867662-A2C1-4554-8F81-A6A96002FB67");
    protected override Bitmap Icon => Icons.For(Name);
    protected override string Task => "generate_data";
    protected override string ProgressLabel => "Generating";

    protected override void RegisterInputParams(GH_InputParamManager pm)
    {
        pm.AddIntegerParameter("Samples", "Samples", "Number of samples. The paper's sets are 10⁴–10⁵.", GH_ParamAccess.item, 500);
        pm.AddIntegerParameter("Elements", "N", "Elements per beam — Predict will only accept beams with this count.", GH_ParamAccess.item, 100);
        pm.AddNumberParameter("Length", "L", "Beam length, m (the maximum when geometry is randomized).", GH_ParamAccess.item, 200.0);
        pm.AddNumberParameter("Rollers", "Rollers", "Roller x-positions, m (a pin sits at x = 0). Ignored when randomized.",
            GH_ParamAccess.list, new List<double> { 18, 58, 138, 168, 198 });
        pm.AddParameter(new GH_ToggleParam("Randomize", "Randomize",
            "Random length in [15 m, L] and 1..Max rollers per sample, instead of the fixed layout.", this));
        pm[4].Optional = true;
        pm.AddIntegerParameter("Max rollers", "MaxR", "Upper bound on rollers when randomized.", GH_ParamAccess.item, 4);
        pm.AddIntegerParameter("Max forces", "MaxF", "1..this many point loads per sample.", GH_ParamAccess.item, 4);
        pm.AddIntervalParameter("Force range", "F", "Point-load magnitude range, N (negative = down).",
            GH_ParamAccess.item, new Rhino.Geometry.Interval(-355857, -35585.7));
        pm.AddNumberParameter("UDL", "UDL", "Uniform load on every element, N/m.", GH_ParamAccess.item, -1000.0);
        pm.AddIntegerParameter("Workers", "Workers", "Parallel processes inside the container.", GH_ParamAccess.item, 4);
        pm.AddIntegerParameter("Seed", "Seed", "Random seed.", GH_ParamAccess.item, 0);
        pm.AddGenericParameter("Material", "Material", "Unwired: defaults.", GH_ParamAccess.item);
        pm[11].Optional = true;
        pm.AddGenericParameter("Settings", "Settings", "Optimizer settings per sample. Unwired: the training script's "
            + "(600 epochs, tolerance 5e-3, patience 5).", GH_ParamAccess.item);
        pm[12].Optional = true;
        pm.AddGenericParameter("Engine", "Engine", "Unwired: defaults.", GH_ParamAccess.item);
        pm[13].Optional = true;
        pm.AddTextParameter("Folder", "Folder", "Run folder; dataset.json is written here.", GH_ParamAccess.item);
        pm[14].Optional = true;
        AddRunInput(pm);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pm)
    {
        AddCommonOutputs(pm);
        pm.AddTextParameter("Dataset", "Dataset", "Path of dataset.json, for Train.", GH_ParamAccess.item);
        pm.AddIntegerParameter("Count", "Count", "Samples actually written (failed analyses are skipped).", GH_ParamAccess.item);
    }

    protected override bool TryBuildCase(IGH_DataAccess da, out CaseDocument doc, out EngineDef engine,
        out string folder, out ModelDef model, out int key)
    {
        doc = null; engine = null; folder = null; model = null; key = 0;
        int samples = 500, n = 100, maxR = 4, maxF = 4, workers = 4, seed = 0;
        double length = 200, udl = -1000;
        var rollers = new List<double>();
        var randomize = false;
        var range = new Rhino.Geometry.Interval(-355857, -35585.7);
        MaterialDef material = null; OptimizerDef settings = null;
        da.GetData(0, ref samples); da.GetData(1, ref n); da.GetData(2, ref length); da.GetDataList(3, rollers);
        da.GetData(4, ref randomize); da.GetData(5, ref maxR); da.GetData(6, ref maxF); da.GetData(7, ref range);
        da.GetData(8, ref udl); da.GetData(9, ref workers); da.GetData(10, ref seed);
        da.GetData(11, ref material); da.GetData(12, ref settings); da.GetData(13, ref engine); da.GetData(14, ref folder);

        if (samples < 1 || n < 2 || length <= 0 || maxF < 1 || maxR < 1 || workers < 1) { Error("Samples, Elements (≥2), Length, Max forces, Max rollers and Workers must be positive."); return false; }
        if (!randomize && rollers.Count == 0) Warning("No rollers: every sample is a cantilever off the pin at x = 0 — unstable. Add rollers or randomize.");
        if (rollers.Any(r => r < 0 || r > length)) { Error("Rollers must lie in [0, Length]."); return false; }

        engine ??= DefaultEngine();
        var p = new JsonObject
        {
            ["num_samples"] = samples, ["workers"] = workers, ["seed"] = seed, ["length"] = length,
            ["n_elements"] = n, ["randomize_geometry"] = randomize, ["length_min"] = Math.Min(15.0, length),
            ["roller_x"] = new JsonArray(rollers.Select(r => (JsonNode)r).ToArray()),
            ["max_rollers"] = maxR, ["max_forces"] = maxF, ["min_force"] = range.T1, ["max_force"] = range.T0,
            ["udl"] = udl, ["output"] = "dataset.json",
        };
        doc = new CaseDocument
        {
            Task = Task,
            Material = material?.Material ?? new Material(),
            Optimizer = settings?.Settings ?? new OptimizerSettings { Epochs = 600, Tolerance = 5e-3, Patience = 5 },
            TaskParams = p,
        };
        key = Key(samples, n, length, string.Join(",", rollers), randomize, maxR, maxF, range.T0, range.T1, udl, workers, seed, material, settings, engine, folder ?? "");
        return true;
    }

    protected override void SetOutputs(IGH_DataAccess da, RunResult r)
    {
        // A container wrote /work/dataset.json; natively the path is already the host's.
        da.SetData("Dataset", Core.Engine.EngineRunner.ToHostPath(r.Engine, r.Folder, r.Result.DatasetPath));
        da.SetData("Count", r.Result.Samples ?? 0);
    }
}
