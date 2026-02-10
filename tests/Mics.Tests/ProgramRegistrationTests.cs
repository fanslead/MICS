namespace Mics.Tests;

public sealed class ProgramRegistrationTests
{
    [Fact]
    public void Program_RegistersMqDispatcherHostedService()
    {
        var root = FindRepoRoot();
        var path = Path.Combine(root, "src", "Mics.Gateway", "Program.cs");
        var text = File.ReadAllText(path);

        Assert.Contains("MqEventDispatcherService", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Program_RegistersShutdownDrainHostedService_AndReadinessChecksDraining()
    {
        var root = FindRepoRoot();
        var path = Path.Combine(root, "src", "Mics.Gateway", "Program.cs");
        var text = File.ReadAllText(path);

        Assert.Contains("ShutdownDrainService", text, StringComparison.Ordinal);
        Assert.Contains("/readyz", text, StringComparison.Ordinal);
        Assert.Contains("IsDraining", text, StringComparison.Ordinal);
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
