using System.Threading.Channels;

namespace Mics.Gateway.Infrastructure.Redis;

internal readonly record struct AdmissionUnregisterWorkItem(
    string TenantId,
    string UserId,
    string DeviceId,
    string ExpectedNodeId,
    string ExpectedConnectionId,
    int Attempt);

internal sealed class AdmissionUnregisterRetryQueue
{
    private readonly Channel<AdmissionUnregisterWorkItem> _channel;
    private int _pending;

    public AdmissionUnregisterRetryQueue(int capacity)
    {
        _channel = Channel.CreateBounded<AdmissionUnregisterWorkItem>(new BoundedChannelOptions(Math.Clamp(capacity, 1, 1_000_000))
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite,
            AllowSynchronousContinuations = false,
        });
    }

    public int Pending => Volatile.Read(ref _pending);

    public ChannelReader<AdmissionUnregisterWorkItem> Reader => _channel.Reader;

    public bool TryEnqueue(AdmissionUnregisterWorkItem item)
    {
        if (!_channel.Writer.TryWrite(item))
        {
            return false;
        }

        Interlocked.Increment(ref _pending);
        return true;
    }

    public void OnDequeued() => Interlocked.Decrement(ref _pending);
}
