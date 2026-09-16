// Adapted from Eddy3D (https://github.com/Eddy3D-Dev/Eddy3D), GUI/ParamUI/GH_ToggleParam.cs.
// Copyright (c) Eddy3D Authors. See GUI/README.md in this folder for provenance and licensing.

using System;
using System.Collections.Generic;
using System.Drawing;
using GH_IO.Serialization;
using Grasshopper.GUI;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;

namespace OpenPyStruct.GH.GUI;

public class GH_ToggleParam : GH_PersistentParam<GH_Boolean>
{
    public Action<bool> HandleToggle;
    public bool Toggle;
    private bool _localTruePending;

    public GH_ToggleParam(
        string name,
        string nickname,
        string description,
        GH_BeautifulComponent component = null,
        bool defaultItem = false
    ) : base(name, nickname, description, "OpenPyStruct", "Params")
    {
        Toggle = defaultItem;
        if (PersistentDataCount == 0)
        {
            PersistentData.Append(new GH_Boolean(Toggle));
            HandleToggle?.Invoke(Toggle);
        }

        if (component != null) component.UseParamUI();
    }

    // Parameterless constructor so Grasshopper can reconstruct this param when reloading a
    // document — without it, variable-parameter components (Run, Probe) lose their toggle
    // widgets on restart and fall back to plain inputs. The actual name/value are restored
    // from the archive by Read.
    public GH_ToggleParam() : this("Toggle", "Toggle", "Boolean toggle input")
    {
    }

    public override Guid ComponentGuid => new("{99FC80D7-1649-40C5-B265-E034BA0C6333}");

    // Hidden: created in code by components, never placed from the ribbon.
    public override GH_Exposure Exposure => GH_Exposure.hidden;
    protected override Bitmap Icon => GH_StandardIcons.BlankParameterIcon_24x24;

    public void SetToggle(bool value)
    {
        if (Toggle == value)
            return;

        Toggle = value;
        _localTruePending = value;
        HandleToggle?.Invoke(value);

        PersistentData.Clear();
        PersistentData.Append(new GH_Boolean(value));
        ExpireSolution(true);
    }

    /// <summary>
    /// Returns true once for a live local click that switched this widget on. This is distinct
    /// from <see cref="Toggle"/> so a component can honour its inline button even while an external
    /// source owns the parameter data, without treating a persisted TRUE as a fresh click on load.
    /// </summary>
    public bool ConsumeLocalTrue()
    {
        var pending = _localTruePending;
        _localTruePending = false;
        return pending;
    }

    protected override GH_GetterResult Prompt_Plural(ref List<GH_Boolean> values)
    {
        return GH_GetterResult.cancel;
    }

    protected override GH_GetterResult Prompt_Singular(ref GH_Boolean value)
    {
        return GH_GetterResult.cancel;
    }

    public override bool Read(GH_IReader reader)
    {
        base.Read(reader);

        var value = false;
        if (reader.TryGetBoolean("Value", ref value)) Toggle = value;
        _localTruePending = false;

        HandleToggle?.Invoke(Toggle);
        return true;
    }

    public override bool Write(GH_IWriter writer)
    {
        base.Write(writer);
        writer.SetBoolean("Value", Toggle);
        return true;
    }
}
