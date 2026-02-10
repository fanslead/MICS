using System.Net.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Mics.Gateway.Composition;
using Mics.Gateway.Cluster;
using Mics.Gateway.Config;
using Mics.Gateway.Connections;
using Mics.Gateway.Grpc;
using Mics.Gateway.Infrastructure;
using Mics.Gateway.Infrastructure.Redis;
using Mics.Gateway.Metrics;
using Mics.Gateway.Mq;
using Mics.Gateway.Ws;
using StackExchange.Redis;

AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(o => o.IncludeScopes = true);

builder.Services.AddGrpc();

var options = GatewayOptions.Load(builder.Configuration);
if (string.IsNullOrWhiteSpace(options.RedisConnection))
{
    throw new InvalidOperationException("REDIS__CONNECTION is required");
}

var listenPort = builder.Configuration.GetValue("PORT", 8080);
builder.WebHost.ConfigureKestrel(k =>
{
    k.ListenAnyIP(listenPort, o => o.Protocols = HttpProtocols.Http1AndHttp2);
});

var publicEndpoint = options.PublicEndpoint;
if (string.IsNullOrWhiteSpace(publicEndpoint))
{
    // For local/dev, default to current listen port.
    publicEndpoint = $"http://localhost:{listenPort}";
}

var mux = await ConnectionMultiplexer.ConnectAsync(options.RedisConnection);
builder.Services.AddSingleton<IConnectionMultiplexer>(mux);

builder.Services.AddSingleton<HttpClient>(_ =>
{
    // Timeout is controlled per-request by HookClient.
    return new HttpClient(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        EnableMultipleHttp2Connections = true,
    })
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };
});

builder.Services.AddSingleton<GatewayServiceProvider>(sp => new GatewayServiceProvider
{
    Options = options,
    PublicEndpoint = publicEndpoint,
    ConnectionMultiplexer = sp.GetRequiredService<IConnectionMultiplexer>(),
    HttpClient = sp.GetRequiredService<HttpClient>(),
    LoggerFactory = sp.GetRequiredService<ILoggerFactory>(),
    TimeProvider = TimeProvider.System,
});

builder.Services.AddSingleton<MetricsRegistry>(sp => sp.GetRequiredService<GatewayServiceProvider>().GetService<MetricsRegistry>());
builder.Services.AddSingleton<NodeGatewayService>(sp => sp.GetRequiredService<GatewayServiceProvider>().GetService<NodeGatewayService>());
builder.Services.AddSingleton<NodeSnapshotService>(sp => sp.GetRequiredService<GatewayServiceProvider>().GetService<NodeSnapshotService>());
builder.Services.AddSingleton<INodeSnapshot>(sp => sp.GetRequiredService<NodeSnapshotService>());
builder.Services.AddSingleton<WsGatewayHandler>(sp => sp.GetRequiredService<GatewayServiceProvider>().GetService<WsGatewayHandler>());
builder.Services.AddSingleton<IShutdownState>(sp => sp.GetRequiredService<GatewayServiceProvider>().GetService<IShutdownState>());

builder.Services.AddHostedService(sp => sp.GetRequiredService<GatewayServiceProvider>().GetService<MqEventDispatcherService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<GatewayServiceProvider>().GetService<HeartbeatSweeperService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<GatewayServiceProvider>().GetService<DeadNodeCleanupService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<NodeSnapshotService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<GatewayServiceProvider>().GetService<ShutdownDrainService>());

var app = builder.Build();

app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = options.WebSocketKeepAliveInterval,
});

app.MapGet("/", () => Results.Text("MICS Gateway"));
app.MapGet("/healthz", () => Results.Text("ok"));
app.MapGet("/readyz", (IShutdownState shutdown) =>
    shutdown.IsDraining ? Results.StatusCode(StatusCodes.Status503ServiceUnavailable) : Results.Text("ok"));
app.MapGet("/metrics", (MetricsRegistry metrics) =>
    Results.Text(metrics.CollectPrometheusText(), "text/plain; version=0.0.4; charset=utf-8"));

app.MapGrpcService<NodeGatewayService>();

app.Map("/ws", async ctx =>
{
    var handler = ctx.RequestServices.GetRequiredService<WsGatewayHandler>();
    await handler.HandleAsync(ctx);
});

app.Run();
