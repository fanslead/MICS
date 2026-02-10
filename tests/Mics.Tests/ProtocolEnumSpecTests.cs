using Mics.Contracts.Message.V1;

namespace Mics.Tests;

public sealed class ProtocolEnumSpecTests
{
    [Fact]
    public void MessageType_NumericValues_MatchSpec()
    {
        Assert.Equal(0, (int)MessageType.SingleChat);
        Assert.Equal(1, (int)MessageType.GroupChat);
    }

    [Fact]
    public void AckStatus_NumericValues_MatchSpec()
    {
        Assert.Equal(0, (int)AckStatus.Sent);
        Assert.Equal(1, (int)AckStatus.Failed);
    }
}

