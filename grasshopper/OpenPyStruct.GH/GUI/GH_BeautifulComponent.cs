// Adapted from Eddy3D (https://github.com/Eddy3D-Dev/Eddy3D), GUI/GH_BeautifulComponent.cs.
// Copyright (c) Eddy3D Authors. See GUI/README.md in this folder for provenance and licensing.

using System;
using System.Linq;
using System.Reflection;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;

namespace OpenPyStruct.GH.GUI;

/// <summary>
/// Base for every OpenPyStruct component: inline toggle/dropdown widgets, buttons, labels and the version
/// suffix on the description.
/// <para>
/// <b>Two conventions every derived component follows</b> :
/// </para>
/// <list type="bullet">
/// <item>A <b>Run toggle is registered LAST</b>, so it sits in the same place on every component
/// and a run is never triggered while reaching past it. Order otherwise reads as a run is set up:
/// where it writes, what it operates on, how it is configured, then Run.</item>
/// <item><b>Never insert or remove an input in the middle.</b> Grasshopper reads a fixed
/// component's parameters BY INDEX, so saved wires silently move to whatever now occupies their old
/// position — which once made a component appear to hang for minutes. Append, or ship a load-time
/// migration (<c>MrtRunCMP.MigrateInputLayout</c>).</item>
/// </list>
/// </summary>
public abstract class GH_BeautifulComponent : GH_Component
{
    public string ButtonText;
    public string ButtonToolTip;

    public string[] Labels;
    public Action<GH_Canvas, GH_CanvasMouseEvent> OnButtonClick;

    // Derived from the live params instead of a flag set in the toggle/dropdown constructor:
    // on reload Grasshopper rebuilds those params via their parameterless ctor (no component
    // reference), so a flag-based approach left the inline round buttons missing until the next
    // full relayout. Computing it from the params keeps the buttons after every deserialization.
    public bool HasBeautifulParams =>
        Params?.Input != null && Params.Input.Any(IsWidgetParam);

    /// <summary>The inline widget param types: what the widget column draws.</summary>
    private static bool IsWidgetParam(IGH_Param p) =>
        p is GH_ToggleParam || p is GH_DropdownParam;

    // The inline widget params (toggles/dropdowns) RegisterInputParams created. The base
    // GH_Component ctor has already run RegisterInputParams by the time this body executes, so this
    // is the component's intended widget set — used to detect and heal deserialization losses.
    private readonly IGH_Param[] _ctorWidgetParams;

    public GH_BeautifulComponent(
        string name,
        string nickname,
        string description,
        string category,
        string subCategory)
        : base(name, nickname, description, category, subCategory)
    {
        _ctorWidgetParams = Params?.Input?
            .Where(IsWidgetParam).ToArray()
            ?? Array.Empty<IGH_Param>();
    }

    private int CountWidgetParams() =>
        Params?.Input?.Count(IsWidgetParam) ?? 0;

    private string _versionSuffix;

    /// <summary>
    /// Appends the plugin version to the component description (shown under it in the tooltip/help),
    /// as the original Eddy3D did. The version is read from the concrete component's own assembly, so
    /// each plugin (Outdoor / OutdoorPlus / Radiance / Indoor / FluidX3D) reports its own build.
    /// </summary>
    public override string Description => base.Description + (_versionSuffix ??= BuildVersionSuffix());

