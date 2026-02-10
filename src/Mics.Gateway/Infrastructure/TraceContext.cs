using System.Threading;

namespace Mics.Gateway.Infrastructure;

internal interface ITraceContext
{
    string? TraceId { get; set; }
}

internal sealed class TraceContext : ITraceContext
{
    private readonly AsyncLocal<string?> _traceId = new();

    public string? TraceId
    {
        get => _traceId.Value;
        set => _traceId.Value = value;
    }
}

