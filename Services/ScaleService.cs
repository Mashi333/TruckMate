namespace TruckMate.Services;

public class ScaleSnapshot
{
    public bool Online { get; set; }
    public decimal? Weight { get; set; }
    public bool IsStable { get; set; }
    public bool IsZero { get; set; }
    public string State { get; set; } = "No signal";
    public string Message { get; set; } = string.Empty;

    public HardwareStatus BarrierLeft { get; set; } = new("Barrier Left");
    public HardwareStatus BarrierRight { get; set; } = new("Barrier Right");
    public HardwareStatus TrafficLight { get; set; } = new("Traffic Light");
    public HardwareStatus Camera { get; set; } = new("Camera");
    public HardwareStatus Indicator { get; set; } = new("Indicator");
}

public class HardwareStatus
{
    public HardwareStatus(string name) => Name = name;
    public string Name { get; set; }
    public bool Ready { get; set; } = true;
    public string Status => Ready ? "Ready" : "Offline";
}

public interface IScaleService
{
    ScaleSnapshot GetSnapshot();
    void SimulateWeight(decimal? weight);
    void SetOnline(bool online);
    void SetHardware(string device, bool ready);
}

public class MockScaleService : IScaleService
{
    private readonly object _lock = new();
    private decimal? _weight = 0m;
    private bool _online = true;
    private bool _stable = true;
    private readonly Dictionary<string, bool> _hardware = new(StringComparer.OrdinalIgnoreCase);

    public ScaleSnapshot GetSnapshot()
    {
        lock (_lock)
        {
            var snap = new ScaleSnapshot
            {
                Online = _online,
                Weight = _online ? _weight : null,
                IsStable = _online && _stable,
            };

            snap.BarrierLeft.Ready = Hardware("Barrier Left");
            snap.BarrierRight.Ready = Hardware("Barrier Right");
            snap.TrafficLight.Ready = Hardware("Traffic Light");
            snap.Camera.Ready = Hardware("Camera");
            snap.Indicator.Ready = _online;

            if (!_online)
            {
                snap.State = "No signal";
                snap.Message = "No signal from the indicator. Check the cable or restart the indicator, then recheck.";
            }
            else if (_weight is null)
            {
                snap.State = "No signal";
                snap.Message = "No signal from the indicator.";
            }
            else if (_weight == 0m)
            {
                snap.IsZero = true;
                snap.State = "Zero";
                snap.Message = "Scale reads 0 kg and the indicator is online. The vehicle can enter.";
            }
            else
            {
                snap.State = "Not zero";
                snap.Message = $"Scale is not 0 ({_weight:N0} kg). The vehicle cannot enter. Clear the scale and recheck.";
            }

            return snap;
        }
    }

    public void SimulateWeight(decimal? weight)
    {
        lock (_lock)
        {
            _weight = weight;
            _stable = true;
        }
    }

    public void SetOnline(bool online)
    {
        lock (_lock)
        {
            _online = online;
        }
    }

    public void SetHardware(string device, bool ready)
    {
        lock (_lock)
        {
            _hardware[device] = ready;
        }
    }

    private bool Hardware(string name) => !_hardware.TryGetValue(name, out var ready) || ready;
}
