using System.Reflection;
using Mics.Gateway.Infrastructure.Redis;
using StackExchange.Redis;

namespace Mics.Tests;

public sealed class RedisConnectionAdmissionLeaseTests
{
    private class DatabaseProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?>? Handler { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null)
            {
                throw new InvalidOperationException();
            }

            if (Handler is null)
            {
                throw new InvalidOperationException("Handler not set.");
            }

            return Handler(targetMethod, args);
        }
    }

    private class MultiplexerProxy : DispatchProxy
    {
        public IDatabase? Database { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null)
            {
                throw new InvalidOperationException();
            }

            if (string.Equals(targetMethod.Name, "GetDatabase", StringComparison.Ordinal))
            {
                return Database ?? throw new InvalidOperationException("Database not set.");
            }

            throw new NotSupportedException(targetMethod.Name);
        }
    }

    [Fact]
    public async Task TryRegisterAsync_UsesLeaseZsets_NotConnTotal()
    {
        RedisKey[]? keys = null;

        var db = DispatchProxy.Create<IDatabase, DatabaseProxy>();
        ((DatabaseProxy)(object)db).Handler = (method, args) =>
        {
            if (string.Equals(method.Name, "ScriptEvaluateAsync", StringComparison.Ordinal))
            {
                keys = (RedisKey[])args![1]!;
                return Task.FromException<RedisResult>(new InvalidOperationException("stop"));
            }

            throw new NotSupportedException(method.Name);
        };

        var mux = DispatchProxy.Create<IConnectionMultiplexer, MultiplexerProxy>();
        ((MultiplexerProxy)(object)mux).Database = db;

        var admission = new RedisConnectionAdmission(mux, nodeId: "node-1");
        try
        {
            await admission.TryRegisterAsync(
                tenantId: "t1",
                userId: "u1",
                deviceId: "d1",
                route: new OnlineDeviceRoute("node-1", "http://n1", "c1", 123),
                heartbeatTimeoutSeconds: 30,
                tenantMaxConnections: 10,
                userMaxConnections: 2,
                cancellationToken: CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
        }

        Assert.NotNull(keys);
        var keyStrings = keys!.Select(k => k.ToString()).ToArray();
        Assert.Contains("t1:conn_leases", keyStrings);
        Assert.Contains("t1:user:u1:conn_leases", keyStrings);
        Assert.DoesNotContain("t1:conn_total", keyStrings);
    }

    [Fact]
    public async Task RenewLeaseAsync_AlsoTouchesOnlineHash_ForTtl()
    {
        RedisKey[]? keys = null;

        var db = DispatchProxy.Create<IDatabase, DatabaseProxy>();
        ((DatabaseProxy)(object)db).Handler = (method, args) =>
        {
            if (string.Equals(method.Name, "ScriptEvaluateAsync", StringComparison.Ordinal))
            {
                keys = (RedisKey[])args![1]!;
                return Task.FromException<RedisResult>(new InvalidOperationException("stop"));
            }

            throw new NotSupportedException(method.Name);
        };

        var mux = DispatchProxy.Create<IConnectionMultiplexer, MultiplexerProxy>();
        ((MultiplexerProxy)(object)mux).Database = db;

        var admission = new RedisConnectionAdmission(mux, nodeId: "node-1");
        try
        {
            await admission.RenewLeaseAsync("t1", "u1", "d1", heartbeatTimeoutSeconds: 30, cancellationToken: CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
        }

        Assert.NotNull(keys);
        var keyStrings = keys!.Select(k => k.ToString()).ToArray();
        Assert.Contains("t1:online:u1", keyStrings);
    }
}
