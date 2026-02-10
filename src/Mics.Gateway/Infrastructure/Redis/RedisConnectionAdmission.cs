using StackExchange.Redis;

namespace Mics.Gateway.Infrastructure.Redis;

internal enum ConnectionAdmissionStatus
{
    Denied = 0,
    AllowedNew = 1,
    AllowedReplace = 2,
}

internal sealed record ConnectionAdmissionResult(ConnectionAdmissionStatus Status, string Reason)
{
    public bool Allowed => Status is ConnectionAdmissionStatus.AllowedNew or ConnectionAdmissionStatus.AllowedReplace;
}

internal interface IConnectionAdmission
{
    ValueTask<ConnectionAdmissionResult> TryRegisterAsync(
        string tenantId,
        string userId,
        string deviceId,
        OnlineDeviceRoute route,
        int heartbeatTimeoutSeconds,
        int tenantMaxConnections,
        int userMaxConnections,
        CancellationToken cancellationToken);

    ValueTask RenewLeaseAsync(
        string tenantId,
        string userId,
        string deviceId,
        int heartbeatTimeoutSeconds,
        CancellationToken cancellationToken);

    ValueTask UnregisterAsync(
        string tenantId,
        string userId,
        string deviceId,
        string expectedNodeId,
        string expectedConnectionId,
        CancellationToken cancellationToken);
}

