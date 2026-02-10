import type { IncomingMessage, ServerResponse } from "node:http";
import { verifyHookSign } from "./signing.js";
import {
  AuthRequestCodec,
  AuthResponseCodec,
  CheckMessageRequestCodec,
  CheckMessageResponseCodec,
  GetOfflineMessagesRequestCodec,
  GetOfflineMessagesResponseCodec,
  GetGroupMembersRequestCodec,
  GetGroupMembersResponseCodec,
  type AuthRequest,
  type AuthResponse,
  type CheckMessageRequest,
  type CheckMessageResponse,
  type GetOfflineMessagesRequest,
  type GetOfflineMessagesResponse,
  type GetGroupMembersRequest,
  type GetGroupMembersResponse,
  type HookMeta,
} from "./proto/mics_hook.js";

export type TenantSecretProvider = (tenantId: string) => string;

export type MicsHookHandlers = Partial<{
  auth: (req: AuthRequest) => Promise<AuthResponse> | AuthResponse;
  checkMessage: (req: CheckMessageRequest) => Promise<CheckMessageResponse> | CheckMessageResponse;
  getGroupMembers: (req: GetGroupMembersRequest) => Promise<GetGroupMembersResponse> | GetGroupMembersResponse;
  getOfflineMessages: (req: GetOfflineMessagesRequest) => Promise<GetOfflineMessagesResponse> | GetOfflineMessagesResponse;
}>;

export interface CreateMicsHookServerOptions {
  requireSign: boolean;
  tenantSecretProvider: TenantSecretProvider;
  handlers: MicsHookHandlers;
}

export function createMicsHookServer(options: CreateMicsHookServerOptions) {
  const requireSign = options.requireSign;
  const tenantSecretProvider = options.tenantSecretProvider;
  const handlers = options.handlers;

  return async function handle(req: IncomingMessage, res: ServerResponse): Promise<void> {
    if ((req.method ?? "").toUpperCase() !== "POST") {
      res.statusCode = 405;
      res.end();
      return;
    }

    const url = req.url ?? "/";
    const body = await readBody(req);

    if (url === "/auth") {
      const request = AuthRequestCodec.decode(body);
      const tenantId = request.meta?.tenantId ?? "";
      const { ok, secretOrReason } = resolveSecret(tenantSecretProvider, tenantId);
      if (!ok) {
        writeProtobuf(res, AuthResponseCodec.encode({ meta: echoMeta(request.meta), ok: false, userId: "", deviceId: "", config: undefined, reason: secretOrReason }).finish());
        return;
      }

      const verifyRes = verifyOrReason(requireSign, secretOrReason, request.meta, AuthRequestCodec.encode({ ...request, meta: clearSign(request.meta) }).finish());
      if (verifyRes !== "") {
        writeProtobuf(res, AuthResponseCodec.encode({ meta: echoMeta(request.meta), ok: false, userId: "", deviceId: "", config: undefined, reason: verifyRes }).finish());
        return;
      }

      const h = handlers.auth;
      const resp = h ? await h(request) : { meta: echoMeta(request.meta), ok: false, userId: "", deviceId: "", config: undefined, reason: "auth handler not configured" };
      if (!resp.meta) resp.meta = echoMeta(request.meta);
      writeProtobuf(res, AuthResponseCodec.encode(resp).finish());
      return;
    }

    if (url === "/check-message") {
      const request = CheckMessageRequestCodec.decode(body);
      const tenantId = request.meta?.tenantId ?? "";
      const { ok, secretOrReason } = resolveSecret(tenantSecretProvider, tenantId);
      if (!ok) {
        writeProtobuf(res, CheckMessageResponseCodec.encode({ meta: echoMeta(request.meta), allow: false, reason: secretOrReason }).finish());
        return;
      }

      const verifyRes = verifyOrReason(requireSign, secretOrReason, request.meta, CheckMessageRequestCodec.encode({ ...request, meta: clearSign(request.meta) }).finish());
      if (verifyRes !== "") {
        writeProtobuf(res, CheckMessageResponseCodec.encode({ meta: echoMeta(request.meta), allow: false, reason: verifyRes }).finish());
        return;
      }

      const h = handlers.checkMessage;
      const resp = h ? await h(request) : { meta: echoMeta(request.meta), allow: true, reason: "" };
      if (!resp.meta) resp.meta = echoMeta(request.meta);
      writeProtobuf(res, CheckMessageResponseCodec.encode(resp).finish());
      return;
    }

    if (url === "/get-group-members") {
      const request = GetGroupMembersRequestCodec.decode(body);
      const tenantId = request.meta?.tenantId ?? "";
      const { ok, secretOrReason } = resolveSecret(tenantSecretProvider, tenantId);
      if (!ok) {
        writeProtobuf(res, GetGroupMembersResponseCodec.encode({ meta: echoMeta(request.meta), userIds: [] }).finish());
        return;
      }

      const verifyRes = verifyOrReason(requireSign, secretOrReason, request.meta, GetGroupMembersRequestCodec.encode({ ...request, meta: clearSign(request.meta) }).finish());
      if (verifyRes !== "") {
        writeProtobuf(res, GetGroupMembersResponseCodec.encode({ meta: echoMeta(request.meta), userIds: [] }).finish());
        return;
      }

      const h = handlers.getGroupMembers;
      const resp = h ? await h(request) : { meta: echoMeta(request.meta), userIds: [] };
      if (!resp.meta) resp.meta = echoMeta(request.meta);
      writeProtobuf(res, GetGroupMembersResponseCodec.encode(resp).finish());
      return;
    }

    if (url === "/get-offline-messages") {
      const request = GetOfflineMessagesRequestCodec.decode(body);
      const tenantId = request.meta?.tenantId ?? "";
      const { ok, secretOrReason } = resolveSecret(tenantSecretProvider, tenantId);
      if (!ok) {
        writeProtobuf(
          res,
          GetOfflineMessagesResponseCodec.encode({
            meta: echoMeta(request.meta),
            ok: false,
            messages: [],
            reason: secretOrReason,
            nextCursor: "",
            hasMore: false,
          }).finish(),
        );
        return;
      }

      const verifyRes = verifyOrReason(requireSign, secretOrReason, request.meta, GetOfflineMessagesRequestCodec.encode({ ...request, meta: clearSign(request.meta) }).finish());
      if (verifyRes !== "") {
        writeProtobuf(
          res,
          GetOfflineMessagesResponseCodec.encode({
            meta: echoMeta(request.meta),
            ok: false,
            messages: [],
            reason: verifyRes,
            nextCursor: "",
            hasMore: false,
          }).finish(),
        );
        return;
      }

      const h = handlers.getOfflineMessages;
      const resp = h
        ? await h(request)
        : { meta: echoMeta(request.meta), ok: true, messages: [], reason: "", nextCursor: "", hasMore: false };
      if (!resp.meta) resp.meta = echoMeta(request.meta);
      writeProtobuf(res, GetOfflineMessagesResponseCodec.encode(resp).finish());
      return;
    }

    res.statusCode = 404;
    res.end();
  };
}

