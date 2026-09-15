using System.Runtime.InteropServices;

namespace OpenPyStruct.Core.Engine;

/// <summary>
/// Runs <c>openpystruct</c> with a Python interpreter on this machine instead of in a container.
/// Exists for one reason: a container on macOS cannot reach the Metal GPU, and the host can. The
/// interpreter is either the one the user names, the venv this class installs under the runs root
/// (<c>~/OpenPyStruct/venv</c>), or a python3 on PATH.
/// </summary>
public static class NativeRunner
{
    public const string VenvFolderName = "venv";

    /// <summary>Where <see cref="InstallSteps"/> puts the venv: beside the runs, not in the repo.</summary>
    public static string VenvDir(EngineSettings s) =>
        Path.Combine(Path.GetDirectoryName(s.ResolvedRunsRoot.TrimEnd(Path.DirectorySeparatorChar)) ?? s.ResolvedRunsRoot, VenvFolderName);

    public static string VenvPython(string venvDir) => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? Path.Combine(venvDir, "Scripts", "python.exe")
        : Path.Combine(venvDir, "bin", "python");

    /// <summary>The interpreter to use, or null when none is installed.</summary>
    public static string? ResolvePython(EngineSettings s)
    {
        if (!string.IsNullOrWhiteSpace(s.Python)) return File.Exists(s.Python) ? s.Python : null;
        var venv = VenvPython(VenvDir(s));
        if (File.Exists(venv)) return venv;
        return FindOnPath("python3") ?? FindOnPath("python");
    }

    private static readonly string[] MacBinDirs = { "/opt/homebrew/bin", "/usr/local/bin", "/usr/bin" };

    private static string? FindOnPath(string name)
    {
        var exe = name + (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : "");
        var dirs = new List<string>();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) dirs.AddRange(MacBinDirs);
        dirs.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
        foreach (var d in dirs)
        {
            try { var c = Path.Combine(d.Trim(), exe); if (File.Exists(c)) return c; } catch { /* malformed entry */ }
        }
        return null;
    }

    /// <summary><c>python -m openpystruct.cli run case.json result.json</c>, host paths throughout.</summary>
    public static IReadOnlyList<string> RunArgs(string runFolder) => new[]
    {
        "-m", "openpystruct.cli", "run",
        Path.Combine(runFolder, ContainerRunner.CaseFileName),
        Path.Combine(runFolder, ContainerRunner.ResultFileName),
    };

    public static IReadOnlyList<string> InfoArgs() => new[] { "-m", "openpystruct.cli", "info" };

    /// <summary>
    /// The commands that make a working native engine from a repository checkout: a venv beside the
    /// runs root, torch from the default index (which is the MPS build on macOS and the CUDA build
    /// on Windows/Linux with an NVIDIA driver), then the package itself, editable so a checkout
    /// update is picked up without reinstalling.
    /// </summary>
    public static IReadOnlyList<(string Exe, IReadOnlyList<string> Args)> InstallSteps(string bootstrapPython, string venvDir, string repoDir)
    {
        var py = VenvPython(venvDir);
        return new (string, IReadOnlyList<string>)[]
        {
            (bootstrapPython, new[] { "-m", "venv", venvDir }),
            (py, new[] { "-m", "pip", "install", "--upgrade", "pip" }),
            (py, new[] { "-m", "pip", "install", "torch" }),
            (py, new[] { "-m", "pip", "install", "-e", repoDir }),
        };
    }

    /// <summary>Null when the interpreter answers <c>openpystruct info</c>; otherwise what is wrong.</summary>
    public static string? Readiness(EngineSettings s, out string? python, out string? info)
    {
        info = null;
        python = ResolvePython(s);
        if (python == null)
            return s.Python != null
                ? $"Python not found at {s.Python}."
                : "No Python found. Press Build with Repo set to create a venv, or set Python to an interpreter with openpystruct installed.";
        var probe = ContainerRunner.Execute(python, InfoArgs(), null, null, CancellationToken.None, 2);
        if (probe.ExitCode != 0)
            return $"{python} cannot import openpystruct. Press Build with Repo set, or `pip install -e <repo>` into it.";
        info = probe.Log.Split('\n').LastOrDefault(l => l.TrimStart().StartsWith("{"))?.Trim();
        return null;
    }
}