    private string BuildVersionSuffix()
    {
        try
        {
            var asm = GetType().Assembly;
            var version =
                asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? asm.GetName().Version?.ToString();
            // Deterministic builds append "+<commit-hash>" to the informational version — trim it.
            var plus = version?.IndexOf('+') ?? -1;
            if (plus >= 0) version = version[..plus];
            return string.IsNullOrWhiteSpace(version) ? "" : $"\n\nVersion {version}";
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// Heals inline widget params lost during deserialization. Files saved without registered widget
    /// proxies deserialized variable-parameter components with
    /// <c>Param_GenericObject</c> in place of the toggles/dropdowns; a resave baked those in. After
    /// base.Read, any input that matches a ctor-built widget by name but lost the widget type is
    /// swapped back to the ctor instance (nickname + wires carried over). Fixed components read their
    /// params in place, so this is a no-op for them.
    /// </summary>
    public override bool Read(GH_IO.Serialization.GH_IReader reader)
    {
        var ok = base.Read(reader);
        RestoreLostWidgetParams();
        return ok;
    }

    private void RestoreLostWidgetParams()
    {
        if (_ctorWidgetParams == null || _ctorWidgetParams.Length == 0 || Params?.Input == null) return;

        var changed = false;
        for (var i = 0; i < Params.Input.Count; i++)
        {
            var live = Params.Input[i];
            if (live is GH_ToggleParam || live is GH_DropdownParam) continue;

            var original = _ctorWidgetParams.FirstOrDefault(w =>
                !ReferenceEquals(w, live) && w.Name == live.Name);
            if (original == null) continue;

            original.NickName = live.NickName;
            original.Optional = live.Optional;
            foreach (var source in live.Sources.ToArray())
                original.AddSource(source);

            Params.UnregisterInputParameter(live, true);
            Params.RegisterInputParam(original, i);
            changed = true;
        }

        if (changed) Params.OnParametersChanged();
    }

    public bool HasButton => !string.IsNullOrEmpty(ButtonText);
    public bool HasLabel => Labels is { Length: > 0 };

    /// <summary>
    /// A dropdown rendered as a widget on the OUTPUT side of the component — for selections that
    /// configure what the component EMITS (e.g. Deconstruct Weather's variable picker). Unlike the
    /// inline input dropdowns this is NOT a parameter: nothing can be wired into it, it occupies no
    /// input index (so it can never take a wire that belongs to a real input), and its selection is
    /// persisted by the owning component, not by parameter data. Assign a standalone
    /// <see cref="GH_DropdownParam"/> (constructed WITHOUT the component argument) and persist its
    /// selection in the component's Write/Read.
    /// </summary>
    public GH_DropdownParam OutputDropdown;

    public bool HasOutputDropdown => OutputDropdown is { HasOptions: true };

    public override void CreateAttributes()
    {
        Attributes = new GH_ComponentUIAttributes(this);
    }

    /// <summary>
    /// The widest text this component must be able to draw UNDER itself (its <c>Message</c>), or
    /// null for no minimum.
    /// <para>
    /// Grasshopper sizes a component from its parameter names and icon, which says nothing about
    /// the Message strip — a wider message wraps onto a second line and reads as a rendering
    /// glitch. This is the widget-aware equivalent of <see cref="MinWidthComponentAttributes"/>,
    /// which a <see cref="GH_BeautifulComponent"/> CANNOT use: overriding <c>CreateAttributes</c>
    /// to install it replaces <see cref="GH_ComponentUIAttributes"/> and the inline dropdowns and
    /// toggles silently stop rendering. Express the minimum as the TEXT that must fit, not a pixel
    /// count, so it survives a font or DPI change.
    /// </para>
    /// </summary>
    public virtual string MinWidthText => null;

    public override void AddedToDocument(GH_Document document)
    {
        base.AddedToDocument(document);

        // Deserialization sanity check: the ctor built inline widget params, but they are all gone
        // now (plain params in their place). GH 8.32 reads fixed-component params in place, so this
        // should be impossible — if it fires, the loss happened in the load path (e.g. a stale
        // duplicate .gha serving the document). Surface what actually loaded so the report carries
        // the answer.
        if (_ctorWidgetParams.Length > 0 && CountWidgetParams() == 0)
        {
            var types = Params?.Input == null
                ? "(none)"
                : string.Join(", ", Params.Input.Select(p => p.GetType().Name));
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"Inline widget inputs were lost while loading this file (expected {_ctorWidgetParams.Length}, " +
                $"found 0; inputs: {types}). Round buttons cannot render. " +
                $"GUI assembly {typeof(GH_BeautifulComponent).Assembly.GetName().Version}. " +
                "A stale or duplicate installed plugin version is the usual cause — check for an old " +
                "YAK-installed package alongside the dev build.");
        }

        // On document load/restart the inline toggle/dropdown buttons sometimes don't render until
        // the next manual canvas relayout — derived components add/remove inline params in their own
        // AddedToDocument (after this base call), which leaves the just-built attribute layout stale.
        // Expire immediately AND after the next solution: during file open the scheduled solution can
        // be dropped (document not yet enabled, canvas not yet attached), which left reloaded
        // components without their widget column until a manual relayout.
        Attributes?.ExpireLayout();
        document?.ScheduleSolution(1, _ =>
        {
            Attributes?.ExpireLayout();
            Grasshopper.Instances.ActiveCanvas?.Invalidate();
        });
    }

    // Retained so the toggle/dropdown param constructors can call it, but a no-op now:
    // HasBeautifulParams is derived from the live params (see above).
    public void UseParamUI()
    {
    }

    protected virtual void Solve(IGH_DataAccess DA)
    {
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        Solve(DA);
    }

    /// <summary>
    /// Resets a named inline <see cref="GH_ToggleParam"/> input back to off on the next solution,
    /// so it behaves as a momentary "run" button. Safe to call from SolveInstance/Solve.
    /// </summary>
    protected void ResetToggle(string inputName)
    {
        OnPingDocument()?.ScheduleSolution(5, _ =>
        {
            // By TYPE first, then name: base.Read stamps saved names onto params BY INDEX, so a
            // layout change can leave a non-toggle param carrying the toggle's name. Find() took
            // that impostor, the real toggle never reset, and every ExpireSolution relaunched the
            // run — the MRT View Factors perpetual-simulation bug (2026-08-11).
            var toggle = Params.Input.OfType<GH_ToggleParam>()
                .FirstOrDefault(p => p.Name == inputName)
                ?? Params.Input.OfType<GH_ToggleParam>().FirstOrDefault();
            toggle?.SetToggle(false);
        });
    }

    public void Error(string message)
    {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, message);
    }

