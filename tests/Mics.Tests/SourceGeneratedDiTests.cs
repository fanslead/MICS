namespace Mics.Tests;

public sealed class SourceGeneratedDiTests
{
    [Fact]
    public void GatewayProject_ReferencesJab()
    {
        var root = FindRepoRoot();
        var path = Path.Combine(root, "src", "Mics.Gateway", "Mics.Gateway.csproj");
        var text = File.ReadAllText(path);

        Assert.Contains("Jab", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Program_UsesGeneratedGatewayServiceProvider()
    {
        var root = FindRepoRoot();
        var path = Path.Combine(root, "src", "Mics.Gateway", "Program.cs");
        var text = File.ReadAllText(path);

        Assert.Contains("GatewayServiceProvider", text, StringComparison.Ordinal);
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

