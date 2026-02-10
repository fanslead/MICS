using System.Linq;

namespace Mics.LoadTester;

public static class LoadRunner
{
    public static async Task<int> RunAsync(LoadTestOptions options, CancellationToken cancellationToken)
    {
        var stats = new LoadStats();

        using var reporterCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var reporterTask = ConsoleReporter.RunAsync(stats, options, reporterCts.Token);

        var tasks = new List<Task>(capacity: options.Connections);

        var rampDelay = options.RampSeconds <= 0
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds(options.RampSeconds / Math.Max(1d, options.Connections));

        for (var i = 0; i < options.Connections; i++)
        {
            var worker = new ConnectionWorker(i, options, stats);
            tasks.Add(worker.RunAsync(cancellationToken));

            if (rampDelay > TimeSpan.Zero && i < options.Connections - 1)
            {
                try
                {
                    await Task.Delay(rampDelay, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            reporterCts.Cancel();
            try { await reporterTask; } catch { }
        }

        Console.WriteLine("---- final ----");
        Console.WriteLine($"connections_ok={stats.ConnectionsOk} connections_failed={stats.ConnectionsFailed} connections_closed={stats.ConnectionsClosed}");
        Console.WriteLine($"send_ok={stats.MessagesSent} send_fail={stats.MessageSendFailed} ack_ok={stats.MessageAcks} ack_fail={stats.MessageAckFailed} deliveries={stats.MessageDeliveries} server_errors={stats.ServerErrors}");
        Console.WriteLine($"connect_p50={stats.ConnectLatencyMs.PercentileMs(0.50)}ms connect_p90={stats.ConnectLatencyMs.PercentileMs(0.90)}ms connect_p99={stats.ConnectLatencyMs.PercentileMs(0.99)}ms");
        Console.WriteLine($"ack_p50={stats.AckLatencyMs.PercentileMs(0.50)}ms ack_p90={stats.AckLatencyMs.PercentileMs(0.90)}ms ack_p99={stats.AckLatencyMs.PercentileMs(0.99)}ms");
        Console.WriteLine($"delivery_p50={stats.DeliveryLatencyMs.PercentileMs(0.50)}ms delivery_p90={stats.DeliveryLatencyMs.PercentileMs(0.90)}ms delivery_p99={stats.DeliveryLatencyMs.PercentileMs(0.99)}ms");

        if (!stats.CloseCodeCounts.IsEmpty)
        {
            Console.WriteLine("close_codes:");
            foreach (var kv in stats.CloseCodeCounts.OrderByDescending(static kv => kv.Value).ThenBy(static kv => kv.Key).Take(10))
            {
                Console.WriteLine($"  {kv.Key}={kv.Value}");
            }
        }

        return 0;
    }
}
