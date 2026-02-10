package com.mics.hooksdk.http;

import com.mics.contracts.hook.v1.AuthRequest;
import com.mics.contracts.hook.v1.AuthResponse;
import com.mics.contracts.hook.v1.CheckMessageRequest;
import com.mics.contracts.hook.v1.CheckMessageResponse;
import com.mics.contracts.hook.v1.GetGroupMembersRequest;
import com.mics.contracts.hook.v1.GetGroupMembersResponse;
import com.mics.contracts.hook.v1.GetOfflineMessagesRequest;
import com.mics.contracts.hook.v1.GetOfflineMessagesResponse;
import com.mics.contracts.hook.v1.HookMeta;
import com.mics.hooksdk.HookSigner;
import com.sun.net.httpserver.HttpExchange;
import com.sun.net.httpserver.HttpHandler;
import com.sun.net.httpserver.HttpServer;

import java.io.Closeable;
import java.io.IOException;
import java.net.InetSocketAddress;
import java.util.Objects;

public final class MicsHookHttpServer implements Closeable {
    private final HttpServer server;

    public MicsHookHttpServer(InetSocketAddress address, MicsHookHandler handler, MicsHookServerOptions options) throws IOException {
        Objects.requireNonNull(address, "address");
        Objects.requireNonNull(handler, "handler");
        Objects.requireNonNull(options, "options");

        this.server = HttpServer.create(address, 0);
        this.server.createContext("/auth", new AuthHandler(handler, options));
        this.server.createContext("/check-message", new CheckMessageHandler(handler, options));
        this.server.createContext("/get-group-members", new GetGroupMembersHandler(handler, options));
        this.server.createContext("/get-offline-messages", new GetOfflineMessagesHandler(handler, options));
    }

    public InetSocketAddress getAddress() {
        return server.getAddress();
    }

    public void start() {
        server.start();
    }

    @Override
    public void close() {
        server.stop(0);
    }

    private static final class AuthHandler implements HttpHandler {
        private final MicsHookHandler handler;
        private final MicsHookServerOptions options;

        private AuthHandler(MicsHookHandler handler, MicsHookServerOptions options) {
            this.handler = handler;
            this.options = options;
        }

        @Override
        public void handle(HttpExchange exchange) throws IOException {
            if (!"POST".equalsIgnoreCase(exchange.getRequestMethod())) {
                ProtobufHttp.writeText(exchange, 405, "Method Not Allowed");
                return;
            }

            AuthRequest req;
            try {
                req = AuthRequest.parseFrom(ProtobufHttp.readBody(exchange));
            } catch (Exception e) {
                ProtobufHttp.writeText(exchange, 400, "Bad Request");
                return;
            }

            HookMeta meta = req.hasMeta() ? req.getMeta() : HookMeta.getDefaultInstance();
            String tenantId = meta.getTenantId();
            String secretOrReason = resolveSecretOrReason(options, tenantId);
            if (secretOrReason.startsWith("reason:")) {
                ProtobufHttp.writeProtobuf(exchange, 200, AuthResponse.newBuilder()
                        .setMeta(echoMeta(meta))
                        .setOk(false)
                        .setReason(secretOrReason.substring("reason:".length()))
                        .build());
                return;
            }

            AuthRequest payloadForSign = clearMetaSign(req);
            boolean verified = HookSigner.verify(secretOrReason, meta, payloadForSign, options.isRequireSign());
            if (!verified) {
                ProtobufHttp.writeProtobuf(exchange, 200, AuthResponse.newBuilder()
                        .setMeta(echoMeta(meta))
                        .setOk(false)
                        .setReason("invalid sign")
                        .build());
                return;
            }

            try {
                AuthResponse resp = handler.onAuth(req);
                AuthResponse.Builder b = resp == null ? AuthResponse.newBuilder() : resp.toBuilder();
                if (!b.hasMeta()) {
                    b.setMeta(echoMeta(meta));
                }
                ProtobufHttp.writeProtobuf(exchange, 200, b.build());
            } catch (Exception e) {
                ProtobufHttp.writeProtobuf(exchange, 200, AuthResponse.newBuilder()
                        .setMeta(echoMeta(meta))
                        .setOk(false)
                        .setReason("handler error")
                        .build());
            }
        }
    }

    private static final class CheckMessageHandler implements HttpHandler {
        private final MicsHookHandler handler;
        private final MicsHookServerOptions options;

        private CheckMessageHandler(MicsHookHandler handler, MicsHookServerOptions options) {
            this.handler = handler;
            this.options = options;
        }

