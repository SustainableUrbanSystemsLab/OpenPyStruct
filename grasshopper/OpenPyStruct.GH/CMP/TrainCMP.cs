using System;
using System.Drawing;
using System.Linq;
using System.Text.Json.Nodes;
using Grasshopper.Kernel;
using OpenPyStruct.Core.Contract;
using OpenPyStruct.Core.Engine;
using OpenPyStruct.GH.GUI;
using OpenPyStruct.GH.Types;

namespace OpenPyStruct.GH.CMP;

/// <summary>Train an FNN or PINN surrogate on a dataset from Generate Data.</summary>
public class TrainCMP : RunComponentBase
{
    private static readonly string[] Kinds = { "fnn", "pinn" };
    private string _dataset;

    public TrainCMP() : base("Train Surrogate", "Train",
        "Train the residual-MLP surrogate (OpenPyStruct_FNN_MultiCase) or its physics-informed variant "
        + "(OpenPyStruct_PINN_MultiCase, which also learns deflections and rotations) on a dataset from "
        + "Generate Data. Writes model.pt to the run folder; wire that path into Predict. GPU helps here.")
    {
    }

    public override Guid ComponentGuid => new("2B6F1A10-9C3D-4E52-8F71-0A1B2C3D4E13");
    protected override Bitmap Icon => Icons.For("Trn", Icons.RunColor);
    protected override string Task => "train";
    protected override string ProgressLabel => "Training";

    protected override void RegisterInputParams(GH_InputParamManager pm)
    {
        pm.AddTextParameter("Dataset", "Dataset", "Path of dataset.json from Generate Data (or StructDataLite.json).", GH_ParamAccess.item);
        pm.AddParameter(new GH_DropdownParam(Kinds, "Kind", "Kind", "fnn: predicts I. pinn: predicts I, deflections and rotations with a physics-consistency loss.", this, defaultItem: Kinds[0]));
        pm[1].Optional = true;
        pm.AddIntegerParameter("Cases", "Cases", "Load cases grouped per training sample (n_cases). Predict accepts up to this many.", GH_ParamAccess.item, 6);
        pm.AddNumberParameter("c", "c", "Label aggregation: target I = mean + c·std across the grouped cases (higher = more conservative).", GH_ParamAccess.item, 1.0);
        pm.AddIntegerParameter("Hidden", "Hidden", "Hidden units.", GH_ParamAccess.item, 128);
        pm.AddIntegerParameter("Blocks", "Blocks", "Residual blocks.", GH_ParamAccess.item, 3);
        pm.AddNumberParameter("Dropout", "Dropout", "Dropout rate.", GH_ParamAccess.item, 0.5);
        pm.AddIntegerParameter("Epochs", "Epochs", "Maximum epochs.", GH_ParamAccess.item, 500);
        pm.AddIntegerParameter("Batch", "Batch", "Batch size.", GH_ParamAccess.item, 128);
        pm.AddIntegerParameter("Patience", "Patience", "Early-stopping patience on validation loss.", GH_ParamAccess.item, 10);
        pm.AddNumberParameter("Learning rate", "lr", "Adam learning rate.", GH_ParamAccess.item, 2e-4);
        pm.AddNumberParameter("Weight decay", "wd", "L2 regularization.", GH_ParamAccess.item, 1e-2);
        pm.AddIntegerParameter("Seed", "Seed", "Random seed.", GH_ParamAccess.item, 0);
        pm.AddGenericParameter("Engine", "Engine", "Unwired: defaults. Turn GPU on with the CUDA image for large sets.", GH_ParamAccess.item);
        pm[13].Optional = true;
        pm.AddTextParameter("Folder", "Folder", "Run folder; model.pt is written here.", GH_ParamAccess.item);
        pm[14].Optional = true;
        AddRunInput(pm);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pm)
    {
        AddCommonOutputs(pm);
        pm.AddTextParameter("Weights", "Weights", "Path of model.pt, for Predict.", GH_ParamAccess.item);
        pm.AddNumberParameter("Validation loss", "Val", "Validation loss per epoch.", GH_ParamAccess.list);
        pm.AddTextParameter("Summary", "Summary", "Kind, samples, epochs, best validation loss.", GH_ParamAccess.item);
    }

    protected override bool TryBuildCase(IGH_DataAccess da, out CaseDocument doc, out EngineDef engine,
        out string folder, out ModelDef model, out int key)
    {
        doc = null; engine = null; folder = null; model = null; key = 0;
        string dataset = null;
        int cases = 6, hidden = 128, blocks = 3, epochs = 500, batch = 128, patience = 10, seed = 0;
        double c = 1.0, dropout = 0.5, lr = 2e-4, wd = 1e-2;
        if (!da.GetData(0, ref dataset) || string.IsNullOrWhiteSpace(dataset)) { Error("Wire the dataset path."); return false; }
        if (!System.IO.File.Exists(dataset)) { Error($"Dataset not found: {dataset}"); return false; }
        da.GetData(2, ref cases); da.GetData(3, ref c); da.GetData(4, ref hidden); da.GetData(5, ref blocks);
        da.GetData(6, ref dropout); da.GetData(7, ref epochs); da.GetData(8, ref batch); da.GetData(9, ref patience);
        da.GetData(10, ref lr); da.GetData(11, ref wd); da.GetData(12, ref seed);
        da.GetData(13, ref engine); da.GetData(14, ref folder);
        var kind = Params.Input[1] is GH_DropdownParam dd ? dd.SelectedValues.FirstOrDefault() ?? Kinds[0] : Kinds[0];
        if (cases < 1 || hidden < 1 || blocks < 0 || epochs < 1 || batch < 1 || patience < 1) { Error("Cases, Hidden, Epochs, Batch and Patience must be positive."); return false; }

        engine ??= DefaultEngine();
        _dataset = dataset;
        doc = new CaseDocument
        {
            Task = Task,
            TaskParams = new JsonObject
            {
                ["kind"] = kind, ["output"] = "model.pt", ["n_cases"] = cases, ["c"] = c,
                ["hidden_units"] = hidden, ["num_blocks"] = blocks, ["dropout"] = dropout,
                ["epochs"] = epochs, ["batch_size"] = batch, ["patience"] = patience,
                ["learning_rate"] = lr, ["weight_decay"] = wd, ["seed"] = seed,
            },
        };
        key = Key(dataset, kind, cases, c, hidden, blocks, dropout, epochs, batch, patience, lr, wd, seed, engine, folder ?? "");
        return true;
    }

    protected override void Prepare(EngineSettings settings, string runFolder, CaseDocument doc)
    {
        doc.TaskParams["dataset"] = ContainerRunner.ExposeFile(settings, runFolder, _dataset);
    }

    protected override void SetOutputs(IGH_DataAccess da, RunResult r)
    {
        var res = r.Result;
        da.SetData("Weights", System.IO.Path.Combine(r.Folder, "model.pt"));
        da.SetDataList("Validation loss", res.ValLoss ?? Array.Empty<double>());
        da.SetData("Summary",
            $"{(res.ModelKind ?? "fnn").ToUpperInvariant()} surrogate for {res.Nelem} elements, {res.NCases} cases per sample\n"
            + $"Samples: {res.Samples}, epochs: {res.Epochs}, best validation loss: {res.BestValLoss:0.####}\n"
            + $"{r.Elapsed.TotalSeconds:0.#} s, weights: {System.IO.Path.Combine(r.Folder, "model.pt")}");
    }
}
