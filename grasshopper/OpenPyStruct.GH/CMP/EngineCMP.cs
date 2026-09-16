using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Grasshopper.Kernel;
using OpenPyStruct.Core.Engine;
using OpenPyStruct.GH.GUI;
using OpenPyStruct.GH.Types;

namespace OpenPyStruct.GH.CMP;

/// <summary>
/// Where the Python engine runs: which container CLI and image, how many CPUs, GPU or not. Also
/// the place to BUILD the image from a checkout of the OpenPyStruct repository and to CHECK that
/// the engine is ready, both in the background with the outcome on the banner.
/// </summary>
public class EngineCMP : GH_BeautifulComponent
{
    private static readonly string[] Platforms = { "native", "linux/amd64", "linux/arm64" };
    private static readonly string[] Backends = { "auto", "opensees", "numpy" };
    private static readonly string[] Modes = { "container", "native" };
    private static readonly string[] Devices = { "auto", "mps", "cuda", "cpu" };

    private readonly BackgroundRun _bg;
    private string _status;

    public EngineCMP() : base("Engine", "Engine",
        "The container that runs OpenSees + PyTorch. Podman is found first, then Docker. Leave "
        + "unwired on a Run component for the defaults (image 'openpystruct', native platform, CPU). "
        + "Point Repo at a checkout of OpenPyStruct and press Build to create the image; press Check "
        + "to verify the engine answers.",
        Strings.Category, Strings.Settings)
    {
        _bg = new BackgroundRun(this);
        UseParamUI();
    }

    public override Guid ComponentGuid => new("1CDC9B7F-DE3F-4305-BE94-2EF41FD12830");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap Icon => Icons.For(Name);
    public override string MinWidthText => "image 'openpystruct' not found. Build it.";

    protected override void RegisterInputParams(GH_InputParamManager pm)
    {
        pm.AddTextParameter("Image", "Image", "Image name[:tag]. Use openpystruct:cuda with the CUDA Dockerfile.", GH_ParamAccess.item, EngineSettings.DefaultImage);
        pm.AddTextParameter("CLI", "CLI", "Full path to podman or docker. Unwired: auto-detect (env OPENPYSTRUCT_CONTAINER_CLI overrides).", GH_ParamAccess.item);
        pm[1].Optional = true;
        pm.AddNumberParameter("CPUs", "CPUs", "CPU limit for the container; 0 = no limit.", GH_ParamAccess.item, 0.0);
        pm.AddParameter(new GH_ToggleParam("GPU", "GPU",
            "Expose NVIDIA GPUs to the container (only Train benefits). Needs the CUDA image and an "
            + "NVIDIA host. It does NOTHING on macOS: a container there runs in a Linux VM that cannot "
            + "reach Metal, so training falls back to the CPU whatever this says.", this));
        pm[3].Optional = true;
        pm.AddParameter(new GH_DropdownParam(Platforms, "Platform", "Platform",
            "Container platform. On Apple Silicon choose linux/amd64 to get OpenSeesPy (emulated, slower); "
            + "native uses the numpy FE backend there.", this, defaultItem: Platforms[0]));
        pm[4].Optional = true;
        pm.AddParameter(new GH_DropdownParam(Backends, "FE backend", "Backend",
            "auto: OpenSeesPy when the image has it, else the numpy direct-stiffness solver (same element, same answers).",
            this, defaultItem: Backends[0]));
        pm[5].Optional = true;
        pm.AddTextParameter("Runs folder", "Runs", "Where run folders are created. Unwired: ~/OpenPyStruct/runs "
            + "(under your home on purpose — that is what Podman/Docker share with the VM by default).", GH_ParamAccess.item);
        pm[6].Optional = true;
        pm.AddIntegerParameter("Timeout", "Timeout", "Kill a run after this many minutes; 0 = never.", GH_ParamAccess.item, 0);
        pm.AddTextParameter("Repo", "Repo", "Path to an OpenPyStruct checkout, for Build (docker/Dockerfile in container mode, the package itself in native mode).", GH_ParamAccess.item);
        pm[8].Optional = true;
        pm.AddParameter(new GH_DropdownParam(Modes, "Mode", "Mode",
            "container: the openpystruct image under Podman/Docker — reproducible, CUDA only.\n"
            + "native: a Python on this machine. The ONLY way to a GPU on a Mac: Apple's MPS is reachable "
            + "from the host and never from a container. Build creates the venv; Check reports the device.",
            this, defaultItem: Modes[0]));
        pm[9].Optional = true;
        pm.AddTextParameter("Python", "Python", "Native mode: interpreter to run. Unwired: the venv Build created "
            + "(~/OpenPyStruct/venv), else python3 on PATH.", GH_ParamAccess.item);
        pm[10].Optional = true;
        pm.AddParameter(new GH_DropdownParam(Devices, "Device", "Device",
            "What Train runs on. auto = CUDA, then MPS, then CPU. Only native mode can reach MPS.",
            this, defaultItem: Devices[0]));
        pm[11].Optional = true;
        pm.AddParameter(new GH_ToggleParam("Check", "Check", "Probe the engine: CLI, daemon and image, or the Python and its device.", this));
        pm[12].Optional = true;
        pm.AddParameter(new GH_ToggleParam("Build", "Build", "Container: build the image from Repo. Native: create a venv with torch and the package from Repo. Minutes either way.", this));
        pm[13].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pm)
    {
        pm.AddGenericParameter("Engine", "Engine", "For the Run components.", GH_ParamAccess.item);
        pm.AddTextParameter("Status", "Status", "Last Check/Build outcome and the build log.", GH_ParamAccess.list);
    }

