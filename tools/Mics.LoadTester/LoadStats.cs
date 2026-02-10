using System.Collections.Concurrent;

namespace Mics.LoadTester;

public sealed class LoadStats
{
    public long ConnectionsAttempted;
    public long ConnectionsOpen;
    public long ConnectionsOk;
    public long ConnectionsFailed;
    public long ConnectionsClosed;

    public long HeartbeatSent;
    public long HeartbeatPong;

    public long MessagesSent;
    public long MessageSendFailed;
    public long MessageAcks;
    public long MessageAckFailed;
    public long MessageDeliveries;
    public long ServerErrors;

    // Close codes observed from server close frames. Key is the integer close code.
    public ConcurrentDictionary<int, long> CloseCodeCounts { get; } = new();

    public LatencyHistogram ConnectLatencyMs { get; } = new(maxMs: 10_000);
    public LatencyHistogram AckLatencyMs { get; } = new(maxMs: 10_000);
    public LatencyHistogram DeliveryLatencyMs { get; } = new(maxMs: 10_000);
}
