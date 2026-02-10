namespace Mics.Client;

public sealed record MicsSession(
    string TenantId,
    string UserId,
    string DeviceId,
    string NodeId,
    string TraceId);

