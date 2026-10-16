using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace WeighAgent;

// Sends the latest reading to the server: when it changes (at most every PostIntervalMs),
// plus a heartbeat every HeartbeatMs. Stops sending if the serial side has been quiet for 3 s,
// so the server shows "No signal" instead of an old weight.
public class PosterWorker : BackgroundService
{
    private readonly AgentOptions _o;
    private readonly LatestReading _latest;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<PosterWorker> _log;

    public PosterWorker(IOptions<AgentOptions> options, LatestReading latest,
        IHttpClientFactory http, ILogger<PosterWorker> log)
    {
        _o = options.Value;
        _latest = latest;
        _http = http;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_o.ApiKey))
            _log.LogWarning("Agent:ApiKey is empty. The server will refuse every reading.");

        var client = _http.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(3);
        client.DefaultRequestHeaders.Add("X-Api-Key", _o.ApiKey);

        AgentReading? sent = null;
        var lastSendUtc = DateTime.MinValue;
        var failing = false;

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_o.PostIntervalMs));
        while (await timer.WaitForNextTickAsync(ct))
        {
            var reading = _latest.Get();
            if (reading is null) continue;

            // Serial side quiet for 3 s: send nothing, so the server marks the scale offline.
            if (DateTime.UtcNow - reading.TimestampUtc > TimeSpan.FromSeconds(3)) continue;

            var changed = sent is null || reading.WeightKg != sent.WeightKg || reading.Stable != sent.Stable;
            var heartbeatDue = DateTime.UtcNow - lastSendUtc >= TimeSpan.FromMilliseconds(_o.HeartbeatMs);
            if (!changed && !heartbeatDue) continue;

            try
            {
                using var response = await client.PostAsJsonAsync(_o.ServerUrl, new
                {
                    scaleId = _o.ScaleId,
                    weightKg = reading.WeightKg,
                    stable = reading.Stable,
                    timestamp = reading.TimestampUtc
                }, ct);
                response.EnsureSuccessStatusCode();

                sent = reading;
                lastSendUtc = DateTime.UtcNow;
                if (failing)
                {
                    _log.LogInformation("Server reachable again.");
                    failing = false;
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // Log once per outage, not 4 times a second.
                if (!failing)
                {
                    _log.LogWarning(ex, "Posting to {Url} failed. Will keep retrying.", _o.ServerUrl);
                    failing = true;
                }
            }
        }
    }
}
