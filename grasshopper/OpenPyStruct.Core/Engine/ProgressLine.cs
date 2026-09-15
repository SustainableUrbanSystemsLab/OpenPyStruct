using System.Globalization;

namespace OpenPyStruct.Core.Engine;

/// <summary>A "PROGRESS done total value" line from the engine's stdout.</summary>
public readonly record struct ProgressLine(int Done, int Total, double Value)
{
    public double Fraction => Total > 0 ? Math.Clamp((double)Done / Total, 0, 1) : 0;

    public static bool TryParse(string? line, out ProgressLine progress)
    {
        progress = default;
        if (line == null || !line.StartsWith("PROGRESS ", StringComparison.Ordinal)) return false;
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4) return false;
        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var done)) return false;
        if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var total)) return false;
        if (!double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) value = double.NaN;
        progress = new ProgressLine(done, total, value);
        return true;
    }
}
