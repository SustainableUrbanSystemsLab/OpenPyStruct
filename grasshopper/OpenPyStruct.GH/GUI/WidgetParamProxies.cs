// Adapted from Eddy3D (https://github.com/Eddy3D-Dev/Eddy3D), GUI/WidgetParamProxies.cs.
// Copyright (c) Eddy3D Authors. See GUI/README.md in this folder for provenance and licensing.

using System;
using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;

namespace OpenPyStruct.GH.GUI;

/// <summary>
/// Registers Grasshopper object proxies for the inline widget parameters (GH_ToggleParam,
/// GH_DropdownParam). These types live in this shared GUI assembly — a plain DLL, not a .gha — so
/// Grasshopper never scans it and its component server holds no proxy for them. That breaks
/// IGH_VariableParameterComponent deserialization: those components save through
/// WriteAllParameterData (each param's type GUID) and load through ReadAllParameterData, which
/// Clear()s the ctor-built params and rebuilds each via Instances.ComponentServer.EmitObject(guid).
/// With no proxy, EmitObject fails ("Parameter type is unknown"), Param_GenericObject is substituted,
/// the fixed-chunk fallback then reports "Input parameter chunk is missing. Archive is corrupt", and
/// the type-gated round widget buttons are gone — a resave bakes the plain params in permanently.
/// Each plugin GHA calls <see cref="Register"/> from its GH_AssemblyPriority so EmitObject can
/// construct the widget params again. Idempotent; first loaded plugin wins.
/// </summary>
public static class WidgetParamProxies
{
    public static void Register()
    {
        TryAdd(new GH_ToggleParam());
        TryAdd(new GH_DropdownParam());
    }

    private static void TryAdd(IGH_Param archetype)
    {
        try
        {
            var server = Instances.ComponentServer;
            if (server == null || server.EmitObjectProxy(archetype.ComponentGuid) != null) return;
            server.AddProxy(new WidgetParamProxy(archetype));
        }
        catch
        {
            // Proxy registration must never break plugin loading.
        }
    }

    private sealed class WidgetParamProxy : IGH_ObjectProxy
    {
        private readonly IGH_Param _archetype;

        public WidgetParamProxy(IGH_Param archetype)
        {
            _archetype = archetype;
        }

        public string Location => _archetype.GetType().Assembly.Location;
        public Guid LibraryGuid => Guid.Empty;
        public bool SDKCompliant => true;
        public bool Obsolete => false;
        public Type Type => _archetype.GetType();
        public GH_ObjectType Kind => GH_ObjectType.CompiledObject;
        public Guid Guid => _archetype.ComponentGuid;
        public Bitmap Icon => _archetype.Icon_24x24;
        public IGH_InstanceDescription Desc => _archetype;
        public GH_Exposure Exposure { get; set; } = GH_Exposure.hidden;

        public IGH_ObjectProxy DuplicateProxy() => new WidgetParamProxy(_archetype);

        public IGH_DocumentObject CreateInstance() =>
            (IGH_DocumentObject)Activator.CreateInstance(_archetype.GetType());
    }
}
