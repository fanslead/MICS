import { describe, expect, test } from "vitest";
import { createMicsHookServer } from "./server.js";
import { AuthRequestCodec } from "./proto/mics_hook.js";

function makeReq(path: string, body: Uint8Array, headers?: Record<string, string>) {
  const chunks = [Buffer.from(body)];
  const req: any = {
    method: "POST",
    url: path,
    headers: {
      "content-type": "application/protobuf",
      ...(headers ?? {}),
    },
    on(event: string, cb: (chunk?: any) => void) {
      if (event === "data") {
        for (const c of chunks) cb(c);
      }
      if (event === "end") cb();
    },
  };
  return req;
}

function makeRes() {
  const res: any = {
    statusCode: 200,
    headers: {} as Record<string, string>,
    body: Buffer.alloc(0),
    setHeader(name: string, value: string) {
      this.headers[name.toLowerCase()] = value;
    },
    end(payload?: any) {
      if (payload) this.body = Buffer.isBuffer(payload) ? payload : Buffer.from(payload);
    },
  };
  return res;
}

describe("server", () => {
  test("auth endpoint decodes protobuf and encodes protobuf response", async () => {
    const handler = createMicsHookServer({
      requireSign: false,
      tenantSecretProvider: (tenantId) => (tenantId === "t1" ? "s" : ""),
      handlers: {
        auth: async (req) => ({
          meta: req.meta,
          ok: true,
          userId: "u1",
          deviceId: req.deviceId,
          config: { hookBaseUrl: "http://hook", heartbeatTimeoutSeconds: 30, offlineBufferTtlSeconds: 300, tenantMaxConnections: 0, userMaxConnections: 0, tenantMaxMessageQps: 0, tenantSecret: "s" },
          reason: "",
        }),
      },
    });

    const payload = AuthRequestCodec.encode({ meta: { tenantId: "t1", requestId: "r1", timestampMs: 1, sign: "", traceId: "tr1" }, token: "valid:u1", deviceId: "d1" }).finish();
    const req = makeReq("/auth", payload);
    const res = makeRes();

    await handler(req, res);
    expect(res.statusCode).toBe(200);
    expect(res.headers["content-type"]).toContain("application/protobuf");
    expect(res.body.length).toBeGreaterThan(0);
  });
});
