using Mics.Gateway.Config;
using Microsoft.Extensions.Configuration;

namespace Mics.Tests;

public sealed class KafkaOptionsTests
{
    [Fact]
    public void Load_ReadsKafkaBootstrapServers()
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["REDIS__CONNECTION"] = "localhost:6379",
                ["KAFKA__BOOTSTRAP_SERVERS"] = "localhost:9092",
                ["KAFKA_MAX_ATTEMPTS"] = "7",
                ["KAFKA_QUEUE_CAPACITY"] = "123",
                ["KAFKA_RETRY_BACKOFF_MS"] = "42",
                ["KAFKA_IDLE_DELAY_MS"] = "3",
            })
            .Build();

        var opts = GatewayOptions.Load(cfg);
        var prop = opts.GetType().GetProperty("KafkaBootstrapServers");
        Assert.NotNull(prop);

        var value = prop!.GetValue(opts) as string;
        Assert.Equal("localhost:9092", value);

        var maxAttemptsProp = opts.GetType().GetProperty("KafkaMaxAttempts");
        Assert.NotNull(maxAttemptsProp);
        Assert.Equal(7, maxAttemptsProp!.GetValue(opts));

        var qProp = opts.GetType().GetProperty("KafkaQueueCapacity");
        Assert.NotNull(qProp);
        Assert.Equal(123, qProp!.GetValue(opts));

        var backoffProp = opts.GetType().GetProperty("KafkaRetryBackoffMs");
        Assert.NotNull(backoffProp);
        Assert.Equal(42, backoffProp!.GetValue(opts));

        var idleProp = opts.GetType().GetProperty("KafkaIdleDelayMs");
        Assert.NotNull(idleProp);
        Assert.Equal(3, idleProp!.GetValue(opts));
    }
}
