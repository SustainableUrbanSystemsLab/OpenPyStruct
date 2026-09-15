// Adapted from Eddy3D (https://github.com/Eddy3D-Dev/Eddy3D), GUI/ParamUI/GH_DropdownParam.cs.
// Copyright (c) Eddy3D Authors. See GUI/README.md in this folder for provenance and licensing.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using GH_IO.Serialization;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Grasshopper.Kernel.Types;

namespace OpenPyStruct.GH.GUI;

public class GH_DropdownParam : GH_PersistentParam<GH_String>
{
    public bool AllowAnyValue;
    public bool CaseSensitive = true;
    public string DefaultItem;
    public Action<string> HandleValueSelected;
    public string ItemIndicateAll;
    public Func<string, string> LabelModifier;
    public bool Multiple;
    public Dictionary<string, List<string>> OptionMap;
    public Dictionary<string, bool> Options;

    /// <summary>
    /// Optional library browser. Given the current key, returns the chosen key or null when
    /// cancelled. When set, the dropdown menu (inline click and right-click alike) leads with
    /// <see cref="BrowseMenuText"/>, and a choice goes through the same path as a menu pick.
    ///
    /// <para>A hook rather than a new input on purpose: a library dropdown already stores the
    /// entry's label as its persistent value, so a browser only needs to hand one back. Adding a
    /// button or an input would move every later input by one index in saved documents
    /// (.claude/rules/component-ui.md); this changes nothing about the layout.</para>
    /// </summary>
    public Func<string, string> Browse;

    /// <summary>The menu item the browser hangs off. Pinned by name because tests look for it.</summary>
    public const string BrowseMenuText = "Browse library…";

    public GH_DropdownParam(
        IEnumerable<string> items,
        string name,
        string nickname,
        string description,
        GH_BeautifulComponent component = null,
        IEnumerable<IEnumerable<string>> itemMap = null,
        string defaultItem = null,
        string itemIndicateAll = null,
        bool hookToLabel = false,
        Action<string> handleValueSelected = null,
        Func<string, string> labelModifier = null,
        bool multiple = false,
        bool optional = false,
        GH_ParamAccess access = GH_ParamAccess.item,
        bool allowAnyValue = false,
        bool caseSensitive = true,
        bool camelCase = false
    ) : base(name, nickname, description,
        "OpenPyStruct",
        "Params")
    {
        HandleValueSelected = str => { };
        if (handleValueSelected != null) HandleValueSelected += handleValueSelected;

        Optional = optional;
        Access = access;
        AllowAnyValue = allowAnyValue;
        CaseSensitive = caseSensitive;
        ItemIndicateAll = itemIndicateAll;

        LabelModifier = s => s;
        if (labelModifier != null) LabelModifier += labelModifier;

        if (camelCase)
        {
            CaseSensitive = false;
            LabelModifier += s => s.ToCamelCase();
        }

        Multiple = multiple;
        ResetOptions(items);

        if (itemMap != null)
            OptionMap = items.Zip(itemMap,
                (k, v) => new { Key = k, Value = v.ToList() }).ToDictionary(
                pair => pair.Key, pair => pair.Value);


        if (PersistentDataCount == 0
            && defaultItem != null
            && defaultItem.Length > 0)
            if (TrySetDropdownValue(defaultItem, true))
                SyncPersistentDataToOptions();

        DefaultItem = defaultItem;

        if (component != null)
        {
            Attributes = new GH_LinkedDropdownParamAttributes(this, component.Attributes);
            component.UseParamUI();
            if (hookToLabel) component.HookToLabel(this);
        }
    }

    // Parameterless constructor so Grasshopper can reconstruct this param when reloading a
    // document (variable-parameter components otherwise lose it on restart). The option set
    // and selection are restored from the archive by Read.
    public GH_DropdownParam() : base("Dropdown", "Dropdown", "Dropdown selection", "OpenPyStruct", "Params")
    {
        HandleValueSelected = s => { };
        LabelModifier = s => s;
        Options = new Dictionary<string, bool>();
    }

    // Hidden: created in code by components, never placed from the ribbon.
    public override GH_Exposure Exposure => GH_Exposure.hidden;

    public bool HasOptions => Options != null && Options.Count > 0;

