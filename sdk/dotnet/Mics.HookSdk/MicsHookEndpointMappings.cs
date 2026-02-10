using Google.Protobuf;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Mics.Contracts.Hook.V1;

namespace Mics.HookSdk;

public static class MicsHookEndpointMappings
{
    public static IEndpointConventionBuilder MapMicsAuth(
        this IEndpointRouteBuilder endpoints,
        Func<AuthRequest, CancellationToken, ValueTask<AuthResponse>> handler,
        MicsHookMapOptions options,
        string pattern = "/auth")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(options);

        return endpoints.MapPost(pattern, async (HttpContext ctx) =>
        {
            var req = await HookProtobufHttp.ReadAsync(AuthRequest.Parser, ctx.Request, ctx.RequestAborted);
            var tenantId = req.Meta?.TenantId ?? "";
            var (ok, secretOrReason) = ResolveSecret(options, tenantId);

            if (!ok)
            {
                await HookProtobufHttp.WriteAsync(new AuthResponse
                {
                    Meta = EchoMeta(req.Meta),
                    Ok = false,
                    Reason = secretOrReason
                }, ctx.Response, ctx.RequestAborted);
                return;
            }

            if (!VerifyOrReject(options, secretOrReason, req.Meta, PayloadForSign(req), out var rejectReason))
            {
                await HookProtobufHttp.WriteAsync(new AuthResponse
                {
                    Meta = EchoMeta(req.Meta),
                    Ok = false,
                    Reason = rejectReason
                }, ctx.Response, ctx.RequestAborted);
                return;
            }

            var resp = await handler(req, ctx.RequestAborted);
            resp.Meta ??= EchoMeta(req.Meta);
            await HookProtobufHttp.WriteAsync(resp, ctx.Response, ctx.RequestAborted);
        });
    }

    public static IEndpointConventionBuilder MapMicsCheckMessage(
        this IEndpointRouteBuilder endpoints,
        Func<CheckMessageRequest, CancellationToken, ValueTask<CheckMessageResponse>> handler,
        MicsHookMapOptions options,
        string pattern = "/check-message")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(options);

        return endpoints.MapPost(pattern, async (HttpContext ctx) =>
        {
            var req = await HookProtobufHttp.ReadAsync(CheckMessageRequest.Parser, ctx.Request, ctx.RequestAborted);
            var tenantId = req.Meta?.TenantId ?? "";
            var (ok, secretOrReason) = ResolveSecret(options, tenantId);

            if (!ok)
            {
                await HookProtobufHttp.WriteAsync(new CheckMessageResponse
                {
                    Meta = EchoMeta(req.Meta),
                    Allow = false,
                    Reason = secretOrReason
                }, ctx.Response, ctx.RequestAborted);
                return;
            }

            if (!VerifyOrReject(options, secretOrReason, req.Meta, PayloadForSign(req), out var rejectReason))
            {
                await HookProtobufHttp.WriteAsync(new CheckMessageResponse
                {
                    Meta = EchoMeta(req.Meta),
                    Allow = false,
                    Reason = rejectReason
                }, ctx.Response, ctx.RequestAborted);
                return;
            }

            var resp = await handler(req, ctx.RequestAborted);
            resp.Meta ??= EchoMeta(req.Meta);
            await HookProtobufHttp.WriteAsync(resp, ctx.Response, ctx.RequestAborted);
        });
    }

    public static IEndpointConventionBuilder MapMicsGetGroupMembers(
        this IEndpointRouteBuilder endpoints,
        Func<GetGroupMembersRequest, CancellationToken, ValueTask<GetGroupMembersResponse>> handler,
        MicsHookMapOptions options,
        string pattern = "/get-group-members")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(options);

        return endpoints.MapPost(pattern, async (HttpContext ctx) =>
        {
            var req = await HookProtobufHttp.ReadAsync(GetGroupMembersRequest.Parser, ctx.Request, ctx.RequestAborted);
            var tenantId = req.Meta?.TenantId ?? "";
            var (ok, secretOrReason) = ResolveSecret(options, tenantId);

            if (!ok)
            {
                await HookProtobufHttp.WriteAsync(new GetGroupMembersResponse
                {
                    Meta = EchoMeta(req.Meta),
                }, ctx.Response, ctx.RequestAborted);
                return;
            }

            if (!VerifyOrReject(options, secretOrReason, req.Meta, PayloadForSign(req), out _))
            {
                await HookProtobufHttp.WriteAsync(new GetGroupMembersResponse
                {
                    Meta = EchoMeta(req.Meta),
                }, ctx.Response, ctx.RequestAborted);
                return;
            }

            var resp = await handler(req, ctx.RequestAborted);
            resp.Meta ??= EchoMeta(req.Meta);
            await HookProtobufHttp.WriteAsync(resp, ctx.Response, ctx.RequestAborted);
        });
    }

    public static IEndpointConventionBuilder MapMicsGetOfflineMessages(
        this IEndpointRouteBuilder endpoints,
        Func<GetOfflineMessagesRequest, CancellationToken, ValueTask<GetOfflineMessagesResponse>> handler,
        MicsHookMapOptions options,
        string pattern = "/get-offline-messages")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(options);

        return endpoints.MapPost(pattern, async (HttpContext ctx) =>
        {
            var req = await HookProtobufHttp.ReadAsync(GetOfflineMessagesRequest.Parser, ctx.Request, ctx.RequestAborted);
            var tenantId = req.Meta?.TenantId ?? "";
            var (ok, secretOrReason) = ResolveSecret(options, tenantId);

            if (!ok)
            {
                await HookProtobufHttp.WriteAsync(new GetOfflineMessagesResponse
                {
                    Meta = EchoMeta(req.Meta),
                    Ok = false,
                    Reason = secretOrReason,
                }, ctx.Response, ctx.RequestAborted);
                return;
            }

            if (!VerifyOrReject(options, secretOrReason, req.Meta, PayloadForSign(req), out var rejectReason))
            {
                await HookProtobufHttp.WriteAsync(new GetOfflineMessagesResponse
                {
                    Meta = EchoMeta(req.Meta),
                    Ok = false,
                    Reason = rejectReason,
                }, ctx.Response, ctx.RequestAborted);
                return;
            }

            var resp = await handler(req, ctx.RequestAborted);
            resp.Meta ??= EchoMeta(req.Meta);
            await HookProtobufHttp.WriteAsync(resp, ctx.Response, ctx.RequestAborted);
        });
    }

    private static (bool Ok, string SecretOrReason) ResolveSecret(MicsHookMapOptions options, string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return (false, "invalid tenant");
        }

        var secret = options.TenantSecretProvider(tenantId);
        if (string.IsNullOrWhiteSpace(secret))
        {
            return (false, "unknown tenant");
        }

        return (true, secret);
    }

    private static bool VerifyOrReject(MicsHookMapOptions options, string tenantSecret, HookMeta? meta, IMessage payloadForSign, out string reason)
    {
        reason = "";

        if (meta is null)
        {
            reason = "missing meta";
            return false;
        }

        if (options.RequireSign && string.IsNullOrWhiteSpace(meta.Sign))
        {
            reason = "invalid sign";
            return false;
        }

        if (string.IsNullOrWhiteSpace(meta.Sign))
        {
            return true;
        }

        if (!HookVerifier.Verify(tenantSecret, meta, payloadForSign))
        {
            reason = "invalid sign";
            return false;
        }

        return true;
    }

    private static HookMeta EchoMeta(HookMeta? meta) =>
        meta is null
            ? new HookMeta { TenantId = "", RequestId = "", TimestampMs = 0, Sign = "", TraceId = "" }
            : new HookMeta { TenantId = meta.TenantId, RequestId = meta.RequestId, TimestampMs = meta.TimestampMs, Sign = meta.Sign, TraceId = meta.TraceId };

    private static AuthRequest PayloadForSign(AuthRequest request)
    {
        var clone = request.Clone();
        if (clone.Meta is not null)
        {
            clone.Meta.Sign = "";
        }
        return clone;
    }

    private static CheckMessageRequest PayloadForSign(CheckMessageRequest request)
    {
        var clone = request.Clone();
        if (clone.Meta is not null)
        {
            clone.Meta.Sign = "";
        }
        return clone;
    }

    private static GetGroupMembersRequest PayloadForSign(GetGroupMembersRequest request)
    {
        var clone = request.Clone();
        if (clone.Meta is not null)
        {
            clone.Meta.Sign = "";
        }
        return clone;
    }

    private static GetOfflineMessagesRequest PayloadForSign(GetOfflineMessagesRequest request)
    {
        var clone = request.Clone();
        if (clone.Meta is not null)
        {
            clone.Meta.Sign = "";
        }
        return clone;
    }
}
