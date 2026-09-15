using OpenPyStruct.Core.Contract;

namespace OpenPyStruct.Core.Engine;

/// <summary>
/// The one entry point the Run components call. Dispatches on <see cref="EngineSettings.Mode"/>
/// so a component never knows whether the engine is a container or a host interpreter.
/// </summary>
public static class EngineRunner
{
    /// <summary>Write case.json into a run folder, stamping the device choice. Returns the folder.</summary>
    public static string PrepareRunFolder(EngineSettings s, CaseDocument doc, string? folder = null, string? label = null)
    {
        if (!string.IsNullOrWhiteSpace(s.Device) && s.Device != "auto") doc.Device = s.Device;
        return ContainerRunner.PrepareRunFolder(s, doc, folder, label);
    }

    /// <summary>
    /// The path the ENGINE should use for a host file: a mount path in a container, the host path
    /// itself natively.
    /// </summary>
    public static string ExposeFile(EngineSettings s, string runFolder, string hostFile) =>
        s.Mode == EngineMode.Native ? Path.GetFullPath(hostFile) : ContainerRunner.ExposeFile(s, runFolder, hostFile);

    public static (RunOutcome Outcome, ResultDocument? Result) RunCase(EngineSettings s, string runFolder,
        Action<string>? log, Action<ProgressLine>? progress, CancellationToken ct)
    {
        if (s.Mode != EngineMode.Native) return ContainerRunner.RunCase(s, runFolder, log, progress, ct);

        var python = NativeRunner.ResolvePython(s)
                     ?? throw new InvalidOperationException("No Python found for native mode. Press Build on the Engine component, or set Python.");
        var args = NativeRunner.RunArgs(runFolder);
        log?.Invoke("$ " + ContainerRunner.Render(python, args));
        var outcome = ContainerRunner.Execute(python, args, log, progress, ct, s.TimeoutMinutes, runFolder);
        var resultPath = Path.Combine(runFolder, ContainerRunner.ResultFileName);
        ResultDocument? result = null;
        if (File.Exists(resultPath))
        {
            try { result = ResultDocument.Load(resultPath); }
            catch (Exception ex) { log?.Invoke("result.json unreadable: " + ex.Message); }
        }
        return (outcome, result);
    }

    /// <summary>A result path as the engine wrote it, mapped back to the host.</summary>
    public static string? ToHostPath(EngineSettings s, string runFolder, string? enginePath)
    {
        if (enginePath == null) return null;
        if (s.Mode != EngineMode.Native && enginePath.StartsWith(ContainerRunner.ContainerWorkDir))
            return Path.Combine(runFolder, enginePath.Substring(ContainerRunner.ContainerWorkDir.Length).TrimStart('/'));
        return enginePath;
    }
}
