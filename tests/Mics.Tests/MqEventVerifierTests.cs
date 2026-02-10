using Google.Protobuf;
using Mics.Contracts.Hook.V1;
using Mics.HookSdk;

namespace Mics.Tests;

public sealed class MqEventVerifierTests
{
    [Fact]
    public void Verify_ReturnsTrue_WhenSignMatches()
    {
        var evt = new MqEvent
        {
            TenantId = "t1",
            EventType = EventType.ConnectOnline,
            MsgId = "",
            UserId = "u1",
            DeviceId = "d1",
            ToUserId = "",
            GroupId = "",
            EventData = ByteString.CopyFrom(new byte[] { 1, 2, 3 }),
            Timestamp = 1,
            NodeId = "n1",
            Sign = ""
        };

        var payload = evt.Clone();
        payload.Sign = "";
        evt.Sign = MqEventSigner.ComputeBase64("secret", payload);

        Assert.True(MqEventVerifier.Verify("secret", evt));
    }
}

