using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Grasshopper.Kernel;
using OpenPyStruct.Core.Contract;
using OpenPyStruct.Core.Engine;
using OpenPyStruct.GH.GUI;
using OpenPyStruct.GH.Types;

namespace OpenPyStruct.GH.CMP;

/// <summary>
/// The shape every Run component shares: build a case.json from the inputs, launch the container
/// on a background task, drive the banner from the engine's PROGRESS lines, and deliver the
/// result on the next solution. Never blocks the canvas; Cancel and Re-run live on the
/// right-click menu (a Grasshopper button sends true-then-false, so cancel-on-false would kill
/// every button-started run).
/// <para>
/// Run is the LAST input on every component and fires ONCE per true — an edge latch, cleared
/// when Run is seen false again or when the inputs change, so a wired toggle held true relaunches
/// as soon as upstream changes. Ported from Eddy3D's StormwaterRunCMP.
/// </para>
/// </summary>
public abstract class RunComponentBase : GH_BeautifulComponent
{
    protected readonly BackgroundRun Background;
    private RunResult _result;
    private string _error;
    private bool _canceled;
    private string _folder;
    private bool _runConsumed;
    private int _lastInputsKey;
    private bool _forceRun;

    protected RunComponentBase(string name, string nickname, string description)
        : base(name, nickname, description, Strings.Category, Strings.Run)
    {
        Background = new BackgroundRun(this);
        UseParamUI();
    }

    public override GH_Exposure Exposure => GH_Exposure.primary;
    public override string MinWidthText => GUI.ProgressBar.Widest(ProgressLabel);

    /// <summary>The engine task name: optimize / predict / generate_data / train.</summary>
    protected abstract string Task { get; }

    /// <summary>What the banner says while running, e.g. "Optimizing".</summary>
    protected abstract string ProgressLabel { get; }

    /// <summary>
    /// Read the inputs and assemble the case. Return false after calling Error() when they are
    /// not usable. <paramref name="key"/> must change whenever a re-run is warranted.
    /// </summary>
    protected abstract bool TryBuildCase(IGH_DataAccess da, out CaseDocument doc, out EngineDef engine,
        out string folder, out ModelDef model, out int key);

    /// <summary>Set the task-specific outputs from a finished run (Result/Log/Folder are set here).</summary>
    protected abstract void SetOutputs(IGH_DataAccess da, RunResult result);

    /// <summary>Called on the background thread before launch to side-load files (mount extra folders).</summary>
    protected virtual void Prepare(EngineSettings settings, string runFolder, CaseDocument doc) { }

    protected void AddRunInput(GH_InputParamManager pm)
    {
        pm.AddParameter(new GH_ToggleParam("Run", "Run",
            "Launch the engine. Works as an inline button or as a wired toggle — held true, the run "
            + "relaunches whenever the inputs change.", this));
        pm[pm.ParamCount - 1].Optional = true;
    }

    protected void AddCommonOutputs(GH_OutputParamManager pm)
    {
        pm.AddGenericParameter("Result", "Result", "The whole run as ONE item, for Deconstruct Result and Visualize Result.", GH_ParamAccess.item);
        pm.AddTextParameter("Log", "Log", "The engine's output, line by line.", GH_ParamAccess.list);
        pm.AddTextParameter("Folder", "Folder", "The run folder holding case.json and result.json.", GH_ParamAccess.item);
    }

    protected static bool ReadRun(IGH_DataAccess da, int index)
    {
        var run = false;
        da.GetData(index, ref run);
        return run;
    }

