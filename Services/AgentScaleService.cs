using Microsoft.Extensions.Options;

namespace TruckMate.Services;

// Real scale: reads the latest weight the edge agent posted. Old or missing data = "No signal".
public class AgentScaleService : IScaleService
{
    private readonly WeightCache _cache;
    private readonly IOptions<ScaleOptions> _opt;

    public AgentScaleService(WeightCache cache, IOptions<ScaleOptions> opt)
    {
        _cache = cache;
        _opt = opt;
    }

    public ScaleSnapshot GetSnapshot()
    {
        var o = _opt.Value;
        var snap = new ScaleSnapshot();
        var entry = _cache.Get(o.ScaleId);
        var fresh = entry is not null
                    && (DateTime.UtcNow - entry.Value.ReceivedUtc).TotalSeconds <= o.StaleAfterSeconds;

        snap.Indicator.Ready = fresh;
        if (!fresh)
        {
            snap.Online = false;
            snap.State = "No signal";
            snap.Message = "No signal from the indicator. Check the cable, the edge agent and the network, then recheck.";
            return snap;
        }

        var r = entry!.Value.Reading;
        snap.Online = true;
        snap.Weight = r.WeightKg;
        snap.IsStable = r.Stable;
        snap.IsZero = Math.Abs(r.WeightKg) <= o.ZeroToleranceKg;

        if (!r.Stable)
        {
            snap.State = "Unstable";
            snap.Message = $"Weight is settling ({r.WeightKg:N0} kg). Wait for a stable reading.";
        }
        else if (snap.IsZero)
        {
            snap.State = "Zero";
            snap.Message = "Scale reads 0 kg and the indicator is online. The vehicle can enter.";
        }
        else
        {
            snap.State = "Not zero";
            snap.Message = $"Scale reads {r.WeightKg:N0} kg.";
        }

        // Barrier, light and camera are not integrated yet, so they keep their default "Ready".
        return snap;
    }
}
