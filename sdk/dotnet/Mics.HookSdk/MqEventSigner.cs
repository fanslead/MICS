using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;

namespace Mics.HookSdk;

public static class MqEventSigner
{
    public static string ComputeBase64(string tenantSecret, IMessage payloadForSign)
    {
        ArgumentNullException.ThrowIfNull(payloadForSign);

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(tenantSecret ?? ""));
        var hash = hmac.ComputeHash(payloadForSign.ToByteArray());
        return Convert.ToBase64String(hash);
    }
}

