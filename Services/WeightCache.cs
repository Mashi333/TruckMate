using System.Collections.Concurrent;

namespace TruckMate.Services;

public record WeightReading(string ScaleId, decimal WeightKg, bool Stable, DateTime Timestamp);

// Latest reading per scale, kept in memory. Staleness is judged by ReceivedUtc (the server's clock),
// never by the agent's timestamp, because the weighbridge PC's clock may be wrong.
public class WeightCache
{
    private readonly ConcurrentDictionary<string, (WeightReading Reading, DateTime ReceivedUtc)> _latest =
        new(StringComparer.OrdinalIgnoreCase);

    public void Update(WeightReading reading) => _latest[reading.ScaleId] = (reading, DateTime.UtcNow);

    public (WeightReading Reading, DateTime ReceivedUtc)? Get(string scaleId) =>
        _latest.TryGetValue(scaleId, out var entry) ? entry : null;
}
