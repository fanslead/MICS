namespace Mics.LoadTester;

public sealed class LatencyHistogram
{
    private readonly int _maxMs;
    private readonly int[] _buckets;
    private long _count;

    public LatencyHistogram(int maxMs)
    {
        if (maxMs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxMs), "maxMs must be > 0");
        }

        _maxMs = maxMs;
        _buckets = new int[maxMs + 1];
    }

    public void RecordMs(int ms)
    {
        if (ms < 0)
        {
            ms = 0;
        }

        if (ms > _maxMs)
        {
            ms = _maxMs;
        }

        Interlocked.Increment(ref _buckets[ms]);
        Interlocked.Increment(ref _count);
    }

    public long Count => Interlocked.Read(ref _count);

    public int PercentileMs(double percentile)
    {
        if (Count == 0)
        {
            return 0;
        }

        if (double.IsNaN(percentile) || percentile <= 0)
        {
            return 0;
        }

        if (percentile >= 1)
        {
            return _maxMs;
        }

        var targetRank = (long)Math.Ceiling(percentile * Count);
        if (targetRank <= 0)
        {
            return 0;
        }

        long cumulative = 0;
        for (var ms = 0; ms <= _maxMs; ms++)
        {
            cumulative += Volatile.Read(ref _buckets[ms]);
            if (cumulative >= targetRank)
            {
                return ms;
            }
        }

        return _maxMs;
    }
}