    public IEnumerable<string> SelectedValues
    {
        get
        {
            if (OptionMap != null)
                return Options.Where(kv => kv.Value).SelectMany(kv => OptionMap[kv.Key]);
            return Options.Where(kv => kv.Value).Select(kv => kv.Key);
        }
    }

    public IEnumerable<string> SelectedLabels => Options.Where(kv => kv.Value).Select(kv => kv.Key);
    public override Guid ComponentGuid => new("{C11CE2E1-B347-4328-9134-F9B9A6FAB5EB}");
    protected override Bitmap Icon => GH_StandardIcons.BlankParameterIcon_24x24;

    public string GetLabel(string value)
    {
        return LabelModifier == null ? value : LabelModifier(value);
    }

    public IEnumerable<string> GetLabels(IEnumerable<string> values)
    {
        return LabelModifier == null ? values : values.Select(LabelModifier);
    }


    public void ResetOptions(IEnumerable<string> options)
    {
        Options = new Dictionary<string, bool>();
        foreach (var item in options) Options[!CaseSensitive ? item.ToLower() : item] = false;
    }

    protected override void OnVolatileDataCollected()
    {
        base.OnVolatileDataCollected();
        if (SourceCount == 0)
            if (PersistentDataCount == 0
                && DefaultItem != null
                && DefaultItem.Length > 0)
                if (TrySetDropdownValue(DefaultItem, true))
                    SyncPersistentDataToOptions();

        if (SourceCount > 0)
        {
            ClearSelection();
            ClearPersistentData();
            var keys = Options.Keys.ToList();
            foreach (var value in VolatileData.AllData(true))
                if (value.CastTo<string>(out var a))
                    try
                    {
                        var i = Convert.ToInt32(a);
                        if (i < keys.Count) TrySetDropdownValue(keys[i], true);
                    }
                    catch
                    {
                        TrySetDropdownValue(a, true);
                    }
        }
    }

    public override void CreateAttributes()
    {
        Attributes = new GH_FloatingDropdownParamAttributes(this);
    }

    public override bool AppendMenuItems(ToolStripDropDown menu)
    {
        Menu_AppendObjectName(menu);
        Menu_AppendDisconnectWires(menu);
        Menu_AppendSeparator(menu);

        Menu_AppendDropdownItems(menu);

        Menu_AppendSeparator(menu);
        Menu_AppendObjectHelp(menu);
        return true;
    }

    public ToolStripDropDownMenu CreateDropdownMenu()
    {
        // DisposeOnClose here rather than at the call site: the factory owns the menu's lifetime,
        // and an undisposed one is finalized off the UI thread and aborts Rhino (see CanvasMenu).
        var menu = new ToolStripDropDownMenu().DisposeOnClose();
        Menu_AppendDropdownItems(menu);
        return menu;
    }

    public virtual void Menu_AppendDropdownItems(ToolStripDropDown menu)
    {
        CollectMissingPersistentData();

        var empty = Options == null || Options.Count == 0;

        // Browse comes FIRST, and before the empty check: a library too large to enumerate has no
        // options to list — the climate catalog is 60,868 stations — and returning "No options
        // available" there would hide the only way to choose one. (Until 2026-09-04 the empty
        // check came first and did exactly that.)
        if (Browse != null)
        {
            var browse = Menu_AppendItem(menu, BrowseMenuText, (sender, e) => OpenBrowser(), true, false);
            if (browse != null) browse.ToolTipText = "Open the library as a sortable, filterable table.";
            if (empty) return;
            Menu_AppendSeparator(menu);
        }

        if (empty)
        {
            var emptyItem = new ToolStripMenuItem("No options available")
            {
                Enabled = false,
                ToolTipText = "No options are currently available to select."
            };
            menu.Items.Add(emptyItem);
            return;
        }

        if (Multiple && Options.Count > 1)
        {
            var selectAll = Menu_AppendItem(menu, "Select All", (sender, e) =>
            {
                foreach (var key in Options.Keys.ToList()) Options[key] = true;
                SyncPersistentDataToOptions();
            }, true, false);
            if (selectAll != null) selectAll.ToolTipText = "Select all available options";

            var clearAll = Menu_AppendItem(menu, "Clear All", (sender, e) =>
            {
                ClearSelection();
                SyncPersistentDataToOptions();
            }, true, false);
            if (clearAll != null) clearAll.ToolTipText = "Clear all selections";

            Menu_AppendSeparator(menu);
        }

        foreach (var option in Options)
        {
            var item = Menu_AppendItem(menu, GetLabel(option.Key), (sender, e) => OnDropdownItemClicked(option.Key), true,
                option.Value);
            if (item != null)
            {
                item.ToolTipText = option.Value
                    ? $"Deselect '{GetLabel(option.Key)}'"
                    : $"Select '{GetLabel(option.Key)}'";
            }
        }
    }

