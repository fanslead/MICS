namespace Mics.Gateway.Protocol;

internal static class MicsProtocolCodes
{
    // WebSocket Close codes (use 4000-4999 for application-defined codes)
    public const int CloseAuthFailed = 4001;
    public const int CloseTenantInvalid = 4002;
    public const int CloseHeartbeatTimeout = 4100;
    public const int CloseServerDraining = 4200;
    public const int CloseRateLimited = 4429;

    // ServerFrame.error.code
    public const int ErrorInvalidProtobuf = 4400;
}
