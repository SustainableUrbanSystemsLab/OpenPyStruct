using System;
using System.Drawing;
using System.Linq;
using Grasshopper.Kernel;
using OpenPyStruct.Core.Contract;
using OpenPyStruct.GH.GUI;
using OpenPyStruct.GH.Types;

namespace OpenPyStruct.GH.CMP;

/// <summary>Material and section constants of the FE model. Defaults are the scripts' steel.</summary>
public class MaterialCMP : GH_BeautifulComponent
{
    /// <summary>Name -> (E Pa, nu). Wired E / nu override the preset.</summary>
    private static readonly (string Name, double E, double Nu)[] Presets =
    {
        ("Steel (scripts, 200 GPa)", 200e9, 0.30),
        ("Steel S355 (210 GPa)", 210e9, 0.30),
        ("Aluminium 6061 (69 GPa)", 69e9, 0.33),
        ("Concrete C30/37 (33 GPa)", 33e9, 0.20),
        ("Glulam GL24h (11.5 GPa)", 11.5e9, 0.40),
        ("Custom", double.NaN, double.NaN),
    };

    public MaterialCMP() : base("Material", "Material",
        "Material and section constants. Leave unwired for the scripts' steel: E = 200 GPa, ν = 0.3, "
        + "A = 0.01 m², I₀ = 0.5 m⁴ (initial guess), k = 0.03 (shear-area proxy A = k·√I).",
        Strings.Category, Strings.Settings)
    {
        UseParamUI();
    }

    public override Guid ComponentGuid => new("4C701DA5-692B-4C2A-AB58-BACB59024B5C");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap Icon => Icons.For(Name);

    protected override void RegisterInputParams(GH_InputParamManager pm)
    {
        pm.AddParameter(new GH_DropdownParam(Presets.Select(p => p.Name), "Preset", "Preset",
            "Sets E and ν. Wire E or ν to override a preset value; choose Custom when both are wired.",
            this, defaultItem: Presets[0].Name));
        pm[0].Optional = true;
        pm.AddNumberParameter("E", "E", "Young's modulus, Pa. Unwired: the preset's.", GH_ParamAccess.item);
        pm[1].Optional = true;
        pm.AddNumberParameter("nu", "ν", "Poisson's ratio (gives G = E / 2(1+ν) for the shear term). Unwired: the preset's.", GH_ParamAccess.item);
        pm[2].Optional = true;
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
        var preset = Presets.FirstOrDefault(p => p.Name == this.Selected(0, Presets[0].Name));
        if (preset.Name == null) preset = Presets[0];
        if (!double.IsNaN(preset.E)) { m.E = preset.E; m.Nu = preset.Nu; }
        double v = 0;
        var eWired = da.GetData(1, ref v); if (eWired) m.E = v;
        var nuWired = da.GetData(2, ref v); if (nuWired) m.Nu = v;
        if (preset.Name == "Custom" && !(eWired && nuWired)) Warning("Custom preset: wire both E and ν (defaults are the scripts' steel).");
        if (da.GetData(3, ref v)) m.A = v;
        if (da.GetData(4, ref v)) m.I0 = v;
        if (da.GetData(5, ref v)) m.K = v;
        if (m.E <= 0 || m.A <= 0 || m.I0 <= 0 || m.K <= 0) { Error("E, A, I₀ and k must be positive."); return; }
        if (m.Nu < 0 || m.Nu >= 0.5) Warning("ν outside [0, 0.5).");
        da.SetData(0, new MaterialDef { Material = m });
        Message = $"{(preset.Name == "Custom" ? "custom" : preset.Name.Split(' ')[0])}, E {m.E / 1e9:0.#} GPa";
    }
}

/// <summary>The Adam loop's knobs, shared by Optimize and Generate Data.</summary>
public class OptimizerSettingsCMP : GH_BeautifulComponent
{
    /// <summary>The three scripts' parameter sets. Wired inputs override the preset's values.</summary>
    private static readonly (string Name, OptimizerSettings S)[] Presets =
    {
        ("Beam (BeamOpt)", new OptimizerSettings()),
        ("Frame (FrameOpt)", new OptimizerSettings { Epochs = 5000, LearningRate = 0.005, Gamma = 1.0, Tolerance = 1e-3, Patience = 10 }),
        ("Training data (MultiCore)", new OptimizerSettings { Epochs = 600, LearningRate = 0.01, Gamma = 0.98, Tolerance = 5e-3, Patience = 5 }),
    };
    private static readonly string[] Combinations = { "Sum of cases", "Envelope per element" };