    /// <summary>
    /// Runs <see cref="Browse"/> and adopts its answer. Same rules as a menu click: picking what
    /// is already selected is a no-op (no needless downstream solve), and a key the option set
    /// does not know is refused unless <see cref="AllowAnyValue"/> — which is also what lets a
    /// browser over a superset library (Trees: density classes + species) hand back a species.
    /// </summary>
    public void OpenBrowser()
    {
        if (Browse == null) return;
        string chosen;
        try
        {
            chosen = Browse(SelectedLabels.FirstOrDefault());
        }
        catch (Exception ex)
        {
            Rhino.RhinoApp.WriteLine("OpenPyStruct: the browser could not open: " + ex.Message);
            return;
        }
        if (string.IsNullOrEmpty(chosen)) return;

        var key = CaseSensitive ? chosen : chosen.ToLower();
        if (!Multiple && Options.TryGetValue(key, out var already) && already) return;
        if (TrySetDropdownValue(chosen, true)) SyncPersistentDataToOptions();
    }

    public void OnDropdownItemClicked(string selected)
    {
        var selection = Options[selected];

        // A single-select dropdown is a picker, not a toggle. Clicking the item that is already
        // selected must be a no-op: clearing its only value leaves consumers with an empty
        // selection and, more importantly, SyncPersistentDataToOptions expires the entire
        // downstream graph for a click that changed nothing. On large field components (for
        // example Thermal Comfort in the UMF_UTCI template) that needless solve can occupy the
        // Rhino UI long enough to look like a hang or exhaust memory.
        if (!Multiple && selection) return;

        if (TrySetDropdownValue(selected, !selection)) SyncPersistentDataToOptions();
    }

    /// <summary>
    /// Programmatic pick: the entry point the Esinti bridge uses to select an option the way a
    /// menu click would, so persistent data, <c>ObjectChanged</c> and the downstream expiry all
    /// travel the same path. Returns false when <paramref name="value"/> is not an option and the
    /// dropdown does not accept free text — the caller reports that rather than guessing. A
    /// single-select dropdown re-picking its current value is a no-op (see
    /// <see cref="OnDropdownItemClicked"/> for why that matters on large field components).
    /// </summary>
    public bool TrySelect(string value)
    {
        if (value == null) return false;
        var key = CaseSensitive ? value : value.ToLower();
        if (!Multiple && Options.TryGetValue(key, out var alreadySelected) && alreadySelected) return true;
        if (!TrySetDropdownValue(value, true)) return false;
        SyncPersistentDataToOptions();
        return true;
    }

    private bool TrySetDropdownValue(string selected, bool value)
    {
        if (!CaseSensitive) selected = selected.ToLower();

        if (!Options.ContainsKey(selected) && !AllowAnyValue) return false;

        if (!Multiple) ClearSelection();

        if (ItemIndicateAll != null && Options[ItemIndicateAll] && selected != ItemIndicateAll)
            Options[ItemIndicateAll] = false;

        if (selected == ItemIndicateAll) ClearSelection();

        Options[selected] = value;
        HandleValueSelected?.Invoke(selected);
        return true;
    }

    private void SyncPersistentDataToOptions()
    {
        RemoveAllSources();
        PersistentData.Clear();

        PersistentData.AppendRange(SelectedValues.Select(v => new GH_String(v)));
        OnObjectChanged(GH_ObjectEventType.PersistentData);
        ExpireSolution(true);
    }

    public void ClearSelection()
    {
        foreach (var mode in Options.Keys.ToList()) Options[mode] = false;
    }

    public void ClearPersistentData()
    {
        PersistentData.Clear();
        OnObjectChanged(GH_ObjectEventType.PersistentData);
    }

