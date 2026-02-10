using System.Globalization;

namespace Mics.LoadTester;

internal static class ConsoleReporter
{
    public static async Task RunAsync(LoadStats stats, LoadTestOptions options, CancellationToken cancellationToken)
    {
        var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        long lastHbSent = 0;
        long lastMsgSent = 0;
        long lastDeliveries = 0;
        long lastAcks = 0;

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                var open = Interlocked.Read(ref stats.ConnectionsOpen);
                var ok = Interlocked.Read(ref stats.ConnectionsOk);
                var failed = Interlocked.Read(ref stats.ConnectionsFailed);

                var hbSent = Interlocked.Read(ref stats.HeartbeatSent);
                var msgSent = Interlocked.Read(ref stats.MessagesSent);
                var deliveries = Interlocked.Read(ref stats.MessageDeliveries);
                var acks = Interlocked.Read(ref stats.MessageAcks);

                var hbQps = hbSent - lastHbSent;
                var sendQps = msgSent - lastMsgSent;
                var deliveryQps = deliveries - lastDeliveries;
                var ackQps = acks - lastAcks;
                lastHbSent = hbSent;
                lastMsgSent = msgSent;
                lastDeliveries = deliveries;
                lastAcks = acks;

                Console.WriteLine(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"open={open} ok={ok} fail={failed} mode={options.Mode} hbQps={hbQps} sendQps={sendQps} ackQps={ackQps} deliveryQps={deliveryQps} " +
                        $"p50={stats.DeliveryLatencyMs.PercentileMs(0.50)}ms p90={stats.DeliveryLatencyMs.PercentileMs(0.90)}ms p99={stats.DeliveryLatencyMs.PercentileMs(0.99)}ms"));
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            timer.Dispose();
        }
    }
}

