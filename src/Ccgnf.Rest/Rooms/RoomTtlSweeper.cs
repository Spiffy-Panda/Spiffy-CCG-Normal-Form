using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ccgnf.Rest.Rooms;

/// <summary>
/// Periodically sweeps <see cref="RoomStore"/> and evicts rooms past their
/// TTL. Configurable via <c>CCGNF_ROOM_TTL_SECONDS</c> (default 600) and
/// <c>CCGNF_ROOM_SWEEP_SECONDS</c> (default 30).
/// </summary>
public sealed class RoomTtlSweeper : BackgroundService
{
    private readonly RoomStore _store;
    private readonly ILogger<RoomTtlSweeper> _log;
    private readonly TimeSpan _ttl;
    private readonly TimeSpan _interval;

    public RoomTtlSweeper(RoomStore store, ILogger<RoomTtlSweeper> log)
    {
        _store = store;
        _log = log;
        _ttl = TimeSpan.FromSeconds(ReadIntEnv("CCGNF_ROOM_TTL_SECONDS", 600));
        _interval = TimeSpan.FromSeconds(ReadIntEnv("CCGNF_ROOM_SWEEP_SECONDS", 30));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation(
            "RoomTtlSweeper started: ttl={Ttl}s, interval={Interval}s.",
            _ttl.TotalSeconds, _interval.TotalSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _store.EvictExpiredAsync(_ttl);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "RoomTtlSweeper sweep failed.");
            }
            try
            {
                await Task.Delay(_interval, stoppingToken);
            }
            catch (TaskCanceledException) { break; }
        }
    }

    private static int ReadIntEnv(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var v) ? Math.Max(1, v) : fallback;
}
