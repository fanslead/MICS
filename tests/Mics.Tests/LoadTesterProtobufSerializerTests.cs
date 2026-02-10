using Google.Protobuf;
using Mics.Contracts.Message.V1;
using Mics.LoadTester;

namespace Mics.Tests;

public sealed class LoadTesterProtobufSerializerTests
{
    [Fact]
    public void Serialize_RoundTripsMessageWithoutExtraBytes()
    {
        var frame = new ClientFrame
        {
            HeartbeatPing = new HeartbeatPing { TimestampMs = 123 }
        };

        using var bytes = PooledProtobufSerializer.Serialize(frame);
        Assert.Equal(frame.CalculateSize(), bytes.Length);

        var cis = new CodedInputStream(bytes.Buffer, 0, bytes.Length);
        var parsed = ClientFrame.Parser.ParseFrom(cis);
        Assert.Equal(frame, parsed);
    }
}

