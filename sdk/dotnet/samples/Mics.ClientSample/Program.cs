using Mics.Client;

if (args.Length == 0)
{
    Console.WriteLine("Usage: dotnet run --project sdk/dotnet/samples/Mics.ClientSample -- --url ws://localhost:8080/ws --tenantId t1 --token valid:u1 --deviceId dev1 --toUserId u2");
    return;
}

var map = ParseArgs(args);
var url = new Uri(Required(map, "--url"));
var tenantId = Required(map, "--tenantId");
var token = Required(map, "--token");
var deviceId = Required(map, "--deviceId");
var toUserId = map.TryGetValue("--toUserId", out var to) ? to : "u2";

await using var client = new MicsClient(MicsClientOptions.Default with { HeartbeatInterval = TimeSpan.FromSeconds(10) });
client.Connected += s => Console.WriteLine($"connected tenant={s.TenantId} user={s.UserId} node={s.NodeId} trace={s.TraceId}");
client.DeliveryReceived += d => Console.WriteLine($"delivery msgId={d.Message?.MsgId} from={d.Message?.UserId}");
client.AckReceived += a => Console.WriteLine($"ack msgId={a.MsgId} status={a.Status} reason={a.Reason}");

await client.ConnectAsync(url, tenantId, token, deviceId);

var ack = await client.SendSingleChatAsync(toUserId, new byte[] { 1, 2, 3 });
Console.WriteLine($"send result={ack.Status} reason={ack.Reason}");

await Task.Delay(Timeout.InfiniteTimeSpan);

static Dictionary<string, string> ParseArgs(string[] args)
{
    var map = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var i = 0; i < args.Length; i++)
    {
        var key = args[i];
        if (!key.StartsWith("--", StringComparison.Ordinal))
        {
            continue;
        }

        var value = (i + 1) < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : "true";
        map[key] = value;
    }
    return map;
}

static string Required(IReadOnlyDictionary<string, string> map, string key)
{
    if (!map.TryGetValue(key, out var v) || string.IsNullOrWhiteSpace(v))
    {
        throw new ArgumentException("Missing arg: " + key);
    }
    return v;
}

