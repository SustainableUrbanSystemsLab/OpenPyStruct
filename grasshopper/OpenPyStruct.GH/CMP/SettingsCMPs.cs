using System;
using System.Drawing;
using Grasshopper.Kernel;
using OpenPyStruct.Core.Contract;
using OpenPyStruct.GH.GUI;
using OpenPyStruct.GH.Types;

namespace OpenPyStruct.GH.CMP;

/// <summary>Material and section constants of the FE model. Defaults are the scripts' steel.</summary>
public class MaterialCMP : GH_BeautifulComponent
{
    public MaterialCMP() : base("Material", "Material",
        "Material and section constants. Leave unwired for the scripts' steel: E = 200 GPa, ν = 0.3, "
        + "A = 0.01 m², I₀ = 0.5 m⁴ (initial guess), k = 0.03 (shear-area proxy A = k·√I).",
        Strings.Category, Strings.Settings)
    {
        UseParamUI();
    }

    public override Guid ComponentGuid => new("4C701DA5-692B-4C2A-AB58-BACB59024B5C");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap Icon => Icons.For("Mt", Icons.SettingsColor);

    protected override void RegisterInputParams(GH_InputParamManager pm)
    {
        pm.AddNumberParameter("E", "E", "Young's modulus, Pa.", GH_ParamAccess.item, 200e9);
        pm.AddNumberParameter("nu", "ν", "Poisson's ratio (gives G = E / 2(1+ν) for the shear term).", GH_ParamAccess.item, 0.3);
        pm.AddNumberParameter("A", "A", "Cross-sectional area used by the FE model, m². Constant: the optimizer designs I only.", GH_ParamAccess.item, 0.01);
        pm.AddNumberParameter("I0", "I₀", "Starting moment of inertia for every element, m⁴. The beam script uses 0.5, the frame script 5e-4.", GH_ParamAccess.item, 0.5);
        pm.AddNumberParameter("k", "k", "Shear-area proxy: the shear-energy term divides by G·k·√I.", GH_ParamAccess.item, 0.03);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pm)
    {
        pm.AddGenericParameter("Material", "Material", "For the Run components.", GH_ParamAccess.item);
    }

    protected override void Solve(IGH_DataAccess da)
    {
        var m = new Material();
        double v = 0;
        if (da.GetData(0, ref v)) m.E = v;
        if (da.GetData(1, ref v)) m.Nu = v;
        if (da.GetData(2, ref v)) m.A = v;
        if (da.GetData(3, ref v)) m.I0 = v;
        if (da.GetData(4, ref v)) m.K = v;
        if (m.E <= 0 || m.A <= 0 || m.I0 <= 0 || m.K <= 0) { Error("E, A, I₀ and k must be positive."); return; }
        if (m.Nu < 0 || m.Nu >= 0.5) Warning("ν outside [0, 0.5).");
        da.SetData(0, new MaterialDef { Material = m });
        Message = $"E {m.E / 1e9:0.#} GPa";
    }
}

/// <summary>The Adam loop's knobs, shared by Optimize and Generate Data.</summary>
public class OptimizerSettingsCMP : GH_BeautifulComponent
{
    public OptimizerSettingsCMP() : base("Optimizer Settings", "OptSettings",
        "Gradient-descent settings for the moment-of-inertia optimizer (Adam + exponential LR decay, "
        + "early stop). Loss = ΣI + α_M·Σ M²/(2EI) + α_V·Σ V²/(G·k·√I). Defaults are the beam script's.",
        Strings.Category, Strings.Settings)
    {
        UseParamUI();
    }

    public override Guid ComponentGuid => new("00E00C71-1315-486A-873A-78AC82FEE870");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap Icon => Icons.For("Op", Icons.SettingsColor);

    protected override void RegisterInputParams(GH_InputParamManager pm)
    {
        pm.AddIntegerParameter("Epochs", "Epochs", "Maximum optimization epochs (each is one FE solve per load case).", GH_ParamAccess.item, 1000);
        pm.AddNumberParameter("Learning rate", "lr", "Adam learning rate on I (m⁴ per step, roughly).", GH_ParamAccess.item, 0.01);
        pm.AddNumberParameter("Gamma", "γ", "Learning-rate decay per epoch.", GH_ParamAccess.item, 0.98);
        pm.AddNumberParameter("Alpha moment", "α_M", "Weight of the bending-energy term.", GH_ParamAccess.item, 1e-2);
        pm.AddNumberParameter("Alpha shear", "α_V", "Weight of the shear-energy term.", GH_ParamAccess.item, 1e-2);
        pm.AddNumberParameter("Tolerance", "Tol", "Minimum loss improvement that counts as progress.", GH_ParamAccess.item, 1e-2);
        pm.AddIntegerParameter("Patience", "Patience", "Epochs without progress before stopping.", GH_ParamAccess.item, 10);
        pm.AddNumberParameter("I min", "I_min", "Floor applied to I after every step, m⁴.", GH_ParamAccess.item, 1e-8);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pm)
    {
        pm.AddGenericParameter("Settings", "Settings", "For Optimize and Generate Data.", GH_ParamAccess.item);
    }

    protected override void Solve(IGH_DataAccess da)
    {
        var s = new OptimizerSettings();
        int i = 0; double v = 0;
        if (da.GetData(0, ref i)) s.Epochs = i;
        if (da.GetData(1, ref v)) s.LearningRate = v;
        if (da.GetData(2, ref v)) s.Gamma = v;
        if (da.GetData(3, ref v)) s.AlphaMoment = v;
        if (da.GetData(4, ref v)) s.AlphaShear = v;
        if (da.GetData(5, ref v)) s.Tolerance = v;
        if (da.GetData(6, ref i)) s.Patience = i;
        if (da.GetData(7, ref v)) s.IMin = v;
        if (s.Epochs < 1 || s.Patience < 1) { Error("Epochs and Patience must be at least 1."); return; }
        if (s.LearningRate <= 0 || s.Gamma <= 0 || s.Gamma > 1) { Error("Learning rate must be positive and γ in (0, 1]."); return; }
        da.SetData(0, new OptimizerDef { Settings = s });
        Message = $"{s.Epochs} epochs";
    }
}
