using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace OpenPyStruct.Core.Contract;

/// <summary>
/// The case.json the plugin hands to the Python engine. Field names and units are defined in
/// <c>openpystruct/schema.py</c>; this is the C# mirror. Keep the two in lockstep and bump
/// <see cref="Schema"/> on every incompatible change so an old engine refuses a new case
/// instead of misreading it.
/// </summary>
public sealed class CaseDocument
{
    public const string Schema = "openpystruct.case/1";

    [JsonPropertyName("schema")] public string SchemaVersion { get; set; } = Schema;
    [JsonPropertyName("task")] public string Task { get; set; } = "optimize";

    /// <summary>"opensees", "numpy" or null for the engine's own choice.</summary>
    [JsonPropertyName("fe_backend")] public string? FeBackend { get; set; }

    [JsonPropertyName("model")] public StructuralModel? Model { get; set; }
    [JsonPropertyName("material")] public Material Material { get; set; } = new();
    [JsonPropertyName("load_cases")] public List<LoadCase> LoadCases { get; set; } = new();
    [JsonPropertyName("optimizer")] public OptimizerSettings Optimizer { get; set; } = new();

    /// <summary>Task-specific parameters; see the Python task modules for each layout.</summary>
    [JsonPropertyName("task_params")] public JsonObject TaskParams { get; set; } = new();

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static CaseDocument FromJson(string json) =>
        JsonSerializer.Deserialize<CaseDocument>(json, JsonOptions)
        ?? throw new InvalidDataException("case.json is empty");
}

public sealed class StructuralModel
{
    /// <summary>"beam" or "frame". A beam is a frame with collinear nodes; the ML tasks need beams.</summary>
    [JsonPropertyName("kind")] public string Kind { get; set; } = "frame";

    /// <summary>[x, y] per node, metres. y is UP.</summary>
    [JsonPropertyName("nodes")] public List<double[]> Nodes { get; set; } = new();

    /// <summary>[i, j] 0-based node indices per element.</summary>
    [JsonPropertyName("elements")] public List<int[]> Elements { get; set; } = new();

    [JsonPropertyName("supports")] public List<Support> Supports { get; set; } = new();

    public int NodeCount => Nodes.Count;
    public int ElementCount => Elements.Count;
}

public sealed class Support
{
    public const string Pin = "pin";
    public const string Roller = "roller";
    public const string Fixed = "fixed";

    [JsonPropertyName("node")] public int Node { get; set; }
    [JsonPropertyName("type")] public string Type { get; set; } = Pin;
}

/// <summary>Material and section constants. Defaults are the scripts' steel beam.</summary>
public sealed class Material
{
    [JsonPropertyName("E")] public double E { get; set; } = 200e9;
    [JsonPropertyName("nu")] public double Nu { get; set; } = 0.3;
    [JsonPropertyName("A")] public double A { get; set; } = 0.01;
    [JsonPropertyName("I0")] public double I0 { get; set; } = 0.5;
    [JsonPropertyName("k")] public double K { get; set; } = 0.03;
}

public sealed class LoadCase
{
    [JsonPropertyName("name")] public string Name { get; set; } = "LC";
    [JsonPropertyName("point_loads")] public List<PointLoad> PointLoads { get; set; } = new();
    [JsonPropertyName("element_loads")] public List<ElementLoad> ElementLoads { get; set; } = new();
}

public sealed class PointLoad
{
    [JsonPropertyName("node")] public int Node { get; set; }
    [JsonPropertyName("fx")] public double Fx { get; set; }
    [JsonPropertyName("fy")] public double Fy { get; set; }
    [JsonPropertyName("mz")] public double Mz { get; set; }
}

public sealed class ElementLoad
{
    [JsonPropertyName("element")] public int Element { get; set; }
    /// <summary>Global-y load per unit length, N/m. Negative = gravity.</summary>
    [JsonPropertyName("wy")] public double Wy { get; set; }
}

public sealed class OptimizerSettings
{
    [JsonPropertyName("epochs")] public int Epochs { get; set; } = 1000;
    [JsonPropertyName("lr")] public double LearningRate { get; set; } = 0.01;
    [JsonPropertyName("gamma")] public double Gamma { get; set; } = 0.98;
    [JsonPropertyName("alpha_moment")] public double AlphaMoment { get; set; } = 1e-2;
    [JsonPropertyName("alpha_shear")] public double AlphaShear { get; set; } = 1e-2;
    [JsonPropertyName("tolerance")] public double Tolerance { get; set; } = 1e-2;
    [JsonPropertyName("patience")] public int Patience { get; set; } = 10;
    [JsonPropertyName("I_min")] public double IMin { get; set; } = 1e-8;
}
