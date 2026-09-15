namespace OpenPyStruct.Core.Engine;

/// <summary>How and where the Python engine runs. One object, wired from the Engine component.</summary>
public enum EngineMode
{
    /// <summary>The openpystruct image under Podman or Docker. Reproducible; CUDA only.</summary>
    Container,
    /// <summary>A Python interpreter on this machine with the package installed. The only way to a
    /// GPU on macOS: MPS is reachable from the host and never from a container.</summary>
    Native,
}

public sealed class EngineSettings
{
    public const string DefaultImage = "openpystruct";

    public EngineMode Mode { get; set; } = EngineMode.Container;

    /// <summary>Native mode: the interpreter to run, or null for the auto-detected venv/python3.</summary>
    public string? Python { get; set; }

    /// <summary>"auto", "cuda", "mps" or "cpu": what training runs on. Forwarded as the case's device.</summary>
    public string Device { get; set; } = "auto";

    /// <summary>Image name[:tag]. Local builds are found by bare name on both CLIs.</summary>
    public string Image { get; set; } = DefaultImage;

    /// <summary>Full path to podman/docker, or null to auto-detect.</summary>
    public string? Cli { get; set; }

    /// <summary>CPU limit passed as --cpus; 0 = no limit.</summary>
    public double Cpus { get; set; } = 0;

    /// <summary>Expose NVIDIA GPUs (--gpus all / --device nvidia.com/gpu=all).</summary>
    public bool Gpu { get; set; }

    /// <summary>--platform, e.g. "linux/amd64" on Apple Silicon for the OpenSeesPy wheel. Null = native.</summary>
    public string? Platform { get; set; }

    /// <summary>Extra arguments spliced in before the image name (advanced).</summary>
    public List<string> ExtraArgs { get; set; } = new();

    /// <summary>
    /// Additional host folders to mount, e.g. the folder holding a trained model or a dataset
    /// that lives outside the run folder. The container sees ONLY what is mounted.
    /// </summary>
    public List<Mount> ExtraMounts { get; set; } = new();

    public EngineSettings Clone() => new()
    {
        Image = Image, Cli = Cli, Cpus = Cpus, Gpu = Gpu, Platform = Platform,
        Mode = Mode, Python = Python, Device = Device,
        ExtraArgs = new List<string>(ExtraArgs), ExtraMounts = new List<Mount>(ExtraMounts),
        FeBackend = FeBackend, TimeoutMinutes = TimeoutMinutes, RunsRoot = RunsRoot,
    };

    /// <summary>"opensees", "numpy" or null: forwarded as the case's fe_backend.</summary>
    public string? FeBackend { get; set; }

    /// <summary>Kill the run after this long; 0 = never.</summary>
    public int TimeoutMinutes { get; set; } = 0;

    /// <summary>Root for run folders when a component gets no Folder input.</summary>
    public string? RunsRoot { get; set; }

    public string ResolvedRunsRoot => string.IsNullOrWhiteSpace(RunsRoot) ? DefaultRunsRoot() : RunsRoot!;

    /// <summary>
    /// Under the user's home on purpose: Podman machine on macOS shares only $HOME with the VM,
    /// and Docker Desktop's file sharing defaults are the same, so a system temp folder would
    /// mount as empty.
    /// </summary>
    public static string DefaultRunsRoot() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OpenPyStruct", "runs");

    /// <summary>A host folder mounted at a container path.</summary>
    public sealed record Mount(string HostDir, string ContainerDir, bool ReadOnly = true)
    {
        public string Spec => $"{HostDir}:{ContainerDir}" + (ReadOnly ? ":ro" : "");
    }

    public override string ToString() => Mode == EngineMode.Native
        ? $"OpenPyStruct engine: native python {Python ?? "(auto)"}, device {Device}"
        : $"OpenPyStruct engine: image {Image}" + (Gpu ? ", GPU" : "") + (Cpus > 0 ? $", {Cpus} cpus" : "")
        + (Platform != null ? $", {Platform}" : "");
}
