namespace TruckMate.Services;

// Bound from the "Scale" section of appsettings.json.
public class ScaleOptions
{
    // true = MockScaleService (Development only); false = real weights from the edge agent.
    public bool UseMock { get; set; }

    // Which weighbridge this server reads. Must match the edge agent's ScaleId.
    public string ScaleId { get; set; } = "WB1";

    // An empty scale may read this many kg away from 0 and still count as zero.
    // Set to one or two scale divisions once the indicator is known (e.g. 20 for a 10 kg division).
    public decimal ZeroToleranceKg { get; set; } = 0m;

    // A vehicle weight below this is rejected as "not fully on the scale".
    public decimal MinWeightKg { get; set; } = 100m;

    // The weight API rejects readings above this as impossible.
    public decimal MaxWeightKg { get; set; } = 80000m;

    // A reading older than this means the indicator or the agent is offline.
    public int StaleAfterSeconds { get; set; } = 5;

    // Secret the edge agent sends in the X-Api-Key header. Empty = the weight API refuses everything.
    public string AgentApiKey { get; set; } = "";
}