    public override bool Read(GH_IReader reader)
    {
        base.Read(reader);

        // Variable-parameter components reconstruct dropdowns with the parameterless constructor.
        // Restore multi-select before replaying saved values; otherwise every TrySet call clears
        // the previous selection and only the final option survives a document restart.
        if (reader.ItemExists("Multiple")) Multiple = reader.GetBoolean("Multiple");

        // When Grasshopper reconstructs this param from an archive (parameterless ctor), the
        // option set is empty; rebuild it from the persisted keys so the saved selection can
        // be restored below. Existing in-memory options (normal construction) are kept.
        if (Options == null || Options.Count == 0)
        {
            var optionKeys = "";
            if (reader.TryGetString("Options", ref optionKeys) && optionKeys.Length > 0)
                ResetOptions(optionKeys.Split('\n'));
        }

        if (SourceCount > 0) return true;

        // An archive written BEFORE this input became a dropdown — when it was a plain
        // Param_String — carries no "Value" key at all, but base.Read has already restored the
        // user's typed value into PersistentData. Adopt it rather than falling through: the code
        // below ends in SyncPersistentDataToOptions, which CLEARS PersistentData and rewrites it
        // from the (empty) selection, so a text-input-to-dropdown swap would silently reset every
        // saved document to the dropdown's default. AllowAnyValue is what lets a value outside the
        // option set survive that adoption. Distinguished by ItemExists rather than by an empty
        // string, because a dropdown deliberately saved with nothing selected DOES write "Value"
        // and must come back with nothing selected.
        if (!reader.ItemExists("Value"))
        {
            CollectMissingPersistentData();
            return true;
        }

        ClearSelection();
        var values = "";
        if (reader.TryGetString("Value", ref values) && values.Length > 0)
        {
            // The comma is the MULTI-select serialization separator (see Write). A
            // single-select value must be adopted whole: splitting it tore any label that
            // itself contains a comma — "3 — Residential, after 2000" came back as two
            // fragments, the tail won (allowAnyValue adopted it), and the component warned
            // "' after 2000' is not a PALM class" on every open (field hit, 2026-08-30).
            var parts = Multiple ? values.Split(',') : new[] { values };
            foreach (var value in parts)
            {
                var actualValue = value;
                if (OptionMap != null)
                    actualValue = OptionMap.Where(kv => kv.Value.Contains(value)).Select(kv => kv.Key).Single();

                TrySetDropdownValue(actualValue, true);
            }
        }

        SyncPersistentDataToOptions();
        return true;
    }

    public override bool Write(GH_IWriter writer)
    {
        base.Write(writer);
        writer.SetString("Value", string.Join(",", SelectedValues.ToArray()));
        // Persist the full option set so a reconstructed param (document reload) can rebuild it.
        writer.SetString("Options", string.Join("\n", Options.Keys));
        writer.SetBoolean("Multiple", Multiple);
        return true;
    }

    public void CollectMissingPersistentData()
    {
        foreach (var item in PersistentData.AllData(true))
            if (item.CastTo<string>(out var value))
                TrySetDropdownValue(value, true);
    }

    protected override GH_GetterResult Prompt_Singular(ref GH_String value)
    {
        return GH_GetterResult.cancel;
    }

    protected override GH_GetterResult Prompt_Plural(ref List<GH_String> values)
    {
        return GH_GetterResult.cancel;
    }
}

public class GH_LinkedDropdownParamAttributes : GH_LinkedParamAttributes
{
    public GH_LinkedDropdownParamAttributes(IGH_Param param, IGH_Attributes parent)
        : base(param, parent)
    {
    }

    public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        if (Owner.Locked) return base.RespondToMouseDown(sender, e);

        if (e.Button == MouseButtons.Left) return DropdownUI.RespondToMouseDown(e, Bounds, Owner as GH_DropdownParam);

        return base.RespondToMouseDown(sender, e);
    }
}

public class GH_FloatingDropdownParamAttributes : GH_FloatingParamAttributes
{
    public GH_FloatingDropdownParamAttributes(IGH_Param param)
        : base(param)
    {
    }
}

public static class StringExtension
{
    public static string ToCamelCase(this string str)
    {
        return string.IsNullOrEmpty(str) || str.Length < 2
            ? str.ToLowerInvariant()
            : char.ToLowerInvariant(str[0]) + str.Substring(1);
    }
}
