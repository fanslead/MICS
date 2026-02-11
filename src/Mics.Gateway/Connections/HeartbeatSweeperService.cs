using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Mics.Gateway.Connections;

internal sealed class HeartbeatSweeperService : BackgroundService
{
    private readonly HeartbeatSweeper _sweeper;
    private readonly ILogger<HeartbeatSweeperService> _logger;

    public HeartbeatSweeperService(HeartbeatSweeper sweeper, ILogger<HeartbeatSweeperService> logger)
    {
        _sweeper = sweeper;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _sweeper.SweepTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "heartbeat sweep failed");
            }

            await Task.Delay(1_000, stoppingToken);
        }
    }
}

