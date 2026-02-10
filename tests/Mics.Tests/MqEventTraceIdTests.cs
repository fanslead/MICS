using Google.Protobuf.Reflection;
using Mics.Contracts.Hook.V1;

namespace Mics.Tests;

public sealed class MqEventTraceIdTests
{
    [Fact]
    public void MqEvent_HasTraceIdField_ForEndToEndTracing()
    {
        var field = MqEvent.Descriptor.FindFieldByName("trace_id");
        Assert.NotNull(field);
        Assert.Equal(FieldType.String, field!.FieldType);
    }
}

