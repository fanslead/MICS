using Mics.Gateway.Cluster;
using Mics.Gateway.Config;
using Mics.Gateway.Group;
using Mics.Gateway.Hook;
using Mics.Gateway.Infrastructure.Redis;
using Mics.Gateway.Metrics;
using Mics.Gateway.Security;
using Microsoft.Extensions.Configuration;

namespace Mics.Tests;

public sealed class OnlineDeviceRouteCodecTests
{
    [Fact]
    public void EncodeDecode_RoundTrip()
    {
        var route = new OnlineDeviceRoute("node-1", "http://n1:8080", "c1", 123456789);
        var encoded = OnlineDeviceRouteCodec.Encode(route);

        Assert.True(OnlineDeviceRouteCodec.TryDecode(encoded, out var decoded));
        Assert.Equal(route, decoded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a|b|c")]
    [InlineData("a|b|c|not-a-number")]
    public void TryDecode_Invalid_ReturnsFalse(string input)
    {
        Assert.False(OnlineDeviceRouteCodec.TryDecode(input, out _));
    }
}

public sealed class RendezvousHashTests
{
    [Fact]
    public void PickNodeId_Empty_ReturnsNull()
    {
        Assert.Null(RendezvousHash.PickNodeId("t1", "u1", Array.Empty<NodeInfo>()));
    }

    [Fact]
    public void PickNodeId_Stable_ForSameInputs()
    {
        var nodes = new[]
        {
            new NodeInfo("n1", "http://n1:8080"),
            new NodeInfo("n2", "http://n2:8080"),
            new NodeInfo("n3", "http://n3:8080"),
        };

        var a = RendezvousHash.PickNodeId("t1", "u1", nodes);
        var b = RendezvousHash.PickNodeId("t1", "u1", nodes);

        Assert.Equal(a, b);
        Assert.Contains(a, nodes.Select(n => n.NodeId));
    }
}

public sealed class GatewayOptionsTests
{
    [Fact]
    public void Load_TenantAuthMap_FromEnvJson()
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TENANT_AUTH_MAP"] = "{\"t1\":\"http://hook:8081\"}",
                ["REDIS__CONNECTION"] = "localhost:6379",
                ["NODE_ID"] = "node-1",
                ["PUBLIC_ENDPOINT"] = "http://localhost:8080",
            })
            .Build();

        var options = GatewayOptions.Load(cfg);
        Assert.Equal("http://hook:8081", options.TenantAuthMap["t1"]);
    }

    [Fact]
    public void Load_TenantHookSecrets_AndSignRequired()
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TENANT_HOOK_SECRETS"] = "{\"t1\":\"s1\"}",
                ["HOOK_SIGN_REQUIRED"] = "true",
                ["REDIS__CONNECTION"] = "localhost:6379",
            })
            .Build();

        var options = GatewayOptions.Load(cfg);
        Assert.Equal("s1", options.TenantHookSecrets["t1"]);
        Assert.True(options.HookSignRequired);
    }

    [Fact]
    public void Load_WebSocketKeepAliveIntervalSeconds_FromEnv()
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WS_KEEPALIVE_INTERVAL_SECONDS"] = "15",
                ["REDIS__CONNECTION"] = "localhost:6379",
            })
            .Build();

        var options = GatewayOptions.Load(cfg);
        Assert.Equal(TimeSpan.FromSeconds(15), options.WebSocketKeepAliveInterval);
    }

    [Fact]
    public void Load_GroupFanoutOptions_FromEnv()
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GROUP_ROUTE_CHUNK_SIZE"] = "128",
                ["GROUP_OFFLINE_BUFFER_MAX_USERS"] = "10",
                ["GROUP_MEMBERS_MAX_USERS"] = "999",
                ["REDIS__CONNECTION"] = "localhost:6379",
            })
            .Build();

        var options = GatewayOptions.Load(cfg);
        Assert.Equal(128, options.GroupRouteChunkSize);
        Assert.Equal(10, options.GroupOfflineBufferMaxUsers);
        Assert.Equal(999, options.GroupMembersMaxUsers);
    }

    [Fact]
    public void Load_DedupMode_DefaultsToMemory_AndCanBeOverridden()
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["REDIS__CONNECTION"] = "localhost:6379",
            })
            .Build();

        var options = GatewayOptions.Load(cfg);
        var prop = typeof(GatewayOptions).GetProperty("DedupMode");
        Assert.NotNull(prop);
        Assert.Equal("memory", (string)prop!.GetValue(options)!);

        var cfg2 = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["REDIS__CONNECTION"] = "localhost:6379",
                ["DEDUP_MODE"] = "redis",
            })
            .Build();

        var options2 = GatewayOptions.Load(cfg2);
        Assert.Equal("redis", (string)prop.GetValue(options2)!);
    }
}

