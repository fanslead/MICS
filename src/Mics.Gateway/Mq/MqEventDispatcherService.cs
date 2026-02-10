using Microsoft.Extensions.Hosting;

namespace Mics.Gateway.Mq;

internal sealed class MqEventDispatcherService : BackgroundService
{
    private readonly MqEventDispatcher _dispatcher;

    public MqEventDispatcherService(MqEventDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => _dispatcher.RunAsync(stoppingToken);
}

