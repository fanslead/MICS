namespace Mics.LoadTester;

public sealed record LoadTestOptions(
    Uri BaseUrl,
    string TenantId,
    int Connections,
    int RampSeconds,
    int DurationSeconds,
    LoadTestMode Mode,
    double SendQpsPerConnection,
    int PayloadBytes,
    string TokenPrefix,
    string DevicePrefix,
    string? GroupId)
{
    public static LoadTestOptions Parse(string[] args)
    {
        if (args is null)
        {
            throw new ArgumentNullException(nameof(args));
        }

        Uri? baseUrl = null;
        string? tenantId = null;
        var connections = 1;
        var rampSeconds = 0;
        var durationSeconds = 10;
        var mode = LoadTestMode.ConnectOnly;
        var sendQpsPerConnection = 0d;
        var payloadBytes = 32;
        var tokenPrefix = "valid:";
        var devicePrefix = "dev-";
        string? groupId = null;

        for (var i = 0; i < args.Length; i++)
        {
            var key = args[i];
            if (!key.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Unexpected argument: '{key}'", nameof(args));
            }

            if (string.Equals(key, "--help", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "--h", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "--?", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("help requested", nameof(args));
            }

            if (i + 1 >= args.Length)
            {
                throw new ArgumentException($"Missing value for '{key}'", nameof(args));
            }

            var value = args[++i];
            switch (key.ToLowerInvariant())
            {
                case "--url":
                    baseUrl = new Uri(value, UriKind.Absolute);
                    break;
                case "--tenantid":
                    tenantId = value;
                    break;
                case "--connections":
                    connections = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--rampseconds":
                    rampSeconds = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--durationseconds":
                    durationSeconds = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--mode":
                    mode = ParseMode(value);
                    break;
                case "--sendqpsperconn":
                case "--sendqpsperconnection":
                    sendQpsPerConnection = double.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--payloadbytes":
                    payloadBytes = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--tokenprefix":
                    tokenPrefix = value;
                    break;
                case "--deviceprefix":
                    devicePrefix = value;
                    break;
                case "--groupid":
                    groupId = value;
                    break;
                default:
                    throw new ArgumentException($"Unknown option: '{key}'", nameof(args));
            }
        }

        if (baseUrl is null)
        {
            throw new ArgumentException("Missing required option: --url", nameof(args));
        }

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("Missing required option: --tenantId", nameof(args));
        }

        if (connections <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(args), "connections must be > 0");
        }

        if (rampSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(args), "rampSeconds must be >= 0");
        }

        if (durationSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(args), "durationSeconds must be > 0");
        }

        if (sendQpsPerConnection < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(args), "sendQpsPerConnection must be >= 0");
        }

        if (payloadBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(args), "payloadBytes must be >= 0");
        }

        if (mode is LoadTestMode.GroupChat && string.IsNullOrWhiteSpace(groupId))
        {
            throw new ArgumentException("Missing required option for group-chat: --groupId", nameof(args));
        }

        return new LoadTestOptions(
            baseUrl,
            tenantId.Trim(),
            connections,
            rampSeconds,
            durationSeconds,
            mode,
            sendQpsPerConnection,
            payloadBytes,
            tokenPrefix,
            devicePrefix,
            groupId);
    }

    private static LoadTestMode ParseMode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return LoadTestMode.ConnectOnly;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "connect-only" or "connectonly" or "connect" => LoadTestMode.ConnectOnly,
            "heartbeat" => LoadTestMode.Heartbeat,
            "single-chat" or "singlechat" or "single" => LoadTestMode.SingleChat,
            "group-chat" or "groupchat" or "group" => LoadTestMode.GroupChat,
            _ => throw new ArgumentException($"Unknown mode: '{value}'", nameof(value)),
        };
    }
}
