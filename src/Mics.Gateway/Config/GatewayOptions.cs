using System.Text.Json;

namespace Mics.Gateway.Config;

internal sealed class GatewayOptions
{
    public string NodeId { get; init; } = "";
    public string PublicEndpoint { get; init; } = "";
    public string RedisConnection { get; init; } = "";
    public TimeSpan WebSocketKeepAliveInterval { get; init; } = TimeSpan.FromSeconds(30);

    // Safety knobs (cluster-level defaults; tenant-specific overrides may be added later via /auth config).
    public int MaxMessageBytes { get; init; } = 1_048_576;
    public int OfflineBufferMaxMessagesPerUser { get; init; } = 128;
    public int OfflineBufferMaxBytesPerUser { get; init; } = 1_048_576;

    public int GroupRouteChunkSize { get; init; } = 256;
    public int GroupOfflineBufferMaxUsers { get; init; } = 1024;
    public int GroupMembersMaxUsers { get; init; } = 200_000;
    public string KafkaBootstrapServers { get; init; } = "";
    public int KafkaMaxAttempts { get; init; } = 3;
    public int KafkaQueueCapacity { get; init; } = 50_000;
    public int KafkaRetryBackoffMs { get; init; } = 50;
    public int KafkaIdleDelayMs { get; init; } = 5;

