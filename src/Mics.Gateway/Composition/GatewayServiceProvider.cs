using Confluent.Kafka;
using Jab;
using Microsoft.Extensions.Logging;
using Mics.Gateway.Cluster;
using Mics.Gateway.Config;
using Mics.Gateway.Connections;
using Mics.Gateway.Grpc;
using Mics.Gateway.Hook;
using Mics.Gateway.Infrastructure;
using Mics.Gateway.Infrastructure.Redis;
using Mics.Gateway.Metrics;
using Mics.Gateway.Mq;
using Mics.Gateway.Offline;
using Mics.Gateway.Ws;
using StackExchange.Redis;

namespace Mics.Gateway.Composition;

[ServiceProvider]
[Singleton(typeof(GatewayOptions), Instance = nameof(Options))]
[Singleton(typeof(IConnectionMultiplexer), Instance = nameof(ConnectionMultiplexer))]
[Singleton(typeof(HttpClient), Instance = nameof(HttpClient))]
[Singleton(typeof(ILoggerFactory), Instance = nameof(LoggerFactory))]
[Singleton(typeof(TimeProvider), Instance = nameof(TimeProvider))]
[Transient(typeof(ILogger<>), typeof(Logger<>))]

[Singleton(typeof(MetricsRegistry))]
[Singleton(typeof(ILocalRouteCache), Factory = nameof(CreateLocalRouteCache))]
[Singleton(typeof(IConnectionRegistry), typeof(ConnectionRegistry))]
[Singleton(typeof(IOfflineBufferStore), Factory = nameof(CreateOfflineBufferStore))]
[Singleton(typeof(INodeClientPool), typeof(NodeClientPool))]
[Singleton(typeof(GrpcNodeCircuitBreaker))]

[Singleton(typeof(IOnlineRouteStore), typeof(OnlineRouteStore))]
[Singleton(typeof(INodeDirectory), typeof(NodeDirectory))]
[Singleton(typeof(IRedisRateLimiter), typeof(RedisRateLimiter))]
[Singleton(typeof(IMessageDeduplicator), Factory = nameof(CreateMessageDeduplicator))]
[Singleton(typeof(RedisConnectionAdmission), Factory = nameof(CreateRedisConnectionAdmission))]
[Singleton(typeof(AdmissionUnregisterRetryQueue), Factory = nameof(CreateAdmissionUnregisterRetryQueue))]
[Singleton(typeof(AdmissionUnregisterRetryService), Factory = nameof(CreateAdmissionUnregisterRetryService))]
[Singleton(typeof(IConnectionAdmission), Factory = nameof(CreateConnectionAdmission))]
[Singleton(typeof(ITraceContext), typeof(TraceContext))]
[Singleton(typeof(IShutdownState), typeof(ShutdownState))]

[Singleton(typeof(HookCircuitBreaker))]
[Singleton(typeof(IHookMetaFactory), typeof(DefaultHookMetaFactory))]
[Singleton(typeof(HookPolicyDefaults), Factory = nameof(CreateHookPolicyDefaults))]
[Singleton(typeof(ITenantHookPolicyCache), typeof(TenantHookPolicyCache))]
[Singleton(typeof(IAuthHookSecretProvider), Factory = nameof(CreateAuthHookSecretProvider))]
[Singleton(typeof(IHookConcurrencyLimiter), typeof(HookConcurrencyLimiter))]
[Singleton(typeof(IHookClient), Factory = nameof(CreateHookClient))]

[Singleton(typeof(NodeSnapshotService), Factory = nameof(CreateNodeSnapshotService))]
[Singleton(typeof(INodeSnapshot), Factory = nameof(CreateNodeSnapshot))]

[Singleton(typeof(WsGatewayHandler), Factory = nameof(CreateWsGatewayHandler))]
[Singleton(typeof(NodeGatewayService))]

[Singleton(typeof(IMqProducer), Factory = nameof(CreateMqProducer))]
[Singleton(typeof(MqEventDispatcher), Factory = nameof(CreateMqEventDispatcher))]
[Singleton(typeof(MqEventDispatcherService))]