        @Override
        public void handle(HttpExchange exchange) throws IOException {
            if (!"POST".equalsIgnoreCase(exchange.getRequestMethod())) {
                ProtobufHttp.writeText(exchange, 405, "Method Not Allowed");
                return;
            }

            CheckMessageRequest req;
            try {
                req = CheckMessageRequest.parseFrom(ProtobufHttp.readBody(exchange));
            } catch (Exception e) {
                ProtobufHttp.writeText(exchange, 400, "Bad Request");
                return;
            }

            HookMeta meta = req.hasMeta() ? req.getMeta() : HookMeta.getDefaultInstance();
            String tenantId = meta.getTenantId();
            String secretOrReason = resolveSecretOrReason(options, tenantId);
            if (secretOrReason.startsWith("reason:")) {
                ProtobufHttp.writeProtobuf(exchange, 200, CheckMessageResponse.newBuilder()
                        .setMeta(echoMeta(meta))
                        .setAllow(false)
                        .setReason(secretOrReason.substring("reason:".length()))
                        .build());
                return;
            }

            CheckMessageRequest payloadForSign = clearMetaSign(req);
            boolean verified = HookSigner.verify(secretOrReason, meta, payloadForSign, options.isRequireSign());
            if (!verified) {
                ProtobufHttp.writeProtobuf(exchange, 200, CheckMessageResponse.newBuilder()
                        .setMeta(echoMeta(meta))
                        .setAllow(false)
                        .setReason("invalid sign")
                        .build());
                return;
            }

            try {
                CheckMessageResponse resp = handler.onCheckMessage(req);
                CheckMessageResponse.Builder b = resp == null ? CheckMessageResponse.newBuilder() : resp.toBuilder();
                if (!b.hasMeta()) {
                    b.setMeta(echoMeta(meta));
                }
                ProtobufHttp.writeProtobuf(exchange, 200, b.build());
            } catch (Exception e) {
                ProtobufHttp.writeProtobuf(exchange, 200, CheckMessageResponse.newBuilder()
                        .setMeta(echoMeta(meta))
                        .setAllow(false)
                        .setReason("handler error")
                        .build());
            }
        }
    }

    private static final class GetGroupMembersHandler implements HttpHandler {
        private final MicsHookHandler handler;
        private final MicsHookServerOptions options;

        private GetGroupMembersHandler(MicsHookHandler handler, MicsHookServerOptions options) {
            this.handler = handler;
            this.options = options;
        }

        @Override
        public void handle(HttpExchange exchange) throws IOException {
            if (!"POST".equalsIgnoreCase(exchange.getRequestMethod())) {
                ProtobufHttp.writeText(exchange, 405, "Method Not Allowed");
                return;
            }

            GetGroupMembersRequest req;
            try {
                req = GetGroupMembersRequest.parseFrom(ProtobufHttp.readBody(exchange));
            } catch (Exception e) {
                ProtobufHttp.writeText(exchange, 400, "Bad Request");
                return;
            }

            HookMeta meta = req.hasMeta() ? req.getMeta() : HookMeta.getDefaultInstance();
            String tenantId = meta.getTenantId();
            String secretOrReason = resolveSecretOrReason(options, tenantId);
            if (secretOrReason.startsWith("reason:")) {
                ProtobufHttp.writeProtobuf(exchange, 200, GetGroupMembersResponse.newBuilder()
                        .setMeta(echoMeta(meta))
                        .build());
                return;
            }

            GetGroupMembersRequest payloadForSign = clearMetaSign(req);
            boolean verified = HookSigner.verify(secretOrReason, meta, payloadForSign, options.isRequireSign());
            if (!verified) {
                ProtobufHttp.writeProtobuf(exchange, 200, GetGroupMembersResponse.newBuilder()
                        .setMeta(echoMeta(meta))
                        .build());
                return;
            }

            try {
                GetGroupMembersResponse resp = handler.onGetGroupMembers(req);
                GetGroupMembersResponse.Builder b = resp == null ? GetGroupMembersResponse.newBuilder() : resp.toBuilder();
                if (!b.hasMeta()) {
                    b.setMeta(echoMeta(meta));
                }
                ProtobufHttp.writeProtobuf(exchange, 200, b.build());
            } catch (Exception e) {
                ProtobufHttp.writeProtobuf(exchange, 200, GetGroupMembersResponse.newBuilder()
                        .setMeta(echoMeta(meta))
                        .build());
            }
        }
    }

