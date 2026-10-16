using System.IO.Ports;

namespace WeighAgent;

// Bound from the "Agent" section of appsettings.json. Change these on site without rebuilding.
public class AgentOptions
{
    public string ScaleId { get; set; } = "WB1";
    public string ComPort { get; set; } = "COM1";
    public int BaudRate { get; set; } = 9600;
    public int DataBits { get; set; } = 8;
    public Parity Parity { get; set; } = Parity.None;
    public StopBits StopBits { get; set; } = StopBits.One;
    public string NewLine { get; set; } = "\r\n";

    public string ServerUrl { get; set; } = "http://localhost:5095/api/weight";
    public string ApiKey { get; set; } = "";

    // Used only when the indicator line has no ST/US flag.
    public int StableCount { get; set; } = 5;
    public decimal StableToleranceKg { get; set; } = 0m;

    public int PostIntervalMs { get; set; } = 250;
    public int HeartbeatMs { get; set; } = 1000;
}
