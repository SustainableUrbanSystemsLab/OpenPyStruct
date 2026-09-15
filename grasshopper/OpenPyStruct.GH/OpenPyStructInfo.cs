using System;
using System.Drawing;
using Grasshopper.Kernel;
using OpenPyStruct.GH.GUI;

namespace OpenPyStruct.GH;

public class OpenPyStructInfo : GH_AssemblyInfo
{
    public override string Name => "OpenPyStruct";
    public override Bitmap Icon => Icons.Plugin();
    public override string Description =>
        "Structural optimization with OpenSees + PyTorch: gradient-based moment-of-inertia design "
        + "for beams and frames, training data generation, and FNN/PINN surrogates.";
    public override Guid Id => new("5C1F0B1E-7A3B-4C7B-9C6E-0F1E2D3C4B5A");
    public override string AuthorName => "OpenPyStruct Authors";
    public override string AuthorContact => Docs.Repo;
    public override string AssemblyVersion => GetType().Assembly.GetName().Version.ToString();
}

/// <summary>
/// Runs once when Grasshopper loads the .gha: registers the inline widget parameter proxies so
/// saved documents deserialize the toggles/dropdowns, and orders the ribbon sub-tabs.
/// </summary>
public class OpenPyStructPriority : GH_AssemblyPriority
{
    public override GH_LoadingInstruction PriorityLoad()
    {
        WidgetParamProxies.Register();
        var server = Grasshopper.Instances.ComponentServer;
        server.AddCategoryShortName(Strings.Category, "OPS");
        server.AddCategorySymbolName(Strings.Category, 'O');
        server.AddCategoryIcon(Strings.Category, Icons.Plugin());
        return GH_LoadingInstruction.Proceed;
    }
}

/// <summary>Ribbon tab and sub-tab names. The digit prefix orders the sub-tabs left to right.</summary>
public static class Strings
{
    public const string Category = "OpenPyStruct";
    public const string Model = "1 | Model";
    public const string Loads = "2 | Loads";
    public const string Settings = "3 | Settings";
    public const string Run = "4 | Run";
    public const string Results = "5 | Results";
}
