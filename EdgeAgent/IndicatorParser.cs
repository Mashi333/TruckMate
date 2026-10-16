using System.Globalization;
using System.Text.RegularExpressions;

namespace WeighAgent;

public record ParsedReading(decimal WeightKg, bool? StableFlag);

// The only code that should change when the real indicator's line format is known.
public static class IndicatorParser
{
    // Toledo style: "ST,GS,+001234.5kg" (stable) or "US,GS,+001234.5kg" (unstable); OL = overload
    private static readonly Regex Toledo = new(
        @"^(?<flag>ST|US|OL),\s*(GS|NT|TR),\s*(?<num>[+-]?\s*\d+(\.\d+)?)\s*(?<unit>kg|t)?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Plain style: "+  1234 kg" (no stability flag)
    private static readonly Regex Plain = new(
        @"^(?<num>[+-]?\s*\d+(\.\d+)?)\s*(?<unit>kg|t)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool TryParse(string? line, out ParsedReading reading)
    {
        reading = new ParsedReading(0m, null);
        if (string.IsNullOrWhiteSpace(line)) return false;
        line = line.Trim();

        bool? flag = null;
        var m = Toledo.Match(line);
        if (m.Success)
        {
            var f = m.Groups["flag"].Value.ToUpperInvariant();
            if (f == "OL") return false; // overload: no valid weight
            flag = f == "ST";
        }
        else
        {
            m = Plain.Match(line);
            if (!m.Success) return false;
        }

        var text = m.Groups["num"].Value.Replace(" ", "");
        if (!decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return false;
        if (m.Groups["unit"].Value.Equals("t", StringComparison.OrdinalIgnoreCase))
            value *= 1000m;

        reading = new ParsedReading(value, flag);
        return true;
    }
}
