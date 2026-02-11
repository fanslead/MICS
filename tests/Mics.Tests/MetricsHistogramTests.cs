using Mics.Gateway.Metrics;

namespace Mics.Tests;

public sealed class MetricsHistogramTests
{
    [Fact]
    public void HistogramObserve_EmitsBucketSumCountLines()
    {
        var metrics = new MetricsRegistry();

        metrics.HistogramObserve("mics_hook_duration_ms", 12.3, ("tenant", "t1"), ("op", "Auth"));

        var text = metrics.CollectPrometheusText();

        Assert.Contains("mics_hook_duration_ms_bucket{tenant=\"t1\",op=\"Auth\",le=\"10\"} 0", text);
        Assert.Contains("mics_hook_duration_ms_bucket{tenant=\"t1\",op=\"Auth\",le=\"25\"} 1", text);
        Assert.Contains("mics_hook_duration_ms_bucket{tenant=\"t1\",op=\"Auth\",le=\"+Inf\"} 1", text);
        Assert.Contains("mics_hook_duration_ms_sum{tenant=\"t1\",op=\"Auth\"} 12.3", text);
        Assert.Contains("mics_hook_duration_ms_count{tenant=\"t1\",op=\"Auth\"} 1", text);
    }
}