    public OptimizerSettingsCMP() : base("Optimizer Settings", "OptSettings",
        "Gradient-descent settings for the moment-of-inertia optimizer (Adam + exponential LR decay, "
        + "early stop). Loss = ΣI + α_M·Σ M²/(2EI) + α_V·Σ V²/(G·k·√I). Defaults are the beam script's.",
        Strings.Category, Strings.Settings)
    {
        UseParamUI();
    }

    public override Guid ComponentGuid => new("00E00C71-1315-486A-873A-78AC82FEE870");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap Icon => Icons.For(Name);

    protected override void RegisterInputParams(GH_InputParamManager pm)
    {
        pm.AddParameter(new GH_DropdownParam(Presets.Select(p => p.Name), "Preset", "Preset",
            "Which script's settings to start from. Beam: 1000 epochs, lr 0.01, γ 0.98, tol 1e-2, patience 10. "
            + "Frame: 5000 epochs, lr 0.005, no decay, tol 1e-3. Training data: 600 epochs, tol 5e-3, patience 5. "
            + "Any wired input overrides the preset's value.", this, defaultItem: Presets[0].Name));
        pm[0].Optional = true;
        pm.AddParameter(new GH_DropdownParam(Combinations, "Combination", "Combine",
            "Several load cases: Sum adds every case's bending and shear energy (the scripts' reading). "
            + "Envelope takes, per element, the worst case's energies, so each element is designed for whichever "
            + "case hurts it most — usually less material than the sum.", this, defaultItem: Combinations[0]));
        pm[1].Optional = true;
        pm.AddIntegerParameter("Epochs", "Epochs", "Maximum optimization epochs (each is one FE solve per load case).", GH_ParamAccess.item);
        pm[2].Optional = true;
        pm.AddNumberParameter("Learning rate", "lr", "Adam learning rate on I (m⁴ per step, roughly).", GH_ParamAccess.item);
        pm[3].Optional = true;
        pm.AddNumberParameter("Gamma", "γ", "Learning-rate decay per epoch (1 = none).", GH_ParamAccess.item);
        pm[4].Optional = true;
        pm.AddNumberParameter("Alpha moment", "α_M", "Weight of the bending-energy term.", GH_ParamAccess.item);
        pm[5].Optional = true;
        pm.AddNumberParameter("Alpha shear", "α_V", "Weight of the shear-energy term.", GH_ParamAccess.item);
        pm[6].Optional = true;
        pm.AddNumberParameter("Tolerance", "Tol", "Minimum loss improvement that counts as progress.", GH_ParamAccess.item);
        pm[7].Optional = true;
        pm.AddIntegerParameter("Patience", "Patience", "Epochs without progress before stopping.", GH_ParamAccess.item);
        pm[8].Optional = true;
        pm.AddNumberParameter("I min", "I_min", "Floor applied to I after every step, m⁴.", GH_ParamAccess.item);
        pm[9].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pm)
    {
        pm.AddGenericParameter("Settings", "Settings", "For Optimize and Generate Data.", GH_ParamAccess.item);
    }

    protected override void Solve(IGH_DataAccess da)
    {
        var presetName = this.Selected(0, Presets[0].Name);
        var basis = Presets.FirstOrDefault(p => p.Name == presetName).S ?? Presets[0].S;
        var s = new OptimizerSettings
        {
            Epochs = basis.Epochs, LearningRate = basis.LearningRate, Gamma = basis.Gamma, AlphaMoment = basis.AlphaMoment,
            AlphaShear = basis.AlphaShear, Tolerance = basis.Tolerance, Patience = basis.Patience, IMin = basis.IMin,
            Combination = this.Selected(1, Combinations[0]) == Combinations[1] ? "envelope" : "sum",
        };
        int i = 0; double v = 0;
        if (da.GetData(2, ref i)) s.Epochs = i;
        if (da.GetData(3, ref v)) s.LearningRate = v;
        if (da.GetData(4, ref v)) s.Gamma = v;
        if (da.GetData(5, ref v)) s.AlphaMoment = v;
        if (da.GetData(6, ref v)) s.AlphaShear = v;
        if (da.GetData(7, ref v)) s.Tolerance = v;
        if (da.GetData(8, ref i)) s.Patience = i;
        if (da.GetData(9, ref v)) s.IMin = v;
        if (s.Epochs < 1 || s.Patience < 1) { Error("Epochs and Patience must be at least 1."); return; }
        if (s.LearningRate <= 0 || s.Gamma <= 0 || s.Gamma > 1) { Error("Learning rate must be positive and γ in (0, 1]."); return; }
        da.SetData(0, new OptimizerDef { Settings = s });
        Message = $"{presetName.Split(' ')[0]}: {s.Epochs} epochs, {s.Combination}";
    }
}
