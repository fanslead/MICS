using System.Collections.Concurrent;
using System.Text;

namespace Mics.Gateway.Metrics;

internal sealed class MetricsRegistry
{
    private readonly ConcurrentDictionary<string, long> _gauges = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _counters = new(StringComparer.Ordinal);

    public void GaugeSet(string name, long value, params (string Key, string Value)[] labels)
    {
        _gauges[FormatKey(name, labels)] = value;
    }

    public void CounterInc(string name, long delta, params (string Key, string Value)[] labels)
    {
        _counters.AddOrUpdate(FormatKey(name, labels), delta, (_, cur) => cur + delta);
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

        return sb.ToString();
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

