using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;
using Mics.Contracts.Hook.V1;
using Mics.Contracts.Message.V1;
using Mics.HookSdk;

namespace Mics.Tests;

public sealed class HookSdkSigningTests
{
    [Fact]
    public void HookSigner_ComputeBase64_MatchesReferenceAlgorithm()
    {
        var meta = new HookMeta { TenantId = "t1", RequestId = "r1", TimestampMs = 123, Sign = "" };
        var req = new CheckMessageRequest
        {
            Meta = meta,
            Message = new MessageRequest { TenantId = "t1", UserId = "u1", DeviceId = "d1", MsgId = "m1", MsgType = MessageType.SingleChat, ToUserId = "u2" }
        };

        var payload = req.Clone();
        payload.Meta.Sign = "";

        var expected = ReferenceHookSign("secret", meta, payload);
        var actual = HookSigner.ComputeBase64("secret", meta, payload);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void HookVerifier_Verify_ReturnsTrueForValidSignature()
    {
        var meta = new HookMeta { TenantId = "t1", RequestId = "r1", TimestampMs = 123, Sign = "" };
        var req = new AuthRequest { Meta = meta, Token = "valid:u1", DeviceId = "d1" };
        var payload = req.Clone();
        payload.Meta.Sign = "";
        meta.Sign = HookSigner.ComputeBase64("secret", meta, payload);

        Assert.True(HookVerifier.Verify("secret", meta, payload));
    }

    [Fact]
    public void MqEventSigner_ComputeBase64_MatchesReferenceAlgorithm()
    {
        var evt = new MqEvent
        {
            TenantId = "t1",
            EventType = EventType.SingleChatMsg,
            MsgId = "m1",
            UserId = "u1",
            DeviceId = "d1",
            ToUserId = "u2",
            GroupId = "",
            EventData = ByteString.CopyFrom(new byte[] { 1, 2, 3 }),
            Timestamp = 999,
            NodeId = "n1",
            Sign = ""
        };
        var payload = evt.Clone();
        payload.Sign = "";

        var expected = ReferenceMqSign("secret", payload);
        var actual = MqEventSigner.ComputeBase64("secret", payload);
        Assert.Equal(expected, actual);
    }

    private static string ReferenceHookSign(string secret, HookMeta meta, IMessage payloadForSign)
    {
        var key = Encoding.UTF8.GetBytes(secret);
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

    private static string ReferenceMqSign(string secret, IMessage payloadForSign)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(payloadForSign.ToByteArray());
        return Convert.ToBase64String(hash);
    }
}