internal sealed class RedisConnectionAdmission : IConnectionAdmission
{
    // KEYS:
    // 1) online hash key: {tenant}:online:{user}
    // 2) tenant conn leases zset: {tenant}:conn_leases
    // 3) user conn leases zset: {tenant}:user:{user}:conn_leases
    // 4) node routes set key: nodes:{nodeId}:routes
    // ARGV:
    // 1) deviceId
    // 2) routeValue
    // 3) routeEntry (tenant|user|device)
    // 4) tenantMember (user|device)
    // 5) userMember (device)
    // 6) nowMs
    // 7) ttlMs
    // 8) tenantMax
    // 9) userMax
    private static readonly LuaScript RegisterScript = LuaScript.Prepare(@"
local onlineKey = KEYS[1]
local tenantLeasesKey = KEYS[2]
local userLeasesKey = KEYS[3]
local nodeRoutesKey = KEYS[4]

local deviceId = ARGV[1]
local routeValue = ARGV[2]
local routeEntry = ARGV[3]
local tenantMember = ARGV[4]
local userMember = ARGV[5]
local nowMs = tonumber(ARGV[6])
local ttlMs = tonumber(ARGV[7])
local tenantMax = tonumber(ARGV[8])
local userMax = tonumber(ARGV[9])
local expiresAt = nowMs + ttlMs

redis.call('ZREMRANGEBYSCORE', tenantLeasesKey, '-inf', nowMs)
redis.call('ZREMRANGEBYSCORE', userLeasesKey, '-inf', nowMs)

local existed = redis.call('HEXISTS', onlineKey, deviceId)
if existed == 1 then
  redis.call('HSET', onlineKey, deviceId, routeValue)
  redis.call('SADD', nodeRoutesKey, routeEntry)
  redis.call('ZADD', tenantLeasesKey, expiresAt, tenantMember)
  redis.call('ZADD', userLeasesKey, expiresAt, userMember)
  redis.call('PEXPIRE', tenantLeasesKey, ttlMs * 2)
  redis.call('PEXPIRE', userLeasesKey, ttlMs * 2)
  return 2
end

if userMax and userMax > 0 then
  local curUser = tonumber(redis.call('ZCARD', userLeasesKey) or '0')
  if curUser >= userMax then
    return 0
  end
end

if tenantMax and tenantMax > 0 then
  local curTenant = tonumber(redis.call('ZCARD', tenantLeasesKey) or '0')
  if curTenant >= tenantMax then
    return 0
  end
end

redis.call('HSET', onlineKey, deviceId, routeValue)
redis.call('SADD', nodeRoutesKey, routeEntry)
redis.call('ZADD', tenantLeasesKey, expiresAt, tenantMember)
redis.call('ZADD', userLeasesKey, expiresAt, userMember)
redis.call('PEXPIRE', tenantLeasesKey, ttlMs * 2)
redis.call('PEXPIRE', userLeasesKey, ttlMs * 2)
return 1
");

    // KEYS:
    // 1) online hash key: {tenant}:online:{user}
    // 2) tenant conn leases zset: {tenant}:conn_leases
    // 3) user conn leases zset: {tenant}:user:{user}:conn_leases
    // 4) node routes set key: nodes:{nodeId}:routes
    // ARGV:
    // 1) deviceId
    // 2) expectedNodeId
    // 3) expectedConnectionId
    // 4) routeEntry (tenant|user|device)
    // 5) tenantMember (user|device)
    // 6) userMember (device)
    private static readonly LuaScript UnregisterScript = LuaScript.Prepare(@"
local onlineKey = KEYS[1]
local tenantLeasesKey = KEYS[2]
local userLeasesKey = KEYS[3]
local nodeRoutesKey = KEYS[4]

local deviceId = ARGV[1]
local expectedNodeId = ARGV[2]
local expectedConnId = ARGV[3]
local routeEntry = ARGV[4]
local tenantMember = ARGV[5]
local userMember = ARGV[6]

local val = redis.call('HGET', onlineKey, deviceId)
if val then
  local p1 = string.find(val, '|', 1, true)
  if p1 then
    local nodeId = string.sub(val, 1, p1-1)
    local p2 = string.find(val, '|', p1+1, true)
    if p2 then
      local p3 = string.find(val, '|', p2+1, true)
      if p3 then
        local connId = string.sub(val, p2+1, p3-1)
        if nodeId == expectedNodeId and connId == expectedConnId then
          redis.call('HDEL', onlineKey, deviceId)
          redis.call('ZREM', tenantLeasesKey, tenantMember)
          redis.call('ZREM', userLeasesKey, userMember)
        end
      end
    end
  end
end

redis.call('SREM', nodeRoutesKey, routeEntry)
return 1
");

    // KEYS:
    // 1) tenant conn leases zset: {tenant}:conn_leases
    // 2) user conn leases zset: {tenant}:user:{user}:conn_leases
    // ARGV:
    // 1) tenantMember (user|device)
    // 2) userMember (device)
    // 3) nowMs
    // 4) ttlMs
    private static readonly LuaScript RenewLeaseScript = LuaScript.Prepare(@"
local tenantLeasesKey = KEYS[1]
local userLeasesKey = KEYS[2]

local tenantMember = ARGV[1]
local userMember = ARGV[2]
local nowMs = tonumber(ARGV[3])
local ttlMs = tonumber(ARGV[4])
local expiresAt = nowMs + ttlMs

redis.call('ZREMRANGEBYSCORE', tenantLeasesKey, '-inf', nowMs)
redis.call('ZREMRANGEBYSCORE', userLeasesKey, '-inf', nowMs)

redis.call('ZADD', tenantLeasesKey, expiresAt, tenantMember)
redis.call('ZADD', userLeasesKey, expiresAt, userMember)
redis.call('PEXPIRE', tenantLeasesKey, ttlMs * 2)
redis.call('PEXPIRE', userLeasesKey, ttlMs * 2)
return 1
");

    private readonly IDatabase _db;
    private readonly string _nodeId;

    public RedisConnectionAdmission(IConnectionMultiplexer mux, string nodeId)
    {
        _db = mux.GetDatabase();
        _nodeId = nodeId;
    }

    public async ValueTask<ConnectionAdmissionResult> TryRegisterAsync(
        string tenantId,
        string userId,
        string deviceId,
        OnlineDeviceRoute route,
        int heartbeatTimeoutSeconds,
        int tenantMaxConnections,
        int userMaxConnections,
        CancellationToken cancellationToken)
    {
        var onlineKey = RedisKeys.OnlineUserHash(tenantId, userId);
        var tenantLeasesKey = RedisKeys.TenantConnLeasesZset(tenantId);
        var userLeasesKey = RedisKeys.UserConnLeasesZset(tenantId, userId);
        var nodeRoutesKey = RedisKeys.NodeRoutesSet(_nodeId);

        var routeValue = OnlineDeviceRouteCodec.Encode(route);
        var routeEntry = NodeRouteEntryCodec.Encode(tenantId, userId, deviceId);
        var tenantMember = LeaseMemberCodec.TenantMember(userId, deviceId);
        var userMember = LeaseMemberCodec.UserMember(deviceId);
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var ttlMs = GetLeaseTtlMsFromHeartbeatSeconds(heartbeatTimeoutSeconds);
        if (tenantMaxConnections < 0) tenantMaxConnections = 0;
        if (userMaxConnections < 0) userMaxConnections = 0;

        cancellationToken.ThrowIfCancellationRequested();
        var res = await _db.ScriptEvaluateAsync(
            RegisterScript.ExecutableScript,
            new RedisKey[] { onlineKey, tenantLeasesKey, userLeasesKey, nodeRoutesKey },
            new RedisValue[] { deviceId, routeValue, routeEntry, tenantMember, userMember, nowMs, ttlMs, tenantMaxConnections, userMaxConnections });

        return (int)res switch
        {
            1 => new ConnectionAdmissionResult(ConnectionAdmissionStatus.AllowedNew, ""),
            2 => new ConnectionAdmissionResult(ConnectionAdmissionStatus.AllowedReplace, ""),
            _ => new ConnectionAdmissionResult(ConnectionAdmissionStatus.Denied, "rate limited"),
        };
    }

    public async ValueTask RenewLeaseAsync(string tenantId, string userId, string deviceId, int heartbeatTimeoutSeconds, CancellationToken cancellationToken)
    {
        var tenantLeasesKey = RedisKeys.TenantConnLeasesZset(tenantId);
        var userLeasesKey = RedisKeys.UserConnLeasesZset(tenantId, userId);
        var tenantMember = LeaseMemberCodec.TenantMember(userId, deviceId);
        var userMember = LeaseMemberCodec.UserMember(deviceId);
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var ttlMs = GetLeaseTtlMsFromHeartbeatSeconds(heartbeatTimeoutSeconds);

        cancellationToken.ThrowIfCancellationRequested();
        await _db.ScriptEvaluateAsync(
            RenewLeaseScript.ExecutableScript,
            new RedisKey[] { tenantLeasesKey, userLeasesKey },
            new RedisValue[] { tenantMember, userMember, nowMs, ttlMs });
    }

    public async ValueTask UnregisterAsync(
        string tenantId,
        string userId,
        string deviceId,
        string expectedNodeId,
        string expectedConnectionId,
        CancellationToken cancellationToken)
    {
        var onlineKey = RedisKeys.OnlineUserHash(tenantId, userId);
        var tenantLeasesKey = RedisKeys.TenantConnLeasesZset(tenantId);
        var userLeasesKey = RedisKeys.UserConnLeasesZset(tenantId, userId);
        var nodeRoutesKey = RedisKeys.NodeRoutesSet(_nodeId);
        var routeEntry = NodeRouteEntryCodec.Encode(tenantId, userId, deviceId);
        var tenantMember = LeaseMemberCodec.TenantMember(userId, deviceId);
        var userMember = LeaseMemberCodec.UserMember(deviceId);

        cancellationToken.ThrowIfCancellationRequested();
        await _db.ScriptEvaluateAsync(
            UnregisterScript.ExecutableScript,
            new RedisKey[] { onlineKey, tenantLeasesKey, userLeasesKey, nodeRoutesKey },
            new RedisValue[] { deviceId, expectedNodeId, expectedConnectionId, routeEntry, tenantMember, userMember });
    }

    private static long GetLeaseTtlMsFromHeartbeatSeconds(int heartbeatTimeoutSeconds)
    {
        var seconds = heartbeatTimeoutSeconds > 0 ? heartbeatTimeoutSeconds : 30;
        var ttlMs = seconds * 1000L * 2;
        return Math.Max(30_000, ttlMs);
    }
}
