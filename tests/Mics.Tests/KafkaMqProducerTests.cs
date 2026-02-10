using Confluent.Kafka;
using Mics.Gateway.Mq;

namespace Mics.Tests;

public sealed class KafkaMqProducerTests
{
    private sealed class FakeProducer : IProducer<byte[], byte[]>
    {
        public required Func<string, Message<byte[], byte[]>, CancellationToken, Task<DeliveryResult<byte[], byte[]>>> OnProduceAsync { get; init; }

        public Handle Handle => throw new NotImplementedException();
        public string Name => "fake";

        public void Dispose()
        {
        }

        public int AddBrokers(string brokers) => throw new NotImplementedException();
        public void Flush(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public int Flush(TimeSpan timeout) => throw new NotImplementedException();
        public void InitTransactions(TimeSpan timeout) => throw new NotImplementedException();
        public void BeginTransaction() => throw new NotImplementedException();
        public void CommitTransaction() => throw new NotImplementedException();
        public void CommitTransaction(TimeSpan timeout) => throw new NotImplementedException();
        public void AbortTransaction() => throw new NotImplementedException();
        public void AbortTransaction(TimeSpan timeout) => throw new NotImplementedException();
        public void SendOffsetsToTransaction(IEnumerable<TopicPartitionOffset> offsets, IConsumerGroupMetadata groupMetadata, TimeSpan timeout) => throw new NotImplementedException();

        public Task<DeliveryResult<byte[], byte[]>> ProduceAsync(string topic, Message<byte[], byte[]> message, CancellationToken cancellationToken = default) =>
            OnProduceAsync(topic, message, cancellationToken);

        public Task<DeliveryResult<byte[], byte[]>> ProduceAsync(TopicPartition topicPartition, Message<byte[], byte[]> message, CancellationToken cancellationToken = default) =>
            OnProduceAsync(topicPartition.Topic, message, cancellationToken);

        public void Produce(string topic, Message<byte[], byte[]> message, Action<DeliveryReport<byte[], byte[]>>? deliveryHandler = null) => throw new NotImplementedException();
        public void Produce(TopicPartition topicPartition, Message<byte[], byte[]> message, Action<DeliveryReport<byte[], byte[]>>? deliveryHandler = null) => throw new NotImplementedException();
        public int Poll(TimeSpan timeout) => 0;

        public void SetSaslCredentials(string username, string password) => throw new NotImplementedException();
    }

    [Fact]
    public async Task ProduceAsync_ReturnsTrue_WhenPersisted()
    {
        var fake = new FakeProducer
        {
            OnProduceAsync = (topic, msg, _) =>
            {
                Assert.Equal("t", topic);
                Assert.Equal(new byte[] { 1 }, msg.Key);
                Assert.Equal(new byte[] { 2 }, msg.Value);
                return Task.FromResult(new DeliveryResult<byte[], byte[]> { Status = PersistenceStatus.Persisted });
            }
        };

        var producer = new KafkaMqProducer(fake);
        var ok = await producer.ProduceAsync("t", new byte[] { 1 }, new byte[] { 2 }, CancellationToken.None);
        Assert.True(ok);
    }

    [Fact]
    public void Producer_IsDisposable_ForGracefulShutdown()
    {
        var fake = new FakeProducer
        {
            OnProduceAsync = (_, _, _) => Task.FromResult(new DeliveryResult<byte[], byte[]> { Status = PersistenceStatus.Persisted })
        };

        var producer = new KafkaMqProducer(fake);
        Assert.IsAssignableFrom<IDisposable>(producer);
    }

    [Fact]
    public async Task ProduceAsync_ReturnsFalse_OnProduceException()
    {
        var fake = new FakeProducer
        {
            OnProduceAsync = (_, _, _) =>
                throw new ProduceException<byte[], byte[]>(new Error(ErrorCode.Local_MsgTimedOut), new DeliveryResult<byte[], byte[]>())
        };

        var producer = new KafkaMqProducer(fake);
        var ok = await producer.ProduceAsync("t", new byte[] { 1 }, new byte[] { 2 }, CancellationToken.None);
        Assert.False(ok);
    }
}