    public void Warning(string message)
    {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, message);
    }

    // Transient-label feedback (e.g. "✅ Copied!" that reverts after a delay). Ported from
    // dev #322 (race-condition fix) when GH_BeautifulComponent moved into this shared GUI project.
    // A single class-level timer is cancelled+restarted on each call so rapid clicks can't leave a
    // stale revert pending. System.Windows.Forms.Timer compiles cross-platform via the net48
    // reference assembly; it only ever runs in the Windows Rhino plugin.
    private System.Windows.Forms.Timer _transientLabelTimer;
    private Action _onTransientLabelExpired;

    private System.Windows.Forms.Timer _transientMessageTimer;
    private Action _onTransientMessageExpired;

    public override void RemovedFromDocument(GH_Document document)
    {
        if (_transientLabelTimer != null)
        {
            _transientLabelTimer.Stop();
            _transientLabelTimer.Dispose();
            _transientLabelTimer = null;
        }
        if (_transientMessageTimer != null)
        {
            _transientMessageTimer.Stop();
            _transientMessageTimer.Dispose();
            _transientMessageTimer = null;
        }
        base.RemovedFromDocument(document);
    }

    /// <summary>
    /// Sets a temporary label that reverts via <paramref name="onExpired"/> after
    /// <paramref name="durationMs"/>. A single class-level timer prevents race conditions on
    /// rapid clicks.
    /// </summary>
    public void SetTransientLabel(string text, int durationMs, Action onExpired)
    {
        this.SetLabel(text);

        if (_transientLabelTimer != null)
        {
            _transientLabelTimer.Stop();
            _transientLabelTimer.Dispose();
        }

        _onTransientLabelExpired = onExpired;
        _transientLabelTimer = new System.Windows.Forms.Timer { Interval = durationMs };
        _transientLabelTimer.Tick += (s, ev) =>
        {
            if (_transientLabelTimer != null)
            {
                _transientLabelTimer.Stop();
                _transientLabelTimer.Dispose();
                _transientLabelTimer = null;
            }
            _onTransientLabelExpired?.Invoke();
        };
        _transientLabelTimer.Start();
    }

    /// <summary>
    /// Sets a temporary native GH Message banner that reverts via <paramref name="onExpired"/> after
    /// <paramref name="durationMs"/>. A single class-level timer prevents race conditions on
    /// rapid clicks.
    /// </summary>
    public void SetTransientMessage(string text, int durationMs, Action onExpired)
    {
        this.Message = text;
        Grasshopper.Instances.ActiveCanvas?.Refresh();

        if (_transientMessageTimer != null)
        {
            _transientMessageTimer.Stop();
            _transientMessageTimer.Dispose();
        }

        _onTransientMessageExpired = onExpired;
        _transientMessageTimer = new System.Windows.Forms.Timer { Interval = durationMs };
        _transientMessageTimer.Tick += (s, ev) =>
        {
            if (_transientMessageTimer != null)
            {
                _transientMessageTimer.Stop();
                _transientMessageTimer.Dispose();
                _transientMessageTimer = null;
            }
            _onTransientMessageExpired?.Invoke();
        };
        _transientMessageTimer.Start();
    }

    /// <summary>
    /// Every beautiful component gets an "Open Documentation" context-menu item. It deep-links
    /// the docs SEARCH (?q=component name) rather than a per-component page URL: the docs pages
    /// are slugged by hand (e.g. "Atmospheric Boundary Layer" lives at ABL_Flow.md), so a
    /// name-derived page URL would 404 while the search always lands somewhere useful.
    /// </summary>
    public override void AppendAdditionalMenuItems(System.Windows.Forms.ToolStripDropDown menu)
    {
        base.AppendAdditionalMenuItems(menu);
        GH_DocumentObject.Menu_AppendSeparator(menu);
        GH_DocumentObject.Menu_AppendItem(menu, "Open Documentation…", (s, e) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = Docs.SearchUrl(Name),
                    UseShellExecute = true,
                });
            }
            catch (Exception)
            {
                // No browser/handler — nothing actionable from inside the menu click.
            }
        });
    }
}
