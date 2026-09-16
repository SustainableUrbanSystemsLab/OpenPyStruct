using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenPyStruct.Core.Contract;

/// <summary>
/// result.json as written by <c>openpystruct run</c>. Parsed leniently through JsonNode: every
/// task shares the envelope (schema, task, ok, error, elapsed_s), the payload differs per task
/// and is exposed through typed accessors that return null when absent.
/// </summary>
public sealed class ResultDocument
{
    public const string Schema = "openpystruct.result/1";

    private readonly JsonObject _root;

    private ResultDocument(JsonObject root) => _root = root;

    public static ResultDocument Parse(string json)
    {
        var node = JsonNode.Parse(json) as JsonObject
                   ?? throw new InvalidDataException("result.json is not a JSON object");
        var schema = node["schema"]?.GetValue<string>();
        if (schema != Schema)
            throw new InvalidDataException(
                $"result.json schema {schema ?? "(none)"} is not {Schema}; engine and plugin versions disagree.");
        return new ResultDocument(node);
    }

    public static ResultDocument Load(string path) => Parse(File.ReadAllText(path));

    public string? Task => _root["task"]?.GetValue<string>();
    public bool Ok => _root["ok"]?.GetValue<bool>() ?? false;
    public string? Error => _root["error"]?.GetValue<string>();
    public string? Traceback => _root["traceback"]?.GetValue<string>();
    public double ElapsedSeconds => _root["elapsed_s"]?.GetValue<double>() ?? 0;
    public string? EngineVersion => _root["engine_version"]?.GetValue<string>();

    // optimize / predict -----------------------------------------------------------------

    /// <summary>Optimized (or predicted) moment of inertia per element, m^4.</summary>
    public double[]? I => Doubles(_root["I"]);
    public int? Epochs => _root["epochs"]?.GetValue<int>();
    public bool? StoppedEarly => _root["stopped_early"]?.GetValue<bool>();
    public double? BestLoss => _root["best_loss"] is JsonValue v && v.TryGetValue<double>(out var d) ? d : null;
    public string? ModelKind => (_root["model_kind"] ?? _root["kind"])?.GetValue<string>();
    public string? ModelPath => _root["model_path"]?.GetValue<string>();

    public LossHistory? Losses
    {
        get
        {
            if (_root["loss_history"] is not JsonObject h) return null;
            return new LossHistory(Doubles(h["total"]) ?? Array.Empty<double>(),
                Doubles(h["primary"]) ?? Array.Empty<double>(),
                Doubles(h["bending"]) ?? Array.Empty<double>(),
                Doubles(h["shear"]) ?? Array.Empty<double>());
        }
    }

    public IReadOnlyList<CaseResult> Cases
    {
        get
        {
            if (_root["cases"] is not JsonArray arr) return Array.Empty<CaseResult>();
            var list = new List<CaseResult>(arr.Count);
            foreach (var c in arr)
            {
                if (c is not JsonObject o) continue;
                list.Add(new CaseResult(
                    o["name"]?.GetValue<string>() ?? "LC",
                    Rows(o["displacements"]),
                    Doubles(o["axial"]) ?? Array.Empty<double>(),
                    Doubles(o["shear_i"]) ?? Array.Empty<double>(),
                    Doubles(o["shear_j"]) ?? Array.Empty<double>(),
                    Doubles(o["moment_i"]) ?? Array.Empty<double>(),
                    Doubles(o["moment_j"]) ?? Array.Empty<double>()));
            }
            return list;
        }
    }

    // generate_data / train --------------------------------------------------------------

    public string? DatasetPath => _root["dataset_path"]?.GetValue<string>();
    public int? Samples => _root["samples"]?.GetValue<int>();
    public int? Nelem => _root["nelem"]?.GetValue<int>();
    public int? NCases => _root["n_cases"]?.GetValue<int>();
    /// <summary>The device training actually ran on: "cuda", "mps" or "cpu".</summary>
    public string? Device => _root["device"]?.GetValue<string>();
    public double? BestValLoss => _root["best_val_loss"] is JsonValue v && v.TryGetValue<double>(out var d) ? d : null;
    public double[]? TrainLoss => Doubles(_root["train_loss"]);
    public double[]? ValLoss => Doubles(_root["val_loss"]);

    public string RawJson => _root.ToJsonString();

    private static double[]? Doubles(JsonNode? node)
    {
        if (node is not JsonArray arr) return null;
        var result = new double[arr.Count];
        for (var i = 0; i < arr.Count; i++)
            result[i] = arr[i] is JsonValue v && v.TryGetValue<double>(out var d) ? d : double.NaN;
        return result;
    }

    private static double[][] Rows(JsonNode? node)
    {
        if (node is not JsonArray arr) return Array.Empty<double[]>();
        var rows = new double[arr.Count][];
        for (var i = 0; i < arr.Count; i++) rows[i] = Doubles(arr[i]) ?? Array.Empty<double>();
        return rows;
    }
}

public sealed record LossHistory(double[] Total, double[] Primary, double[] Bending, double[] Shear);

/// <summary>
/// One load case's final analysis. Section forces follow the engine's convention: sagging
/// positive moment, V = dM/dx, tension positive axial; both element ends are given.
/// </summary>
public sealed record CaseResult(
    string Name,
    double[][] Displacements,
    double[] Axial,
    double[] ShearI,
    double[] ShearJ,
    double[] MomentI,
    double[] MomentJ)
{
    public int ElementCount => MomentI.Length;
    public int NodeCount => Displacements.Length;
}
