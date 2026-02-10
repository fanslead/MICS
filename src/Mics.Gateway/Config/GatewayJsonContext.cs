using System.Text.Json.Serialization;

namespace Mics.Gateway.Config;

[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, int>))]
internal partial class GatewayJsonContext : JsonSerializerContext;
