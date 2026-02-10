using Mics.LoadTester;

namespace Mics.Tests;

public sealed class LatencyHistogramTests
{
    [Fact]
    public void PercentileMs_UsesNearestRank()
    {
        var h = new LatencyHistogram(maxMs: 100);
        h.RecordMs(1);
        h.RecordMs(2);
        h.RecordMs(3);
        h.RecordMs(4);
        h.RecordMs(5);

        Assert.Equal(5, h.Count);
        Assert.Equal(3, h.PercentileMs(0.50));
        Assert.Equal(5, h.PercentileMs(0.90));
        Assert.Equal(5, h.PercentileMs(0.99));
    }
}

