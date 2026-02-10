using StackExchange.Redis;

namespace Mics.Gateway.Infrastructure.Redis;

internal interface IRedisConnectionFactory : IAsyncDisposable
{
    IConnectionMultiplexer Multiplexer { get; }
}

internal sealed class RedisConnectionFactory : IRedisConnectionFactory
{
    private readonly ConnectionMultiplexer _multiplexer;

    private RedisConnectionFactory(ConnectionMultiplexer multiplexer)
    {
        _multiplexer = multiplexer;
    }

    public IConnectionMultiplexer Multiplexer => _multiplexer;

    public static async Task<RedisConnectionFactory> ConnectAsync(string configuration, CancellationToken cancellationToken)
    {
        var options = ConfigurationOptions.Parse(configuration);
        options.AbortOnConnectFail = false;
        options.AsyncTimeout = 2_000;
        options.ConnectTimeout = 2_000;
        options.ReconnectRetryPolicy = new ExponentialRetry(100);

        var mux = await ConnectionMultiplexer.ConnectAsync(options);
        await mux.ConfigureAsync();

        cancellationToken.ThrowIfCancellationRequested();
        return new RedisConnectionFactory(mux);
    }

    public async ValueTask DisposeAsync()
    {
        await _multiplexer.CloseAsync();
        _multiplexer.Dispose();
    }
}

