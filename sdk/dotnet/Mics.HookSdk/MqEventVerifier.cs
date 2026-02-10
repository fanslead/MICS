using System.Security.Cryptography;
using Google.Protobuf;
using Mics.Contracts.Hook.V1;

namespace Mics.HookSdk;

public static class MqEventVerifier
{
    public static bool Verify(string tenantSecret, MqEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        if (string.IsNullOrWhiteSpace(evt.Sign))
        {
            return false;
        }

        byte[] actual;
        try
        {
            actual = Convert.FromBase64String(evt.Sign);
        }
        catch (FormatException)
        {
            return false;
        }

        var payload = evt.Clone();
        payload.Sign = "";
        var expected = Convert.FromBase64String(MqEventSigner.ComputeBase64(tenantSecret, payload));
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}

