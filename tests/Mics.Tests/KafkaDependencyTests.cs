namespace Mics.Tests;

public sealed class KafkaDependencyTests
{
    [Fact]
    public void GatewayProject_ReferencesConfluentKafka()
    {
        var root = FindRepoRoot();
        var path = Path.Combine(root, "src", "Mics.Gateway", "Mics.Gateway.csproj");
        var text = File.ReadAllText(path);

        Assert.Contains("Confluent.Kafka", text, StringComparison.Ordinal);
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

