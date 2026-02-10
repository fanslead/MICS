using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Mics.Gateway.Metrics;
using StackExchange.Redis;

namespace Mics.Gateway.Infrastructure.Redis;

internal sealed class DeadNodeCleanupService : BackgroundService
{
    private static readonly LuaScript CleanupRouteScript = LuaScript.Prepare(@"
local onlineKey = KEYS[1]
local tenantLeasesKey = KEYS[2]
local userLeasesKey = KEYS[3]
local deadNodeRoutesKey = KEYS[4]

local deviceId = ARGV[1]
local deadNodeId = ARGV[2]
local routeEntry = ARGV[3]
local tenantMember = ARGV[4]
local userMember = ARGV[5]

local val = redis.call('HGET', onlineKey, deviceId)
if val then
  local p1 = string.find(val, '|', 1, true)
  if p1 then
    local nodeId = string.sub(val, 1, p1-1)
    if nodeId == deadNodeId then
      redis.call('HDEL', onlineKey, deviceId)
      redis.call('ZREM', tenantLeasesKey, tenantMember)
      redis.call('ZREM', userLeasesKey, userMember)
    end
  end
end

redis.call('SREM', deadNodeRoutesKey, routeEntry)
return 1
");

    private readonly IConnectionMultiplexer _mux;
    private readonly MetricsRegistry _metrics;
    private readonly ILogger<DeadNodeCleanupService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly string _selfNodeId;

    public DeadNodeCleanupService(
        IConnectionMultiplexer mux,
        MetricsRegistry metrics,
        ILogger<DeadNodeCleanupService> logger,
        TimeProvider timeProvider,
        string selfNodeId)
    {
        _mux = mux;
        _metrics = metrics;
        _logger = logger;
        _timeProvider = timeProvider;
        _selfNodeId = selfNodeId;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "dead node cleanup sweep failed");
            }

            await Task.Delay(5_000, stoppingToken);
        }
    }

    private async Task SweepOnceAsync(CancellationToken cancellationToken)
    {
        var db = _mux.GetDatabase();
        var now = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();

        cancellationToken.ThrowIfCancellationRequested();
        await db.SortedSetRemoveRangeByScoreAsync(RedisKeys.NodesLiveZset, double.NegativeInfinity, now);

        cancellationToken.ThrowIfCancellationRequested();
        var live = await db.SortedSetRangeByScoreAsync(RedisKeys.NodesLiveZset, now, double.PositiveInfinity);
        var liveSet = new HashSet<string>(live.Select(v => v.ToString()), StringComparer.Ordinal);

        cancellationToken.ThrowIfCancellationRequested();
        var known = await db.SetMembersAsync(RedisKeys.NodesKnownSet);

        foreach (var nodeValue in known)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var nodeId = nodeValue.ToString();
            if (string.IsNullOrWhiteSpace(nodeId) || nodeId == _selfNodeId)
            {
                continue;
            }

            if (liveSet.Contains(nodeId))
            {
                continue;
            }

            await CleanupDeadNodeAsync(db, nodeId, cancellationToken);
        }
    }

    private async Task CleanupDeadNodeAsync(IDatabase db, string deadNodeId, CancellationToken cancellationToken)
    {
        _metrics.CounterInc("mics_dead_node_cleanups_total", 1, ("node", deadNodeId));
        _logger.LogWarning("dead_node_cleanup_start node={NodeId}", deadNodeId);

        var routesKey = RedisKeys.NodeRoutesSet(deadNodeId);
        var routes = await db.SetMembersAsync(routesKey);
        var cleaned = 0;

        foreach (var entryVal in routes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = entryVal.ToString();

            if (!NodeRouteEntryCodec.TryDecode(entry, out var tenantId, out var userId, out var deviceId))
            {
                await db.SetRemoveAsync(routesKey, entry);
                continue;
            }

            var onlineKey = RedisKeys.OnlineUserHash(tenantId, userId);
            var tenantLeasesKey = RedisKeys.TenantConnLeasesZset(tenantId);
            var userLeasesKey = RedisKeys.UserConnLeasesZset(tenantId, userId);
            var tenantMember = LeaseMemberCodec.TenantMember(userId, deviceId);
            var userMember = LeaseMemberCodec.UserMember(deviceId);

            await db.ScriptEvaluateAsync(
                CleanupRouteScript.ExecutableScript,
                new RedisKey[] { onlineKey, tenantLeasesKey, userLeasesKey, routesKey },
                new RedisValue[] { deviceId, deadNodeId, entry, tenantMember, userMember });

            cleaned++;
        }

        _metrics.CounterInc("mics_dead_node_routes_processed_total", cleaned, ("node", deadNodeId));

        // Best-effort cleanup of node artifacts.
        await db.KeyDeleteAsync(routesKey);
        await db.KeyDeleteAsync(RedisKeys.NodeInfoHash(deadNodeId));
        await db.SetRemoveAsync(RedisKeys.NodesKnownSet, deadNodeId);

        _logger.LogWarning("dead_node_cleanup_done node={NodeId} routes={Routes}", deadNodeId, cleaned);
    }
}
