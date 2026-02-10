using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;
using Mics.Contracts.Hook.V1;

namespace Mics.HookSdk;

public static class HookSigner
{
    public static string ComputeBase64(string tenantSecret, HookMeta meta, IMessage payloadForSign)
    {
        ArgumentNullException.ThrowIfNull(meta);
        ArgumentNullException.ThrowIfNull(payloadForSign);

        var key = Encoding.UTF8.GetBytes(tenantSecret ?? "");
        var requestIdBytes = Encoding.UTF8.GetBytes(meta.RequestId ?? "");
        var tsBytes = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(tsBytes, meta.TimestampMs);

        using var hmac = new HMACSHA256(key);
        var payloadBytes = payloadForSign.ToByteArray();
        hmac.TransformBlock(payloadBytes, 0, payloadBytes.Length, null, 0);
        hmac.TransformBlock(requestIdBytes, 0, requestIdBytes.Length, null, 0);
        hmac.TransformFinalBlock(tsBytes, 0, tsBytes.Length);

        return Convert.ToBase64String(hmac.Hash!);
    }
}

