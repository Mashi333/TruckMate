namespace WeighAgent;

public record AgentReading(decimal WeightKg, bool Stable, DateTime TimestampUtc);

// The newest parsed reading, shared between the serial reader and the poster.
public class LatestReading
{
    private AgentReading? _value;
    public void Set(AgentReading reading) => Volatile.Write(ref _value, reading);
    public AgentReading? Get() => Volatile.Read(ref _value);
}
