using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DataFlow.Metrics;

/// <summary>
/// Once per second: closes the current bucket of every device, samples the network
/// interfaces, and evicts idle devices.
/// </summary>
public sealed class SamplerHostedService : BackgroundService
{
    private readonly ThroughputStore _store;
    private readonly ILogger<SamplerHostedService> _logger;
    private readonly NetworkInterfaceSampler _nic = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="SamplerHostedService"/> class.
    /// </summary>
    /// <param name="store">The throughput store.</param>
    /// <param name="logger">The logger.</param>
    public SamplerHostedService(ThroughputStore store, ILogger<SamplerHostedService> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DataFlow sampler started ({History}s history)", _store.HistorySeconds);
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(ThroughputStore.IntervalMs));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                var now = DateTimeOffset.UtcNow;
                long sent = 0;
                long received = 0;
                try
                {
                    (sent, received) = _nic.Sample(Plugin.CurrentConfiguration.NetworkInterfaces ?? [], now);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "DataFlow: network interface sampling failed");
                }

                _store.Tick(now.ToUnixTimeMilliseconds(), sent, received);
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }
}