public sealed class MetricsRegistryTests
{
    [Fact]
    public void CollectPrometheusText_ContainsMetrics()
    {
        var metrics = new MetricsRegistry();
        metrics.GaugeSet("mics_ws_connections", 2, ("node", "n1"));
        metrics.CounterInc("mics_ws_connected_total", 1, ("tenant", "t1"));

        var text = metrics.CollectPrometheusText();

        Assert.Contains("mics_ws_connections{node=\"n1\"} 2", text);
        Assert.Contains("mics_ws_connected_total{tenant=\"t1\"} 1", text);
    }
}

public sealed class HookCircuitBreakerTests
{
    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;

        public ManualTimeProvider(DateTimeOffset now)
        {
            _now = now;
        }

        public void Advance(TimeSpan delta) => _now = _now.Add(delta);

        public override DateTimeOffset GetUtcNow() => _now;
    }

    [Fact]
	    public void CircuitBreaker_OpensAfterThreshold_AndHalfOpens()
	    {
	        var tp = new ManualTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(0));
	        var breaker = new HookCircuitBreaker(tp);
	        var policy = new HookBreakerPolicy(3, TimeSpan.FromSeconds(5));

	        const string tenant = "t1";

	        Assert.True(breaker.TryBegin(tenant, HookOperation.CheckMessage));
	        breaker.OnFailure(tenant, HookOperation.CheckMessage, policy);
	        Assert.True(breaker.TryBegin(tenant, HookOperation.CheckMessage));
	        breaker.OnFailure(tenant, HookOperation.CheckMessage, policy);
	        Assert.True(breaker.TryBegin(tenant, HookOperation.CheckMessage));
	        breaker.OnFailure(tenant, HookOperation.CheckMessage, policy);

        // Open
        Assert.False(breaker.TryBegin(tenant, HookOperation.CheckMessage));

        // Half-open after duration: only one allowed
        tp.Advance(TimeSpan.FromSeconds(5));
        Assert.True(breaker.TryBegin(tenant, HookOperation.CheckMessage));
        Assert.False(breaker.TryBegin(tenant, HookOperation.CheckMessage));

        // Success closes
        breaker.OnSuccess(tenant, HookOperation.CheckMessage);
        Assert.True(breaker.TryBegin(tenant, HookOperation.CheckMessage));
    }
}

public sealed class MessageDeduplicatorTests
{
    [Fact]
    public async Task InMemoryDeduplicator_DetectsDuplicateWithinTtl()
    {
        var d = new InMemoryMessageDeduplicator();
        var ttl = TimeSpan.FromMinutes(1);

        Assert.True(await d.TryMarkAsync("t1", "m1", ttl, CancellationToken.None));
        Assert.False(await d.TryMarkAsync("t1", "m1", ttl, CancellationToken.None));
        Assert.True(await d.TryMarkAsync("t1", "m2", ttl, CancellationToken.None));
    }
}

public sealed class NodeRouteEntryCodecTests
{
    [Fact]
    public void EncodeDecode_RoundTrip()
    {
        var entry = NodeRouteEntryCodec.Encode("t1", "u1", "d1");
        Assert.True(NodeRouteEntryCodec.TryDecode(entry, out var t, out var u, out var d));
        Assert.Equal("t1", t);
        Assert.Equal("u1", u);
        Assert.Equal("d1", d);
    }