    private static final class GetOfflineMessagesHandler implements HttpHandler {
        private final MicsHookHandler handler;
        private final MicsHookServerOptions options;

        private GetOfflineMessagesHandler(MicsHookHandler handler, MicsHookServerOptions options) {
            this.handler = handler;
            this.options = options;
        }

        @Override
        public void handle(HttpExchange exchange) throws IOException {
            if (!"POST".equalsIgnoreCase(exchange.getRequestMethod())) {
                ProtobufHttp.writeText(exchange, 405, "Method Not Allowed");
                return;
            }

            GetOfflineMessagesRequest req;
            try {
                req = GetOfflineMessagesRequest.parseFrom(ProtobufHttp.readBody(exchange));
            } catch (Exception e) {
                ProtobufHttp.writeText(exchange, 400, "Bad Request");
                return;
            }

            HookMeta meta = req.hasMeta() ? req.getMeta() : HookMeta.getDefaultInstance();
            String tenantId = meta.getTenantId();
            String secretOrReason = resolveSecretOrReason(options, tenantId);
            if (secretOrReason.startsWith("reason:")) {
                ProtobufHttp.writeProtobuf(exchange, 200, GetOfflineMessagesResponse.newBuilder()
                        .setMeta(echoMeta(meta))
                        .setOk(false)
                        .setReason(secretOrReason.substring("reason:".length()))
                        .build());
                return;
            }

            GetOfflineMessagesRequest payloadForSign = clearMetaSign(req);
            boolean verified = HookSigner.verify(secretOrReason, meta, payloadForSign, options.isRequireSign());
            if (!verified) {
                ProtobufHttp.writeProtobuf(exchange, 200, GetOfflineMessagesResponse.newBuilder()
                        .setMeta(echoMeta(meta))
                        .setOk(false)
                        .setReason("invalid sign")
                        .build());
                return;
            }

            try {
                GetOfflineMessagesResponse resp = handler.onGetOfflineMessages(req);
                GetOfflineMessagesResponse.Builder b = resp == null ? GetOfflineMessagesResponse.newBuilder().setOk(true) : resp.toBuilder();
                if (!b.hasMeta()) {
                    b.setMeta(echoMeta(meta));
                }
                ProtobufHttp.writeProtobuf(exchange, 200, b.build());
            } catch (Exception e) {
                ProtobufHttp.writeProtobuf(exchange, 200, GetOfflineMessagesResponse.newBuilder()
                        .setMeta(echoMeta(meta))
                        .setOk(false)
                        .setReason("handler error")
                        .build());
            }
        }
    }

    private static String resolveSecretOrReason(MicsHookServerOptions options, String tenantId) {
        if (tenantId == null || tenantId.isBlank()) {
            return "reason:invalid tenant";
        }
        String secret = options.getTenantSecretProvider().apply(tenantId);
        if (secret == null || secret.isBlank()) {
            return "reason:unknown tenant";
        }
        return secret;
    }

    private static HookMeta echoMeta(HookMeta meta) {
        HookMeta m = meta == null ? HookMeta.getDefaultInstance() : meta;
        return HookMeta.newBuilder()
                .setTenantId(m.getTenantId())
                .setRequestId(m.getRequestId())
                .setTimestampMs(m.getTimestampMs())
                .setSign(m.getSign())
                .setTraceId(m.getTraceId())
                .build();
    }

    private static AuthRequest clearMetaSign(AuthRequest request) {
        if (request == null || !request.hasMeta()) {
            return request;
        }
        HookMeta cleared = request.getMeta().toBuilder().clearSign().build();
        return request.toBuilder().setMeta(cleared).build();
    }

    private static CheckMessageRequest clearMetaSign(CheckMessageRequest request) {
        if (request == null || !request.hasMeta()) {
            return request;
        }
        HookMeta cleared = request.getMeta().toBuilder().clearSign().build();
        return request.toBuilder().setMeta(cleared).build();
    }

    private static GetGroupMembersRequest clearMetaSign(GetGroupMembersRequest request) {
        if (request == null || !request.hasMeta()) {
            return request;
        }
        HookMeta cleared = request.getMeta().toBuilder().clearSign().build();
        return request.toBuilder().setMeta(cleared).build();
    }

    private static GetOfflineMessagesRequest clearMetaSign(GetOfflineMessagesRequest request) {
        if (request == null || !request.hasMeta()) {
            return request;
        }
        HookMeta cleared = request.getMeta().toBuilder().clearSign().build();
        return request.toBuilder().setMeta(cleared).build();
    }
}
