using System.Net.Http.Headers;
using Google.Protobuf;
using Microsoft.AspNetCore.Http;

namespace Mics.HookSdk;

public static class HookProtobufHttp
{
    public const string ProtobufContentType = "application/protobuf";

    public static async ValueTask<T> ReadAsync<T>(MessageParser<T> parser, HttpRequest request, CancellationToken cancellationToken)
        where T : class, IMessage<T>
    {
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(request);

        using var ms = new MemoryStream();
        await request.Body.CopyToAsync(ms, cancellationToken);
        return parser.ParseFrom(ms.ToArray());
    }

    public static async ValueTask WriteAsync<T>(T message, HttpResponse response, CancellationToken cancellationToken)
        where T : class, IMessage<T>
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(response);

        response.ContentType = ProtobufContentType;
        await response.Body.WriteAsync(message.ToByteArray(), cancellationToken);
    }

    public static bool IsProtobufContentType(HttpRequest request)
    {
        var raw = request.ContentType;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        if (!MediaTypeHeaderValue.TryParse(raw, out var mt))
        {
            return false;
        }

        return string.Equals(mt.MediaType, ProtobufContentType, StringComparison.OrdinalIgnoreCase);
    }
}
