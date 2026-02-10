using System.Reflection;

namespace Mics.Tests;

public sealed class MqHookTests
{
    [Fact]
    public void TopicNames_AreTenantIsolated_AndFollowSpec()
    {
        var asm = typeof(Mics.Gateway.Hook.HookClient).Assembly;
        var type = asm.GetType("Mics.Gateway.Mq.MqTopicName", throwOnError: false);
        Assert.NotNull(type);

        var eventTopic = InvokeStaticString(type!, "GetEventTopic", "t1");
        var dlqTopic = InvokeStaticString(type!, "GetDlqTopic", "t1");

        Assert.Equal("im-mics-t1-event", eventTopic);
        Assert.Equal("im-mics-t1-event-dlq", dlqTopic);
    }

    [Fact]
    public void DispatcherTypes_Exist()
    {
        var asm = typeof(Mics.Gateway.Hook.HookClient).Assembly;
        Assert.NotNull(asm.GetType("Mics.Gateway.Mq.IMqProducer", throwOnError: false));
        Assert.NotNull(asm.GetType("Mics.Gateway.Mq.KafkaMqProducer", throwOnError: false));
        Assert.NotNull(asm.GetType("Mics.Gateway.Mq.NoopMqProducer", throwOnError: false));
        Assert.NotNull(asm.GetType("Mics.Gateway.Mq.MqEventDispatcherOptions", throwOnError: false));
        Assert.NotNull(asm.GetType("Mics.Gateway.Mq.MqEventDispatcher", throwOnError: false));
        Assert.NotNull(asm.GetType("Mics.Gateway.Mq.MqEventDispatcherService", throwOnError: false));
        var factory = asm.GetType("Mics.Gateway.Mq.MqEventFactory", throwOnError: false);
        Assert.NotNull(factory);
        Assert.NotNull(factory!.GetMethod("CreateConnectOnline", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static));
        Assert.NotNull(factory.GetMethod("CreateConnectOffline", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static));
        Assert.NotNull(factory.GetMethod("CreateForMessage", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static));
    }

    private static string InvokeStaticString(Type type, string method, params object[] args)
    {
        var mi = type.GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(mi);
        var res = mi!.Invoke(obj: null, args);
        Assert.IsType<string>(res);
        return (string)res;
    }
}
