// Adapted from Eddy3D (https://github.com/Eddy3D-Dev/Eddy3D), GUI/BackgroundRun.cs.
// Copyright (c) Eddy3D Authors. See GUI/README.md in this folder for provenance and licensing.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Grasshopper.Kernel;

namespace OpenPyStruct.GH.GUI;

/// <summary>
/// Background-run state for a component that shells out to a long-running engine: owns the
/// cancellation source, collects the run's log lines for a Log output, and drives the component's
/// Message strip from the worker thread.
/// <para>
/// This is the plugin-agnostic half of the pattern <c>Radiance.CMP.MrtComponentRun</c> established
/// (which stays as it is — it is bound to <c>MrtRunLog</c>'s progress events). Here the log is a
/// plain <see cref="Action{T}"/> sink, so any engine that writes lines can use it.
/// </para>
/// <para>
/// A modal progress dialog is deliberately NOT used: it blocks the canvas, and cancel belongs on
/// the component's right-click menu.
/// </para>
/// </summary>
public sealed class BackgroundRun
{
    private readonly GH_Component _owner;
    private readonly object _gate = new();
    private CancellationTokenSource _cts;
    private List<string> _lines = new();
    private string _banner;

    public BackgroundRun(GH_Component owner) => _owner = owner;

    /// <summary>True between <see cref="Start"/> and the completion callback.</summary>
    public bool Running { get; private set; }

    /// <summary>Wall time of the last completed run; null until one finishes.</summary>
    public TimeSpan? Elapsed { get; private set; }

    /// <summary>Current banner text, for a mid-run recompute.</summary>
    public string Banner => _banner;

    /// <summary>The last run's log lines, in order.</summary>
    public IReadOnlyList<string> Lines
    {
        get { lock (_gate) return _lines.ToArray(); }
    }

    /// <summary>
    /// A fresh log sink for one run. The previous run's lines are replaced when the new run
    /// STARTS, so a failed run's log stays readable until the user actually relaunches.
    /// </summary>
    public Action<string> NewLog()
    {
        lock (_gate) _lines = new List<string>();
        return line =>
        {
            if (line == null) return;
            lock (_gate) _lines.Add(line);
        };
    }

    /// <summary>Adds one line to the current log without disturbing the banner.</summary>
    public void Log(string line)
    {
        if (line == null) return;
        lock (_gate) _lines.Add(line);
    }

    /// <summary>
    /// Launches the work on a background task. <paramref name="onFinished"/> runs on the UI thread
    /// with (canceled, error) exactly once; an explicit <see cref="Cancel"/> and a cooperative
    /// <see cref="OperationCanceledException"/> both count as canceled.
    /// </summary>
    public void Start(Action<CancellationToken> work, Action<bool, Exception> onFinished)
    {
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        Running = true;
        var clock = Stopwatch.StartNew();

        Task.Run(() => work(ct), ct).ContinueWith(t =>
        {
            clock.Stop();
            Elapsed = clock.Elapsed;
            Running = false;

            var canceled = t.IsCanceled;
            Exception error = null;
            if (t.IsFaulted)
            {
                var inner = t.Exception?.Flatten().InnerException ?? t.Exception;
                if (inner is OperationCanceledException) canceled = true;
                else error = inner;
            }

            Rhino.RhinoApp.InvokeOnUiThread(new Action(() => onFinished(canceled, error)));
        });
    }

    /// <summary>
    /// Requests cancellation OFF the UI thread. <see cref="CancellationTokenSource.Cancel()"/> runs
    /// every registered callback synchronously on the calling thread, and engine teardown there can
    /// mean killing a process tree — called straight from a menu handler that would otherwise
    /// freeze Rhino for the duration.
    /// </summary>
    public void Cancel()
    {
        var cts = _cts;
        if (cts == null) return;

        Task.Run(() =>
        {
            try { cts.Cancel(); }
            catch (ObjectDisposedException) { }
            catch (AggregateException) { /* a callback threw; the run is being torn down anyway */ }
        });
    }

    /// <summary>Sets the Message strip from any thread, skipping redundant repaints.</summary>
    public void SetBanner(string text)
    {
        if (text == _banner) return;
        _banner = text;

        Rhino.RhinoApp.InvokeOnUiThread(new Action(() =>
        {
            _owner.Message = text;
            _owner.Attributes?.ExpireLayout();
            Grasshopper.Instances.ActiveCanvas?.Invalidate();
        }));
    }

    /// <summary>
    /// Sets the Message strip to a labelled progress bar. Same thread-safety and repaint dedupe as
    /// <see cref="SetBanner"/> — a download reporting every buffer would otherwise repaint the
    /// canvas thousands of times, and <see cref="SetBanner"/> drops the repeats because the bar's
    /// text only changes once per cell or per whole percent.
    /// </summary>
    public void SetProgress(string label, double fraction, int cells = ProgressBar.DefaultCells) =>
        SetBanner(ProgressBar.Text(label, fraction, cells));

    /// <inheritdoc cref="SetProgress(string,double,int)"/>
    public void SetProgress(string label, int done, int total, int cells = ProgressBar.DefaultCells) =>
        SetBanner(ProgressBar.Text(label, done, total, cells));

    /// <summary>Runtime for a banner — seconds under a minute, minutes above.</summary>
    public static string Time(TimeSpan t) =>
        t.TotalSeconds < 60 ? $"{t.TotalSeconds:F1} s" : $"{(int)t.TotalMinutes} min {t.Seconds:D2} s";
}