    public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
    {
        base.AppendAdditionalMenuItems(menu);
        var cancel = Menu_AppendItem(menu, "Cancel run", (_, _) => Background.Cancel());
        cancel.Enabled = Background.Running;
        var rerun = Menu_AppendItem(menu, "Re-run", (_, _) => { _forceRun = true; ExpireSolution(true); });
        rerun.Enabled = !Background.Running;
        var open = Menu_AppendItem(menu, "Open run folder…", (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = _folder, UseShellExecute = true }); }
            catch { /* nothing to open */ }
        });
        open.Enabled = _folder != null && System.IO.Directory.Exists(_folder);
    }

    public override void RemovedFromDocument(GH_Document document)
    {
        Background.Cancel();
        base.RemovedFromDocument(document);
    }

    protected override void Solve(IGH_DataAccess da)
    {
        var run = ReadRun(da, Params.Input.Count - 1);

        if (!TryBuildCase(da, out var doc, out var engine, out var folder, out var model, out var key))
            return;

        if (Background.Running)
        {
            Message = Background.Banner ?? ProgressLabel + "…";
            da.SetDataList("Log", Background.Lines);
            if (_folder != null) da.SetData("Folder", _folder);
            return;
        }

        if (key != _lastInputsKey) { _lastInputsKey = key; _runConsumed = false; }
        if (!run) _runConsumed = false;

        if ((run && !_runConsumed) || _forceRun)
        {
            _runConsumed = true;
            _forceRun = false;
            Launch(doc, engine, folder, model);
            Message = "Starting…";
            return;
        }

        Report(da);
    }

    private void Launch(CaseDocument doc, EngineDef engine, string folder, ModelDef model)
    {
        _result = null; _error = null; _canceled = false;
        var settings = engine.Settings.Clone();
        var log = Background.NewLog();
        RunResult produced = null;
        var elapsed = TimeSpan.Zero;

        Background.Start(ct =>
        {
            var runFolder = ContainerRunner.PrepareRunFolder(settings, doc, folder, Task);
            _folder = runFolder;
            Prepare(settings, runFolder, doc);
            // Prepare may have changed task_params (container-side paths): write the final case.
            System.IO.File.WriteAllText(System.IO.Path.Combine(runFolder, ContainerRunner.CaseFileName), doc.ToJson());
            log("run folder: " + runFolder);
            Background.SetBanner(ProgressLabel + "…");

            var (outcome, result) = ContainerRunner.RunCase(settings, runFolder, log,
                p => Background.SetProgress(ProgressLabel, p.Fraction), ct);
            elapsed = outcome.Elapsed;
            ct.ThrowIfCancellationRequested();

            if (result == null)
            {
                var tail = string.Join("\n", outcome.Log.Split('\n').Where(l => l.Trim().Length > 0).TakeLast(6));
                throw new InvalidOperationException(
                    $"the engine exited with code {outcome.ExitCode} and wrote no result.json.\n{tail}");
            }
            if (!result.Ok)
            {
                if (result.Traceback != null) log(result.Traceback);
                throw new InvalidOperationException(result.Error ?? "the engine reported a failure without a message");
            }
            produced = new RunResult { Task = Task, Case = doc, Result = result, Folder = runFolder, Elapsed = elapsed, Model = model };
        }, (canceled, error) =>
        {
            _canceled = canceled;
            _error = error?.Message;
            _result = produced;
            ExpireSolution(true);
        });
    }

    private void Report(IGH_DataAccess da)
    {
        da.SetDataList("Log", Background.Lines);
        if (_folder != null) da.SetData("Folder", _folder);

        if (_canceled)
        {
            Message = "Canceled";
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "The run was canceled.");
            return;
        }
        if (_error != null)
        {
            Message = "Failed";
            Error(_error);
            return;
        }
        if (_result == null)
        {
            Message = "Ready";
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Set Run to true to launch the engine.");
            return;
        }

        da.SetData("Result", _result);
        SetOutputs(da, _result);
        Message = $"Done, {BackgroundRun.Time(_result.Elapsed)}";
    }

    /// <summary>Identity hash of the inputs: upstream components hand over new instances when they recompute.</summary>
    protected static int Key(params object[] parts)
    {
        var h = new HashCode();
        foreach (var p in parts)
        {
            if (p is string s) h.Add(s);
            else if (p is null) h.Add(0);
            else if (p.GetType().IsValueType) h.Add(p);
            else if (p is IEnumerable<object> list) foreach (var o in list) h.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o));
            else h.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(p));
        }
        return h.ToHashCode();
    }

    protected static EngineDef DefaultEngine() => new();
}