    [Theory]
    [InlineData("")]
    [InlineData("t1|u1")]
    [InlineData("t1|u1|")]
    [InlineData("t1||d1")]
    public void TryDecode_Invalid_ReturnsFalse(string input)
    {
        Assert.False(NodeRouteEntryCodec.TryDecode(input, out _, out _, out _));
    }
}

public sealed class GroupFanoutPlannerTests
{
    [Fact]
    public void BuildBuckets_DedupsUsersAndNodes()
    {
        var routesByUser = new Dictionary<string, IReadOnlyList<OnlineDeviceRoute>>(StringComparer.Ordinal)
        {
            ["u1"] = new[]
            {
                new OnlineDeviceRoute("n1", "http://n1", "c1", 1),
                new OnlineDeviceRoute("n1", "http://n1", "c2", 2), // same node, another device
                new OnlineDeviceRoute("n2", "http://n2", "c3", 3), // user has device on another node
            },
            ["u2"] = new[]
            {
                new OnlineDeviceRoute("n2", "http://n2", "c4", 4),
            },
        };

        var members = new[] { "u1", "u1", "u2", "u3" }; // u3 offline
        var buckets = GroupFanoutPlanner.BuildBuckets("n1", members, routesByUser);

        var n1 = buckets.Single(b => b.NodeId == "n1");
        Assert.Equal(new[] { "u1" }, n1.UserIds.Order(StringComparer.Ordinal).ToArray());

        var n2 = buckets.Single(b => b.NodeId == "n2");
        Assert.Equal(new[] { "u1", "u2" }, n2.UserIds.Order(StringComparer.Ordinal).ToArray());
    }
}

public sealed class LocalRouteCacheTests
{
    [Fact]
    public void SetThenGetThenInvalidate_Works()
    {
        var cache = new Mics.Gateway.Infrastructure.LocalRouteCache(1 * 1024 * 1024);

        var routes = new Dictionary<string, OnlineDeviceRoute>(StringComparer.Ordinal)
        {
            ["d1"] = new OnlineDeviceRoute("n1", "http://n1", "c1", 1),
        };

        cache.Set("t1", "u1", routes, TimeSpan.FromSeconds(5));

        Assert.True(cache.TryGet("t1", "u1", out var got));
        Assert.Single(got);
        Assert.Equal("n1", got["d1"].NodeId);

        cache.Invalidate("t1", "u1");
        Assert.False(cache.TryGet("t1", "u1", out _));
    }
}

public sealed class HmacSignTests
{
    [Fact]
    public void ComputeBase64_MatchesSpec()
    {
        var meta = new Mics.Contracts.Hook.V1.HookMeta
        {
            TenantId = "t1",
            RequestId = "r1",
            TimestampMs = 123,
            Sign = "",
        };

        var req = new Mics.Contracts.Hook.V1.AuthRequest
        {
            Meta = meta,
            Token = "valid:u1",
            DeviceId = "d1",
        };

        var payload = req.Clone();
        payload.Meta.Sign = "";

        var sign = HmacSign.ComputeBase64("secret", meta, payload);

        using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes("secret"));
        var payloadBytes = Google.Protobuf.MessageExtensions.ToByteArray(payload);
        hmac.TransformBlock(payloadBytes, 0, payloadBytes.Length, null, 0);

        var ridBytes = System.Text.Encoding.UTF8.GetBytes(meta.RequestId);
        hmac.TransformBlock(ridBytes, 0, ridBytes.Length, null, 0);

        Span<byte> ts = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(ts, meta.TimestampMs);
        hmac.TransformFinalBlock(ts.ToArray(), 0, ts.Length);

        var expected = Convert.ToBase64String(hmac.Hash!);
        Assert.Equal(expected, sign);
    }
}
