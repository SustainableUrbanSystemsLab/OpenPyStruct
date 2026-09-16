using System.Diagnostics;
using System.Globalization;
using System.Text;
using OpenPyStruct.Core.Contract;

namespace OpenPyStruct.Core.Engine;

public sealed record RunOutcome(int ExitCode, string Log, TimeSpan Elapsed, bool TimedOut)
{
    public bool Succeeded => ExitCode == 0 && !TimedOut;
}

/// <summary>
/// Runs <c>openpystruct</c> inside the engine image with a host folder mounted at /work. Command
/// lines are assembled by pure functions (unit-tested) and executed by <see cref="Execute"/>,
/// which streams stdout to a log sink and progress lines to a progress sink.
/// </summary>
public static class ContainerRunner
{
    public const string CaseFileName = "case.json";
    public const string ResultFileName = "result.json";
    public const string ContainerWorkDir = "/work";
    /// <summary>Where a side-loaded input (model bundle, dataset) is mounted, read-only.</summary>
    public const string ContainerInputDir = "/input";

    /// <summary>
    /// Make a host FILE reachable inside the container: mounts its folder at /input (or, when
    /// it already sits inside the run folder, nothing) and returns the container-side path.
    /// </summary>
    public static string ExposeFile(EngineSettings s, string runFolder, string hostFile)
    {
        var full = Path.GetFullPath(hostFile);
        var run = Path.GetFullPath(runFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var dir = Path.GetDirectoryName(full) ?? run;
        if (string.Equals(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), run,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            return $"{ContainerWorkDir}/{Path.GetFileName(full)}";
        var existing = s.ExtraMounts.FirstOrDefault(m => m.HostDir == dir);
        if (existing == null)
        {
            var target = s.ExtraMounts.Count == 0 ? ContainerInputDir : $"{ContainerInputDir}{s.ExtraMounts.Count + 1}";
            existing = new EngineSettings.Mount(dir, target);
            s.ExtraMounts.Add(existing);
        }
        return $"{existing.ContainerDir}/{Path.GetFileName(full)}";
    }

    /// <summary>Arguments for <c>cli run ... image run /work/case.json /work/result.json</c>.</summary>
    public static IReadOnlyList<string> RunArgs(EngineSettings s, string hostWorkDir)
    {
        var args = new List<string> { "run", "--rm" };
        if (s.Cpus > 0) { args.Add("--cpus"); args.Add(s.Cpus.ToString(CultureInfo.InvariantCulture)); }
        if (!string.IsNullOrWhiteSpace(s.Platform)) { args.Add("--platform"); args.Add(s.Platform!); }
        if (s.Gpu)
        {
            if (ContainerCli.IsPodman(s.Cli)) { args.Add("--device"); args.Add("nvidia.com/gpu=all"); }
            else { args.Add("--gpus"); args.Add("all"); }
        }
        args.Add("-v");
        args.Add($"{hostWorkDir}:{ContainerWorkDir}");
        foreach (var m in s.ExtraMounts) { args.Add("-v"); args.Add(m.Spec); }
        args.AddRange(s.ExtraArgs);
        args.Add(s.Image);
        args.Add("run");
        args.Add($"{ContainerWorkDir}/{CaseFileName}");
        args.Add($"{ContainerWorkDir}/{ResultFileName}");
        return args;
    }

    /// <summary>Arguments for <c>cli build -t image -f dockerfile context</c>.</summary>
    public static IReadOnlyList<string> BuildArgs(EngineSettings s, string contextDir, string dockerfile)
    {
        var args = new List<string> { "build", "-t", s.Image, "-f", dockerfile };
        if (!string.IsNullOrWhiteSpace(s.Platform)) { args.Add("--platform"); args.Add(s.Platform!); }
        args.Add(contextDir);
        return args;
    }

    /// <summary>Arguments for <c>cli image inspect image</c> — a cheap "is it built?" probe.</summary>
    public static IReadOnlyList<string> InspectImageArgs(EngineSettings s) =>
        new[] { "image", "inspect", "--format", "{{.Id}}", s.Image };

    /// <summary>A copy-pasteable rendering of a command line.</summary>
    public static string Render(string cli, IReadOnlyList<string> args)
    {
        var sb = new StringBuilder(Quote(cli));
        foreach (var a in args) sb.Append(' ').Append(Quote(a));
        return sb.ToString();
    }

    private static string Quote(string a) =>
        a.Length > 0 && a.All(c => char.IsLetterOrDigit(c) || "-_./:=,{}".Contains(c)) ? a : "\"" + a.Replace("\"", "\\\"") + "\"";

    /// <summary>Write case.json into a fresh run folder. Returns the folder.</summary>
    public static string PrepareRunFolder(EngineSettings s, CaseDocument doc, string? folder = null, string? label = null)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var name = string.IsNullOrWhiteSpace(label) ? stamp : $"{stamp}-{Sanitize(label!)}";
            folder = Path.Combine(s.ResolvedRunsRoot, name);
        }
        Directory.CreateDirectory(folder);
        if (!string.IsNullOrWhiteSpace(s.FeBackend)) doc.FeBackend = s.FeBackend;
        File.WriteAllText(Path.Combine(folder, CaseFileName), doc.ToJson());
        var stale = Path.Combine(folder, ResultFileName);
        if (File.Exists(stale)) File.Delete(stale);
        return folder;
    }

