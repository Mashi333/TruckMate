using System.IO.Ports;
using System.Text;
using Microsoft.Extensions.Options;

namespace WeighAgent;

// Owns the serial port: reads lines, parses them, works out stability, keeps only the latest reading.
// If the port fails (cable pulled, adapter unplugged) it waits 5 seconds and reopens it.
public class SerialReaderWorker : BackgroundService
{
    private readonly AgentOptions _o;
    private readonly LatestReading _latest;
    private readonly ILogger<SerialReaderWorker> _log;
    private decimal? _last;
    private int _sameCount;

    public SerialReaderWorker(IOptions<AgentOptions> options, LatestReading latest, ILogger<SerialReaderWorker> log)
    {
        _o = options.Value;
        _latest = latest;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Run(() => ReadLoop(ct), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Serial port {Port} failed. Retrying in 5 s.", _o.ComPort);
                try { await Task.Delay(TimeSpan.FromSeconds(5), ct); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private void ReadLoop(CancellationToken ct)
    {
        using var port = new SerialPort(_o.ComPort, _o.BaudRate, _o.Parity, _o.DataBits, _o.StopBits)
        {
            NewLine = _o.NewLine,
            ReadTimeout = 3000,
            Encoding = Encoding.ASCII
        };
        port.Open();
        _log.LogInformation("Opened {Port} at {Baud} baud.", _o.ComPort, _o.BaudRate);

        while (!ct.IsCancellationRequested)
        {
            string line;
            try { line = port.ReadLine(); }
            catch (TimeoutException) { continue; } // quiet line: the poster stops sending stale data

            if (!IndicatorParser.TryParse(line, out var p))
            {
                _log.LogDebug("Ignored line: {Line}", line);
                continue;
            }

            var same = _last is not null && Math.Abs(p.WeightKg - _last.Value) <= _o.StableToleranceKg;
            _sameCount = same ? _sameCount + 1 : 1;
            _last = p.WeightKg;

            // Trust the indicator's own flag; count identical readings only when it has none.
            var stable = p.StableFlag ?? (_sameCount >= _o.StableCount);

            _latest.Set(new AgentReading(p.WeightKg, stable, DateTime.UtcNow));
        }
    }
}