    public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
    {
        base.AppendAdditionalMenuItems(menu);
        var cancel = Menu_AppendItem(menu, "Cancel build/check", (_, _) => _bg.Cancel());
        cancel.Enabled = _bg.Running;
        Menu_AppendItem(menu, "Download Podman…", (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = ContainerCli.PodmanDownloadUrl, UseShellExecute = true }); }
            catch { /* no browser */ }
        });
    }

    protected override void Solve(IGH_DataAccess da)
    {
        var s = new EngineSettings();
        string text = null; double d = 0; int i = 0; var gpu = false; var check = false; var build = false;
        if (da.GetData(0, ref text) && !string.IsNullOrWhiteSpace(text)) s.Image = text.Trim();
        text = null; if (da.GetData(1, ref text) && !string.IsNullOrWhiteSpace(text)) s.Cli = text.Trim();
        if (da.GetData(2, ref d)) s.Cpus = Math.Max(0, d);
        da.GetData(3, ref gpu); s.Gpu = gpu;
        var gpuOnMac = gpu && RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        var platform = Selected(4) ?? Platforms[0];
        s.Platform = platform == "native" ? null : platform;
        var backend = Selected(5) ?? Backends[0];
        s.FeBackend = backend == "auto" ? null : backend;
        text = null; if (da.GetData(6, ref text) && !string.IsNullOrWhiteSpace(text)) s.RunsRoot = text.Trim();
        if (da.GetData(7, ref i)) s.TimeoutMinutes = Math.Max(0, i);
        string repo = null; da.GetData(8, ref repo);
        s.Mode = (Selected(9) ?? Modes[0]) == "native" ? EngineMode.Native : EngineMode.Container;
        text = null; if (da.GetData(10, ref text) && !string.IsNullOrWhiteSpace(text)) s.Python = text.Trim();
        s.Device = Selected(11) ?? Devices[0];
        da.GetData(12, ref check); da.GetData(13, ref build);
        if (gpuOnMac && s.Mode == EngineMode.Container)
            Warning("GPU has no effect on macOS in container mode: the VM has no Metal passthrough. "
                    + "Switch Mode to native and Device to mps.");
        if (s.Mode == EngineMode.Container && s.Device == "mps")
            Warning("MPS is only reachable in native mode; the container will fall back to the CPU.");

        da.SetData(0, new EngineDef { Settings = s });

        if (_bg.Running)
        {
            Message = _bg.Banner ?? "Working…";
            da.SetDataList(1, _bg.Lines);
            return;
        }

        if (build && s.Mode == EngineMode.Native)
        {
            ResetToggle("Build");
            if (string.IsNullOrWhiteSpace(repo) || !File.Exists(Path.Combine(repo, "pyproject.toml")))
            {
                Error("Build needs Repo: a folder containing pyproject.toml (an OpenPyStruct checkout).");
            }
            else
            {
                Launch("Installing", ct =>
                {
                    var bootstrap = NativeRunner.ResolvePython(new EngineSettings { RunsRoot = s.RunsRoot })
                                    ?? throw new InvalidOperationException("No python3 on this machine to create the venv with. Install Python 3.10+ first.");
                    var venv = NativeRunner.VenvDir(s);
                    var log = _bg.NewLog();
                    var steps = NativeRunner.InstallSteps(bootstrap, venv, repo);
                    for (var k = 0; k < steps.Count; k++)
                    {
                        var (exe, args) = steps[k];
                        _bg.SetBanner($"Installing… {k + 1}/{steps.Count}");
                        log("$ " + ContainerRunner.Render(exe, args));
                        var outcome = ContainerRunner.Execute(exe, args, log, null, ct, 0, repo);
                        if (!outcome.Succeeded) throw new InvalidOperationException($"step {k + 1} failed (exit {outcome.ExitCode}); see Status");
                    }
                    var problem = NativeRunner.Readiness(s, out _, out var info);
                    if (problem != null) throw new InvalidOperationException(problem);
                    return $"Installed venv at {venv}: {info}";
                });
                Message = "Installing…";
                return;
            }
        }

        if (build)
        {
            ResetToggle("Build");
            if (string.IsNullOrWhiteSpace(repo) || !File.Exists(Path.Combine(repo, "docker", "Dockerfile")))
            {
                Error("Build needs Repo: a folder containing docker/Dockerfile (an OpenPyStruct checkout).");
            }
            else
            {
                Launch("Building", ct =>
                {
                    var cli = ContainerCli.Resolve(s.Cli) ?? throw new InvalidOperationException(ContainerCli.NotFoundMessage);
                    var dockerfile = Path.Combine(repo, "docker", s.Gpu ? "Dockerfile.cuda" : "Dockerfile");
                    if (!File.Exists(dockerfile)) dockerfile = Path.Combine(repo, "docker", "Dockerfile");
                    var args = ContainerRunner.BuildArgs(s, repo, dockerfile);
                    var log = _bg.NewLog();
                    log("$ " + ContainerRunner.Render(cli, args));
                    var step = 0;
                    var outcome = ContainerRunner.Execute(cli, args, line =>
                    {
                        log(line);
                        if (line.StartsWith("STEP", StringComparison.OrdinalIgnoreCase) || line.StartsWith("#", StringComparison.Ordinal))
                            _bg.SetBanner($"Building… step {++step}");
                    }, null, ct, 0, repo);
                    if (!outcome.Succeeded) throw new InvalidOperationException($"build failed (exit {outcome.ExitCode}); see Status");
                    return $"Built {s.Image} in {BackgroundRun.Time(outcome.Elapsed)}";
                });
                Message = "Building…";
                return;
            }
        }

        if (check)
        {
            ResetToggle("Check");
            Launch("Checking", _ =>
            {
                var log = _bg.NewLog();
                if (s.Mode == EngineMode.Native)
                {
                    var nativeProblem = NativeRunner.Readiness(s, out var python, out var info);
                    log("python: " + (python ?? "(none)"));
                    if (info != null) log(info);
                    if (nativeProblem != null) throw new InvalidOperationException(nativeProblem);
                    return $"Ready: {python} — {info}";
                }
                var problem = ContainerRunner.Readiness(s, out var cli);
                log("cli: " + (cli ?? "(none)"));
                if (problem != null) throw new InvalidOperationException(problem);
                return $"Ready: {ContainerCli.Name(cli)} + {s.Image}";
            });
            Message = "Checking…";
            return;
        }

        if (_status != null)
        {
            Message = _status.Length > 48 ? _status[..48] + "…" : _status;
            if (_status.StartsWith("Error", StringComparison.Ordinal)) Warning(_status);
        }
        else
            Message = s.Mode == EngineMode.Native ? "native" : s.Image;
        da.SetDataList(1, _bg.Lines.Count == 0 && _status != null ? new[] { _status } : _bg.Lines);
    }

    private void Launch(string label, Func<System.Threading.CancellationToken, string> work)
    {
        _status = null;
        string outcome = null;
        _bg.Start(ct => { outcome = work(ct); }, (canceled, error) =>
        {
            _status = canceled ? "Canceled" : error != null ? "Error: " + error.Message : outcome;
            ExpireSolution(true);
        });
        _bg.SetBanner(label + "…");
    }

    private string Selected(int index) =>
        Params.Input.Count > index && Params.Input[index] is GH_DropdownParam dd ? dd.SelectedValues.FirstOrDefault() : null;
}
