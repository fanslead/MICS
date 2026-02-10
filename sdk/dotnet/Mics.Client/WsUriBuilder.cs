namespace Mics.Client;

internal static class WsUriBuilder
{
    public static Uri Build(Uri baseUrl, string tenantId, string token, string deviceId)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);
        tenantId ??= "";
        token ??= "";
        deviceId ??= "";

        var builder = new UriBuilder(baseUrl);
        var query = builder.Query;
        if (!string.IsNullOrEmpty(query))
        {
            query = query.TrimStart('?');
            if (query.Length > 0 && !query.EndsWith('&'))
            {
                query += "&";
            }
        }
        else
        {
            query = "";
        }

        query += "tenantId=" + Uri.EscapeDataString(tenantId)
                 + "&token=" + Uri.EscapeDataString(token)
                 + "&deviceId=" + Uri.EscapeDataString(deviceId);

        builder.Query = query;
        return builder.Uri;
    }
}

