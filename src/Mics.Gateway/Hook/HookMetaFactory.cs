using Mics.Contracts.Hook.V1;
using Mics.Gateway.Infrastructure;

namespace Mics.Gateway.Hook;

internal interface IHookMetaFactory
{
    HookMeta Create(string tenantId);
}

internal sealed class DefaultHookMetaFactory : IHookMetaFactory
{
    private readonly TimeProvider _timeProvider;
    private readonly ITraceContext _traceContext;

    public DefaultHookMetaFactory(TimeProvider timeProvider, ITraceContext traceContext)
    {
        _timeProvider = timeProvider;
        _traceContext = traceContext;
    }

    public HookMeta Create(string tenantId) =>
        new()
        {
            TenantId = tenantId,
            RequestId = Guid.NewGuid().ToString("N"),
            TimestampMs = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds(),
            Sign = "",
            TraceId = _traceContext.TraceId ?? "",
        };
}
