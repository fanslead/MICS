import http from "node:http";
import { createMicsHookServer } from "../../dist/index.js";

const server = http.createServer(
  createMicsHookServer({
    requireSign: false,
    tenantSecretProvider: (tenantId) => (tenantId === "t1" ? "dev-secret-t1" : ""),
    handlers: {
      auth: async (req) => ({
        meta: req.meta,
        ok: true,
        userId: req.token?.replace(/^valid:/, "") ?? "u1",
        deviceId: req.deviceId ?? "",
        config: {
          hookBaseUrl: "http://localhost:8081",
          heartbeatTimeoutSeconds: 30,
          offlineBufferTtlSeconds: 300,
          tenantMaxConnections: 0,
          userMaxConnections: 0,
          tenantMaxMessageQps: 0,
          tenantSecret: "dev-secret-t1",
        },
        reason: "",
      }),
      checkMessage: async (req) => ({
        meta: req.meta,
        allow: true,
        reason: "",
      }),
      getGroupMembers: async (req) => ({
        meta: req.meta,
        userIds: req.groupId === "group-1" ? ["u1", "u2", "u3"] : [],
      }),
    },
  })
);

server.listen(8081, () => {
  // eslint-disable-next-line no-console
  console.log("Hook server listening on http://localhost:8081");
});