    private static string Sanitize(string s) =>
        new(s.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());

    /// <summary>
    /// Run a case end to end: resolve the CLI, launch, stream, then parse result.json. Throws
    /// with a readable message when the CLI is missing; returns a failed outcome otherwise.
    /// </summary>
    public static (RunOutcome Outcome, ResultDocument? Result) RunCase(EngineSettings s, string runFolder,
        Action<string>? log, Action<ProgressLine>? progress, CancellationToken ct)
    {
        var cli = ContainerCli.Resolve(s.Cli) ?? throw new InvalidOperationException(ContainerCli.NotFoundMessage);
        var args = RunArgs(s, runFolder);
        log?.Invoke("$ " + Render(cli, args));
        var outcome = Execute(cli, args, log, progress, ct, s.TimeoutMinutes);
        var resultPath = Path.Combine(runFolder, ResultFileName);
        ResultDocument? result = null;
        if (File.Exists(resultPath))
        {
            try { result = ResultDocument.Load(resultPath); }
            catch (Exception ex) { log?.Invoke("result.json unreadable: " + ex.Message); }
        }
        return (outcome, result);
    }

    /// <summary>Launch the CLI and stream its output until it exits, is cancelled, or times out.</summary>
    public static RunOutcome Execute(string cli, IReadOnlyList<string> args, Action<string>? log,
        Action<ProgressLine>? progress, CancellationToken ct, int timeoutMinutes = 0,
        string? workingDirectory = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = cli,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        // GUI-launched Rhino on macOS has a minimal PATH; make sure the CLI's siblings (gvproxy,
        // vfkit, docker-credential-*) are reachable.
        var dir = Path.GetDirectoryName(cli);
        if (!string.IsNullOrEmpty(dir))
            psi.Environment["PATH"] = dir + Path.PathSeparator + (Environment.GetEnvironmentVariable("PATH") ?? "");

        var sb = new StringBuilder();
        var clock = Stopwatch.StartNew();
        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        void OnLine(string? line)
        {
            if (line == null) return;
            lock (sb) sb.AppendLine(line);
            if (ProgressLine.TryParse(line, out var p)) progress?.Invoke(p);
            else log?.Invoke(line);
        }
        proc.OutputDataReceived += (_, e) => OnLine(e.Data);
        proc.ErrorDataReceived += (_, e) => OnLine(e.Data);
        try
        {
            proc.Start();
        }
        catch (Exception ex)
        {
            return new RunOutcome(-1, $"could not start {cli}: {ex.Message}", clock.Elapsed, false);
        }
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        var timedOut = false;
        var deadline = timeoutMinutes > 0 ? TimeSpan.FromMinutes(timeoutMinutes) : Timeout.InfiniteTimeSpan;
        while (!proc.WaitForExit(250))
        {
            if (ct.IsCancellationRequested || (deadline != Timeout.InfiniteTimeSpan && clock.Elapsed > deadline))
            {
                timedOut = !ct.IsCancellationRequested;
                try { proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
                proc.WaitForExit(5000);
                break;
            }
        }
        proc.WaitForExit();
        clock.Stop();
        var code = ct.IsCancellationRequested ? -2 : (timedOut ? -3 : proc.ExitCode);
        string logText; lock (sb) logText = sb.ToString();
        return new RunOutcome(code, logText, clock.Elapsed, timedOut);
    }

    /// <summary>Probe whether the daemon answers and the image exists. Returns null when fine, else the problem.</summary>
    public static string? Readiness(EngineSettings s, out string? cli)
    {
        cli = ContainerCli.Resolve(s.Cli);
        if (cli == null) return ContainerCli.NotFoundMessage;
        var info = Execute(cli, new[] { "info" }, null, null, CancellationToken.None, 1);
        if (info.ExitCode != 0) return ContainerCli.DaemonDownMessage(cli);
        var image = Execute(cli, InspectImageArgs(s), null, null, CancellationToken.None, 1);
        if (image.ExitCode != 0)
            return $"image '{s.Image}' not found. Build it: {ContainerCli.Name(cli)} build -t {s.Image} -f docker/Dockerfile <OpenPyStruct repo>";
        return null;
    }
}