[Singleton(typeof(HeartbeatSweeper))]
[Singleton(typeof(HeartbeatSweeperService))]
[Singleton(typeof(ShutdownDrainService), Factory = nameof(CreateShutdownDrainService))]
[Singleton(typeof(DeadNodeCleanupService), Factory = nameof(CreateDeadNodeCleanupService))]
internal partial class GatewayServiceProvider
{
    public required GatewayOptions Options { get; set; }
    public required string PublicEndpoint { get; set; }
    public required IConnectionMultiplexer ConnectionMultiplexer { get; set; }
    public required HttpClient HttpClient { get; set; }
    public required ILoggerFactory LoggerFactory { get; set; }
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    public RedisConnectionAdmission CreateRedisConnectionAdmission(IConnectionMultiplexer mux) =>
        new RedisConnectionAdmission(mux, Options.NodeId);

    public AdmissionUnregisterRetryQueue CreateAdmissionUnregisterRetryQueue() =>
        new AdmissionUnregisterRetryQueue(Options.AdmissionUnregisterRetryQueueCapacity);

    public AdmissionUnregisterRetryService CreateAdmissionUnregisterRetryService(
        RedisConnectionAdmission inner,
        AdmissionUnregisterRetryQueue queue,
        MetricsRegistry metrics,
        ILogger<AdmissionUnregisterRetryService> logger,
        TimeProvider timeProvider) =>
        new AdmissionUnregisterRetryService(
            inner,
            queue,
            metrics,
            logger,
            timeProvider,
            Options.AdmissionUnregisterRetryMaxAttempts,
            TimeSpan.FromMilliseconds(Options.AdmissionUnregisterRetryBackoffMs));

    public IConnectionAdmission CreateConnectionAdmission(
        RedisConnectionAdmission inner,
        AdmissionUnregisterRetryQueue queue,
        MetricsRegistry metrics,
        ILogger<ResilientConnectionAdmission> logger) =>
        new ResilientConnectionAdmission(inner, queue, metrics, logger);

    public IMessageDeduplicator CreateMessageDeduplicator(IConnectionMultiplexer mux) =>
        string.Equals(Options.DedupMode, "redis", StringComparison.OrdinalIgnoreCase)
            ? new RedisMessageDeduplicator(mux)
            : new InMemoryMessageDeduplicator();

    public ILocalRouteCache CreateLocalRouteCache() =>
        Options.LocalRouteCacheTtlSeconds <= 0 || Options.LocalRouteCacheSizeBytes <= 0
            ? new NoopLocalRouteCache()
            : new LocalRouteCache(Options.LocalRouteCacheSizeBytes);

    public IOfflineBufferStore CreateOfflineBufferStore(MetricsRegistry metrics) =>
        new OfflineBufferStore(
            maxMessagesPerUser: Options.OfflineBufferMaxMessagesPerUser,
            maxBytesPerUser: Options.OfflineBufferMaxBytesPerUser,
            maxUsersPerTenant: Options.OfflineBufferMaxUsersPerTenant,
            metrics: metrics);

    public HookPolicyDefaults CreateHookPolicyDefaults() =>
        new HookPolicyDefaults(
            Options.HookMaxConcurrencyDefault,
            Options.TenantHookMaxConcurrency,
            Options.HookQueueTimeout,
            Options.HookBreakerFailureThreshold,
            Options.HookBreakerOpenDuration,
            Options.HookSignRequired);

    public IAuthHookSecretProvider CreateAuthHookSecretProvider() =>
        new AuthHookSecretProvider(Options.TenantHookSecrets);

    public IHookClient CreateHookClient(
        HttpClient http,
        HookCircuitBreaker breaker,
        IHookMetaFactory metaFactory,
        IAuthHookSecretProvider authSecrets,
        ITenantHookPolicyCache policies,
        IHookConcurrencyLimiter limiter,
        MetricsRegistry metrics,
        ILogger<HookClient> logger,
        TimeProvider timeProvider) =>
        new HookClient(http, Options.HookTimeout, breaker, metaFactory, authSecrets, policies, limiter, metrics, logger, timeProvider);