function writeProtobuf(res: ServerResponse, bytes: Uint8Array): void {
  res.statusCode = 200;
  res.setHeader("content-type", "application/protobuf");
  res.end(Buffer.from(bytes));
}

function resolveSecret(provider: TenantSecretProvider, tenantId: string): { ok: true; secretOrReason: string } | { ok: false; secretOrReason: string } {
  if (!tenantId) {
    return { ok: false, secretOrReason: "invalid tenant" };
  }
  const secret = provider(tenantId);
  if (!secret) {
    return { ok: false, secretOrReason: "unknown tenant" };
  }
  return { ok: true, secretOrReason: secret };
}

function verifyOrReason(requireSign: boolean, tenantSecret: string, meta: HookMeta | undefined, payloadForSignBytes: Uint8Array): string {
  if (!meta) return "missing meta";
  if (requireSign && !meta.sign) return "invalid sign";
  if (!meta.sign) return "";
  return verifyHookSign(tenantSecret, meta, payloadForSignBytes, meta.sign) ? "" : "invalid sign";
}

function echoMeta(meta: HookMeta | undefined): HookMeta {
  return meta
    ? { tenantId: meta.tenantId, requestId: meta.requestId, timestampMs: meta.timestampMs, sign: meta.sign, traceId: meta.traceId }
    : { tenantId: "", requestId: "", timestampMs: 0, sign: "", traceId: "" };
}

function clearSign(meta: HookMeta | undefined): HookMeta | undefined {
  if (!meta) return meta;
  return { ...meta, sign: "" };
}

function readBody(req: IncomingMessage): Promise<Uint8Array> {
  return new Promise((resolve, reject) => {
    const chunks: Buffer[] = [];
    req.on("error", reject);
    req.on("data", (chunk) => {
      chunks.push(Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk));
    });
    req.on("end", () => {
      resolve(Buffer.concat(chunks));
    });
  });
}
