// See https://aka.ms/new-console-template for more information
using Mics.LoadTester;

if (args.Any(a => string.Equals(a, "--help", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "-h", StringComparison.OrdinalIgnoreCase)))
{
    PrintUsage();
    return;
}

LoadTestOptions options;
try
{
    options = LoadTestOptions.Parse(args);
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    PrintUsage();
    return;
}

using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(options.DurationSeconds));
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

await LoadRunner.RunAsync(options, cts.Token);

static void PrintUsage()
{
    Console.WriteLine(
        """
        Mics.LoadTester (.NET ClientWebSocket)

        Required:
          --url ws://host:port/ws
          --tenantId t1
          --connections 1000
          --durationSeconds 30

        Optional:
          --rampSeconds 10
          --mode connect-only|heartbeat|single-chat|group-chat
          --sendQpsPerConn 1
          --payloadBytes 32
          --tokenPrefix valid:
          --devicePrefix dev-
          --groupId group-1   (required for group-chat)

        Examples:
          dotnet run --project tools/Mics.LoadTester -- --url ws://localhost:8080/ws --tenantId t1 --connections 100 --durationSeconds 30 --mode heartbeat --sendQpsPerConn 1
          dotnet run --project tools/Mics.LoadTester -- --url ws://localhost:8080/ws --tenantId t1 --connections 200 --durationSeconds 30 --mode single-chat --sendQpsPerConn 2 --payloadBytes 128
        """);
}
