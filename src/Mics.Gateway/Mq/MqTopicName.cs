namespace Mics.Gateway.Mq;

internal static class MqTopicName
{
    public static string GetEventTopic(string tenantId) => $"im-mics-{tenantId}-event";

    public static string GetDlqTopic(string tenantId) => $"im-mics-{tenantId}-event-dlq";
}

