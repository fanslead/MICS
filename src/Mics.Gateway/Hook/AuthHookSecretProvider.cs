namespace Mics.Gateway.Hook;

internal interface IAuthHookSecretProvider
{
    bool TryGet(string tenantId, out string secret);
}

internal sealed class AuthHookSecretProvider : IAuthHookSecretProvider
{
    private readonly IReadOnlyDictionary<string, string> _secrets;

    public AuthHookSecretProvider(IReadOnlyDictionary<string, string> secrets)
    {
        _secrets = secrets;
    }

    public bool TryGet(string tenantId, out string secret) =>
        _secrets.TryGetValue(tenantId, out secret!) && !string.IsNullOrWhiteSpace(secret);
}

