using System.Runtime.InteropServices;

namespace OpenPyStruct.Core.Engine;

/// <summary>
/// Finds the container CLI to shell out to: Podman first (licence-free, rootless), Docker as
/// the fallback. Override with the OPENPYSTRUCT_CONTAINER_CLI environment variable (a full path,
/// or "docker"/"podman"). GUI-launched Rhino on macOS does not see the shell PATH, so the usual
/// install locations are probed explicitly there.
/// <para>Modelled on Eddy3D's ContainerCli (github.com/Eddy3D-Dev/Eddy3D), trimmed to what this
/// plugin needs.</para>
/// </summary>
public static class ContainerCli
{
    public const string OverrideVariable = "OPENPYSTRUCT_CONTAINER_CLI";
    public const string PodmanDownloadUrl = "https://podman-desktop.io/downloads";
    public const string NotFoundMessage =
        "No container engine found. Install Podman (" + PodmanDownloadUrl + ") or Docker Desktop, start it, then re-check.";

    public static bool IsPodman(string? cli) =>
        cli != null && Name(cli).Contains("podman", StringComparison.OrdinalIgnoreCase);

    public static string Name(string? cli)
    {
        if (string.IsNullOrEmpty(cli)) return "docker";
        var leaf = cli.Split('/', '\\')[^1];
        return Path.GetFileNameWithoutExtension(leaf);
    }

    public static string DaemonDownMessage(string? cli) => IsPodman(cli)
        ? "Podman machine is not running. Run 'podman machine start' (or start Podman Desktop)."
        : "Docker daemon is not running. Start Docker Desktop.";

    /// <summary>The CLI actually installed on this machine, or null.</summary>
    public static string? Resolve(string? overrideCli = null)
    {
        var o = (overrideCli ?? Environment.GetEnvironmentVariable(OverrideVariable))?.Trim();
        if (!string.IsNullOrEmpty(o))
        {
            if (o.Contains('/') || o.Contains('\\')) return File.Exists(o) ? o : null;
            return Find(o);
        }
        return Find("podman") ?? Find("docker");
    }

    private static readonly string[] MacBinDirs =
    {
        "/opt/podman/bin", "/opt/homebrew/bin", "/usr/local/bin",
        "/Applications/Docker.app/Contents/Resources/bin",
    };

    private static string? Find(string name)
    {
        var exe = name + (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : "");
        foreach (var dir in WellKnownDirs(name))
        {
            var c = Path.Combine(dir, exe);
            if (File.Exists(c)) return c;
        }
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "")
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var c = Path.Combine(dir.Trim(), exe);
                if (File.Exists(c)) return c;
            }
            catch { /* malformed PATH entry */ }
        }
        return null;
    }

    private static IEnumerable<string> WellKnownDirs(string name)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            foreach (var d in MacBinDirs) yield return d;
            yield break;
        }
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) yield break;
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (name == "docker")
        {
            yield return Path.Combine(pf, "Docker", "Docker", "resources", "bin");
            yield return Path.Combine(pf, "Docker", "Docker", "resources");
        }
        else if (name == "podman")
        {
            yield return Path.Combine(pf, "RedHat", "Podman");
        }
    }
}
