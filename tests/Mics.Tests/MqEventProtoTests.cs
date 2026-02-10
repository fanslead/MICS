using System.Text.RegularExpressions;

namespace Mics.Tests;

public sealed class MqEventProtoTests
{
    [Fact]
    public void HookProto_DefinesMqEvent_AndEventType()
    {
        var root = FindRepoRoot();
        var path = Path.Combine(root, "src", "Mics.Contracts", "Protos", "mics_hook.proto");
        var text = File.ReadAllText(path);

        Assert.Matches(new Regex(@"\bmessage\s+MqEvent\b", RegexOptions.CultureInvariant), text);
        Assert.Matches(new Regex(@"\benum\s+EventType\b", RegexOptions.CultureInvariant), text);

        // Required fields from需求文档 6.3.2 (simplified)
        Assert.Contains("string tenant_id = 1;", text, StringComparison.Ordinal);
        Assert.Contains("EventType event_type = 2;", text, StringComparison.Ordinal);
        Assert.Contains("string msg_id = 3;", text, StringComparison.Ordinal);
        Assert.Contains("string user_id = 4;", text, StringComparison.Ordinal);
        Assert.Contains("string device_id = 5;", text, StringComparison.Ordinal);
        Assert.Contains("string to_user_id = 6;", text, StringComparison.Ordinal);
        Assert.Contains("string group_id = 7;", text, StringComparison.Ordinal);
        Assert.Contains("bytes event_data = 8;", text, StringComparison.Ordinal);
        Assert.Contains("int64 timestamp = 9;", text, StringComparison.Ordinal);
        Assert.Contains("string node_id = 10;", text, StringComparison.Ordinal);
        Assert.Contains("string sign = 11;", text, StringComparison.Ordinal);
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