    public TimeSpan HookTimeout { get; init; } = TimeSpan.FromMilliseconds(150);
    public TimeSpan HookQueueTimeout { get; init; } = TimeSpan.FromMilliseconds(10);
    public int HookMaxConcurrencyDefault { get; init; } = 32;
    public int HookBreakerFailureThreshold { get; init; } = 5;
    public TimeSpan HookBreakerOpenDuration { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan NodeTtl { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan DrainTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public Dictionary<string, string> TenantAuthMap { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> TenantHookSecrets { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> TenantHookMaxConcurrency { get; init; } = new(StringComparer.Ordinal);
    public bool HookSignRequired { get; init; }

    public static GatewayOptions Load(IConfiguration config)
    {
        var nodeId = config["NODE_ID"] ?? Environment.MachineName;
        var publicEndpoint = config["PUBLIC_ENDPOINT"] ?? "";
        var redis = config["REDIS__CONNECTION"] ?? config["Redis:Connection"] ?? "";
        var wsKeepAliveSeconds = Math.Clamp(config.GetValue("WS_KEEPALIVE_INTERVAL_SECONDS", 30), 0, 600);
        var maxMessageBytes = Math.Clamp(config.GetValue("MAX_MESSAGE_BYTES", 1_048_576), 0, 64 * 1024 * 1024);
        var offlineMaxMessagesPerUser = Math.Clamp(config.GetValue("OFFLINE_BUFFER_MAX_MESSAGES_PER_USER", 128), 0, 1_000_000);
        var offlineMaxBytesPerUser = Math.Clamp(config.GetValue("OFFLINE_BUFFER_MAX_BYTES_PER_USER", 1_048_576), 0, 256 * 1024 * 1024);
        var groupChunk = Math.Clamp(config.GetValue("GROUP_ROUTE_CHUNK_SIZE", 256), 1, 4096);
        var groupOfflineMax = Math.Clamp(config.GetValue("GROUP_OFFLINE_BUFFER_MAX_USERS", 1024), 0, 1_000_000);
        var groupMembersMax = Math.Clamp(config.GetValue("GROUP_MEMBERS_MAX_USERS", 200_000), 1, 5_000_000);
        var kafka = config["KAFKA__BOOTSTRAP_SERVERS"] ?? config["Kafka:BootstrapServers"] ?? "";
        var kafkaMaxAttempts = Math.Clamp(config.GetValue("KAFKA_MAX_ATTEMPTS", 3), 1, 10);
        var kafkaQueueCapacity = Math.Clamp(config.GetValue("KAFKA_QUEUE_CAPACITY", 50_000), 1, 1_000_000);
        var kafkaRetryBackoffMs = Math.Clamp(config.GetValue("KAFKA_RETRY_BACKOFF_MS", 50), 0, 10_000);
        var kafkaIdleDelayMs = Math.Clamp(config.GetValue("KAFKA_IDLE_DELAY_MS", 5), 0, 1_000);

        var tenantAuthMap = LoadTenantAuthMap(config);
        var tenantHookSecrets = LoadTenantHookSecrets(config);
        var hookSignRequired = config.GetValue("HOOK_SIGN_REQUIRED", false) || config.GetValue("Hook:SignRequired", false);
        var hookMaxConcurrencyDefault = config.GetValue("HOOK_MAX_CONCURRENCY", 32) > 0 ? config.GetValue("HOOK_MAX_CONCURRENCY", 32) : 32;
        var hookQueueTimeoutMs = config.GetValue("HOOK_QUEUE_TIMEOUT_MS", 10);
        var tenantHookMaxConcurrency = LoadTenantHookMaxConcurrency(config);
        var breakerFailureThreshold = Math.Clamp(config.GetValue("HOOK_BREAKER_FAILURE_THRESHOLD", 5), 1, 100);
        var breakerOpenMs = Math.Clamp(config.GetValue("HOOK_BREAKER_OPEN_MS", 5_000), 0, 60_000);
        var drainTimeoutSeconds = Math.Clamp(config.GetValue("DRAIN_TIMEOUT_SECONDS", 10), 0, 600);

        return new GatewayOptions
        {
            NodeId = nodeId,
            PublicEndpoint = publicEndpoint,
            RedisConnection = redis,
            WebSocketKeepAliveInterval = wsKeepAliveSeconds == 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(wsKeepAliveSeconds),
            MaxMessageBytes = maxMessageBytes,
            OfflineBufferMaxMessagesPerUser = offlineMaxMessagesPerUser,
            OfflineBufferMaxBytesPerUser = offlineMaxBytesPerUser,
            GroupRouteChunkSize = groupChunk,
            GroupOfflineBufferMaxUsers = groupOfflineMax,
            GroupMembersMaxUsers = groupMembersMax,
            KafkaBootstrapServers = kafka,
            KafkaMaxAttempts = kafkaMaxAttempts,
            KafkaQueueCapacity = kafkaQueueCapacity,
            KafkaRetryBackoffMs = kafkaRetryBackoffMs,
            KafkaIdleDelayMs = kafkaIdleDelayMs,
            TenantAuthMap = tenantAuthMap,
            TenantHookSecrets = tenantHookSecrets,
            HookSignRequired = hookSignRequired,
            HookMaxConcurrencyDefault = hookMaxConcurrencyDefault,
            HookQueueTimeout = TimeSpan.FromMilliseconds(Math.Clamp(hookQueueTimeoutMs, 0, 10_000)),
            TenantHookMaxConcurrency = tenantHookMaxConcurrency,
            HookBreakerFailureThreshold = breakerFailureThreshold,
            HookBreakerOpenDuration = TimeSpan.FromMilliseconds(breakerOpenMs),
            DrainTimeout = drainTimeoutSeconds == 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(drainTimeoutSeconds),
        };
    }

    private static Dictionary<string, string> LoadTenantAuthMap(IConfiguration config)
    {
        // 1) Env: TENANT_AUTH_MAP='{"t1":"http://hook:8081"}'
        var envJson = config["TENANT_AUTH_MAP"];
        if (!string.IsNullOrWhiteSpace(envJson))
        {
            var map = JsonSerializer.Deserialize(envJson, GatewayJsonContext.Default.DictionaryStringString);
            return map is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(map, StringComparer.Ordinal);
        }

        // 2) Config section: Tenants:AuthMap:{TenantId}=url
        var section = config.GetSection("Tenants:AuthMap");
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var child in section.GetChildren())
        {
            if (!string.IsNullOrWhiteSpace(child.Key) && !string.IsNullOrWhiteSpace(child.Value))
            {
                dict[child.Key] = child.Value!;
            }
        }

        return dict;
    }

    private static Dictionary<string, string> LoadTenantHookSecrets(IConfiguration config)
    {
        // 1) Env: TENANT_HOOK_SECRETS='{"t1":"secret"}'
        var envJson = config["TENANT_HOOK_SECRETS"];
        if (!string.IsNullOrWhiteSpace(envJson))
        {
            var map = JsonSerializer.Deserialize(envJson, GatewayJsonContext.Default.DictionaryStringString);
            return map is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(map, StringComparer.Ordinal);
        }

        // 2) Config section: Tenants:HookSecrets:{TenantId}=secret
        var section = config.GetSection("Tenants:HookSecrets");
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var child in section.GetChildren())
        {
            if (!string.IsNullOrWhiteSpace(child.Key) && !string.IsNullOrWhiteSpace(child.Value))
            {
                dict[child.Key] = child.Value!;
            }
        }

        return dict;
    }

    private static Dictionary<string, int> LoadTenantHookMaxConcurrency(IConfiguration config)
    {
        // 1) Env: TENANT_HOOK_MAX_CONCURRENCY='{"t1":16}'
        var envJson = config["TENANT_HOOK_MAX_CONCURRENCY"];
        if (!string.IsNullOrWhiteSpace(envJson))
        {
            var map = JsonSerializer.Deserialize(envJson, GatewayJsonContext.Default.DictionaryStringInt32);
            return map is null
                ? new Dictionary<string, int>(StringComparer.Ordinal)
                : new Dictionary<string, int>(map, StringComparer.Ordinal);
        }

        // 2) Config section: Tenants:HookMaxConcurrency:{TenantId}=int
        var section = config.GetSection("Tenants:HookMaxConcurrency");
        var dict = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var child in section.GetChildren())
        {
            if (string.IsNullOrWhiteSpace(child.Key) || string.IsNullOrWhiteSpace(child.Value))
            {
                continue;
            }

            if (int.TryParse(child.Value, out var v) && v > 0)
            {
                dict[child.Key] = v;
            }
        }

        return dict;
    }
}