    public NodeSnapshotService CreateNodeSnapshotService(INodeDirectory directory, ILogger<NodeSnapshotService> logger) =>
        new NodeSnapshotService(directory, logger, Options.NodeId, PublicEndpoint, Options.NodeTtl);

    public INodeSnapshot CreateNodeSnapshot(NodeSnapshotService snapshotService) => snapshotService;

    public WsGatewayHandler CreateWsGatewayHandler(
        IHookClient hook,
        IConnectionRegistry connections,
        IOnlineRouteStore routes,
        IConnectionAdmission admission,
        INodeSnapshot nodes,
        INodeClientPool nodeClients,
        GrpcNodeCircuitBreaker grpcBreaker,
        IOfflineBufferStore offline,
        IRedisRateLimiter rateLimiter,
        IMessageDeduplicator dedup,
        MqEventDispatcher mq,
        MetricsRegistry metrics,
        ILogger<WsGatewayHandler> logger,
        ITraceContext traceContext,
        IShutdownState shutdown) =>
        new WsGatewayHandler(
            Options.NodeId,
            PublicEndpoint,
            Options.TenantAuthMap,
            hook,
            connections,
            routes,
            admission,
            nodes,
            nodeClients,
            grpcBreaker,
            new GrpcBreakerPolicy(Options.GrpcBreakerFailureThreshold, Options.GrpcBreakerOpenDuration),
            offline,
            rateLimiter,
            dedup,
            mq,
            metrics,
            logger,
            traceContext,
            shutdown,
            Options.MaxMessageBytes,
            Options.GroupRouteChunkSize,
            Options.GroupOfflineBufferMaxUsers,
            Options.GroupMembersMaxUsers);

    public IMqProducer CreateMqProducer()
    {
        if (string.IsNullOrWhiteSpace(Options.KafkaBootstrapServers))
        {
            return new NoopMqProducer();
        }

        var producer = new ProducerBuilder<byte[], byte[]>(new ProducerConfig
        {
            BootstrapServers = Options.KafkaBootstrapServers,
            ClientId = Options.NodeId,
            Acks = Acks.All,
            EnableIdempotence = true,
        }).Build();

        return new KafkaMqProducer(producer);
    }

    public MqEventDispatcher CreateMqEventDispatcher(IMqProducer producer, MetricsRegistry metrics, TimeProvider timeProvider) =>
        new MqEventDispatcher(
            producer,
            metrics,
            timeProvider,
            new MqEventDispatcherOptions(
                QueueCapacity: Options.KafkaQueueCapacity,
                MaxPendingPerTenant: Options.KafkaMaxPendingPerTenant,
                MaxAttempts: Options.KafkaMaxAttempts,
                RetryBackoffBase: TimeSpan.FromMilliseconds(Options.KafkaRetryBackoffMs),
                IdleDelay: TimeSpan.FromMilliseconds(Options.KafkaIdleDelayMs),
                DlqFallbackQueueCapacity: Options.KafkaDlqFallbackQueueCapacity,
                DlqFallbackMaxPendingPerTenant: Options.KafkaDlqFallbackMaxPendingPerTenant,
                DlqFallbackMaxAttempts: Options.KafkaDlqFallbackMaxAttempts,
                DlqFallbackRetryBackoffBase: TimeSpan.FromMilliseconds(Options.KafkaDlqFallbackRetryBackoffMs)));

    public DeadNodeCleanupService CreateDeadNodeCleanupService(
        IConnectionMultiplexer mux,
        MetricsRegistry metrics,
        ILogger<DeadNodeCleanupService> logger,
        TimeProvider timeProvider) =>
        new DeadNodeCleanupService(mux, metrics, logger, timeProvider, Options.NodeId);

    public ShutdownDrainService CreateShutdownDrainService(
        IShutdownState shutdown,
        IConnectionRegistry connections,
        IConnectionAdmission admission,
        MetricsRegistry metrics,
        ILogger<ShutdownDrainService> logger) =>
        new ShutdownDrainService(
            Options.NodeId,
            Options.DrainTimeout,
            shutdown,
            connections,
            admission,
            metrics,
            logger);
}
