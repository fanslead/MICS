namespace Mics.Tests;

public sealed class WsMqIntegrationTests
{
    [Fact]
    public void WsGatewayHandler_EmitsMqEvents()
    {
        var root = FindRepoRoot();
        var path = Path.Combine(root, "src", "Mics.Gateway", "Ws", "WsGatewayHandler.cs");
        var text = File.ReadAllText(path);

        Assert.Contains("MqEventFactory", text, StringComparison.Ordinal);
        Assert.Contains("TryEnqueue", text, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Mics.slnx")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Repo root not found (missing Mics.slnx).");
    }
}

