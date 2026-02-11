using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace Mics.Gateway.Metrics;

internal sealed class MetricsRegistry
{
    private readonly ConcurrentDictionary<string, long> _gauges = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _counters = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, HistogramSeries> _histograms = new(StringComparer.Ordinal);

    private static readonly double[] DefaultHistogramBucketsMs =
    {
        1, 2, 5, 10, 25, 50, 100, 150, 200, 250, 500, 1000, 2500, 5000, 10_000,
    };

    private sealed class HistogramSeries
    {
        public long[] Buckets = new long[DefaultHistogramBucketsMs.Length + 1]; // last is +Inf
        public long SumMicros;
        public long Count;
    }

    public void GaugeSet(string name, long value, params (string Key, string Value)[] labels)
    {
        _gauges[FormatKey(name, labels)] = value;
    }

    public void CounterInc(string name, long delta, params (string Key, string Value)[] labels)
    {
        _counters.AddOrUpdate(FormatKey(name, labels), delta, (_, cur) => cur + delta);
    }

    public void HistogramObserve(string name, double valueMs, params (string Key, string Value)[] labels)
    {
        if (double.IsNaN(valueMs) || double.IsInfinity(valueMs) || valueMs < 0)
        {
            valueMs = 0;
        }

        var key = FormatKey(name, labels);
        var series = _histograms.GetOrAdd(key, _ => new HistogramSeries());

        var bucketIndex = DefaultHistogramBucketsMs.Length;
        for (var i = 0; i < DefaultHistogramBucketsMs.Length; i++)
        {
            if (valueMs <= DefaultHistogramBucketsMs[i])
            {
                bucketIndex = i;
                break;
            }
        }

        Interlocked.Increment(ref series.Buckets[bucketIndex]);
        Interlocked.Increment(ref series.Count);
        Interlocked.Add(ref series.SumMicros, (long)(valueMs * 1000.0));
    }

    public string CollectPrometheusText()
    {
        var sb = new StringBuilder(8 * 1024);

        foreach (var (k, v) in _gauges.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            sb.Append(k).Append(' ').Append(v).Append('\n');
        }

        foreach (var (k, v) in _counters.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            sb.Append(k).Append(' ').Append(v).Append('\n');
        }

        foreach (var (k, series) in _histograms.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var (name, labels) = SplitKey(k);

            long cumulative = 0;
            for (var i = 0; i < DefaultHistogramBucketsMs.Length; i++)
            {
                cumulative += Volatile.Read(ref series.Buckets[i]);
                sb.Append(FormatHistogramBucketKey(name, labels, DefaultHistogramBucketsMs[i].ToString(CultureInfo.InvariantCulture)))
                    .Append(' ')
                    .Append(cumulative)
                    .Append('\n');
            }

            cumulative += Volatile.Read(ref series.Buckets[DefaultHistogramBucketsMs.Length]);
            sb.Append(FormatHistogramBucketKey(name, labels, "+Inf"))
                .Append(' ')
                .Append(cumulative)
                .Append('\n');

            var sumMs = Volatile.Read(ref series.SumMicros) / 1000.0;
            var count = Volatile.Read(ref series.Count);

            sb.Append(FormatHistogramSimpleKey(name, labels, "sum"))
                .Append(' ')
                .Append(sumMs.ToString("0.###", CultureInfo.InvariantCulture))
                .Append('\n');
            sb.Append(FormatHistogramSimpleKey(name, labels, "count"))
                .Append(' ')
                .Append(count)
                .Append('\n');
        }

        return sb.ToString();
    }

    private static (string Name, string? Labels) SplitKey(string key)
    {
        var idx = key.IndexOf('{', StringComparison.Ordinal);
        if (idx < 0)
        {
            return (key, null);
        }

        // key is name{...}
        var name = key[..idx];
        var labels = key[(idx + 1)..^1];
        return (name, labels);
    }

    private static string FormatHistogramBucketKey(string name, string? labels, string le)
    {
        var metric = name + "_bucket";
        if (string.IsNullOrEmpty(labels))
        {
            return $"{metric}{{le=\"{Escape(le)}\"}}";
        }

        return $"{metric}{{{labels},le=\"{Escape(le)}\"}}";
    }

    private static string FormatHistogramSimpleKey(string name, string? labels, string suffix)
    {
        var metric = name + "_" + suffix;
        if (string.IsNullOrEmpty(labels))
        {
            return metric;
        }

        return $"{metric}{{{labels}}}";
    }

    private static string FormatKey(string name, (string Key, string Value)[] labels)
    {
        if (labels.Length == 0)
        {
            return name;
        }

        var sb = new StringBuilder(name.Length + 32);
        sb.Append(name).Append('{');
        for (var i = 0; i < labels.Length; i++)
        {
            var (k, v) = labels[i];
            if (i > 0) sb.Append(',');
            sb.Append(k).Append("=\"").Append(Escape(v)).Append('"');
        }
        sb.Append('}');
        return sb.ToString();
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}

