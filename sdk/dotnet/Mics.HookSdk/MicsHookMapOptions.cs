namespace Mics.HookSdk;

public sealed record MicsHookMapOptions(Func<string, string?> TenantSecretProvider, bool RequireSign)
{
    public static MicsHookMapOptions Default(Func<string, string?> tenantSecretProvider) =>
        new(tenantSecretProvider, RequireSign: true);
}

