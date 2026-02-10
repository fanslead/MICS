package com.mics.samples.hookserver;

import com.mics.contracts.hook.v1.AuthRequest;
import com.mics.contracts.hook.v1.AuthResponse;
import com.mics.contracts.hook.v1.CheckMessageRequest;
import com.mics.contracts.hook.v1.CheckMessageResponse;
import com.mics.contracts.hook.v1.GetGroupMembersRequest;
import com.mics.contracts.hook.v1.GetGroupMembersResponse;
import com.mics.contracts.hook.v1.TenantRuntimeConfig;
import com.mics.contracts.message.v1.MessageRequest;
import com.mics.hooksdk.http.MicsHookHandler;
import com.mics.hooksdk.http.MicsHookHttpServer;
import com.mics.hooksdk.http.MicsHookServerOptions;

import java.net.InetSocketAddress;
import java.util.Arrays;
import java.util.Collections;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.concurrent.CountDownLatch;

public final class Main {
    public static void main(String[] args) throws Exception {
        int port = envInt("PORT", 8091);
        boolean requireSign = envBool("REQUIRE_SIGN", true);

        Map<String, String> tenantSecrets = parseTenantSecrets(System.getenv("TENANT_SECRETS"));
        if (tenantSecrets.isEmpty()) {
            tenantSecrets = new HashMap<>();
            tenantSecrets.put("t1", "secret");
        }

        Map<String, List<String>> groupMembers = parseGroupMembers(System.getenv("GROUP_MEMBERS"));
        if (groupMembers.isEmpty()) {
            groupMembers = new HashMap<>();
            groupMembers.put("group-1", List.of("u1", "u2", "u3"));
        }

        String publicUrl = System.getenv("PUBLIC_URL");
        if (publicUrl == null || publicUrl.isBlank()) {
            publicUrl = "http://127.0.0.1:" + port;
        }

        MicsHookHandler handler = new DemoHandler(tenantSecrets, groupMembers, publicUrl);
        MicsHookServerOptions options = new MicsHookServerOptions(tenantSecrets::get, requireSign);

        try (MicsHookHttpServer server = new MicsHookHttpServer(new InetSocketAddress(port), handler, options)) {
            server.start();
            System.out.println("MICS Hook sample server listening on " + publicUrl + " (requireSign=" + requireSign + ")");
            new CountDownLatch(1).await();
        }
    }

    private static final class DemoHandler implements MicsHookHandler {
        private final Map<String, String> secrets;
        private final Map<String, List<String>> groupMembers;
        private final String publicUrl;

        private DemoHandler(Map<String, String> secrets, Map<String, List<String>> groupMembers, String publicUrl) {
            this.secrets = secrets;
            this.groupMembers = groupMembers;
            this.publicUrl = publicUrl;
        }

        @Override
        public AuthResponse onAuth(AuthRequest request) {
            String tenantId = request.hasMeta() ? request.getMeta().getTenantId() : "";
            String token = request.getToken();
            if (token == null || !token.startsWith("valid:")) {
                return AuthResponse.newBuilder()
                        .setOk(false)
                        .setReason("invalid token")
                        .build();
            }

            String userId = token.substring("valid:".length());
            String secret = secrets.getOrDefault(tenantId, "");

            TenantRuntimeConfig cfg = TenantRuntimeConfig.newBuilder()
                    .setHookBaseUrl(publicUrl)
                    .setHeartbeatTimeoutSeconds(30)
                    .setOfflineBufferTtlSeconds(30)
                    .setTenantMaxConnections(100_000)
                    .setUserMaxConnections(3)
                    .setTenantMaxMessageQps(10_000)
                    .setTenantSecret(secret)
                    .build();

            return AuthResponse.newBuilder()
                    .setOk(true)
                    .setUserId(userId)
                    .setDeviceId(request.getDeviceId())
                    .setConfig(cfg)
                    .build();
        }

        @Override
        public CheckMessageResponse onCheckMessage(CheckMessageRequest request) {
            MessageRequest msg = request.getMessage();
            boolean allow = msg.getMsgBody() != null && !msg.getMsgBody().isEmpty();
            return CheckMessageResponse.newBuilder()
                    .setAllow(allow)
                    .setReason(allow ? "" : "empty msg_body")
                    .build();
        }

        @Override
        public GetGroupMembersResponse onGetGroupMembers(GetGroupMembersRequest request) {
            List<String> members = groupMembers.getOrDefault(request.getGroupId(), Collections.emptyList());
            return GetGroupMembersResponse.newBuilder()
                    .addAllUserIds(members)
                    .build();
        }
    }

    private static Map<String, String> parseTenantSecrets(String env) {
        if (env == null || env.isBlank()) {
            return new HashMap<>();
        }
        Map<String, String> map = new HashMap<>();
        for (String pair : env.split(",")) {
            String[] kv = pair.trim().split("=", 2);
            if (kv.length == 2 && !kv[0].isBlank()) {
                map.put(kv[0].trim(), kv[1].trim());
            }
        }
        return map;
    }

    private static Map<String, List<String>> parseGroupMembers(String env) {
        if (env == null || env.isBlank()) {
            return new HashMap<>();
        }
        Map<String, List<String>> map = new HashMap<>();
        for (String pair : env.split(",")) {
            String[] kv = pair.trim().split("=", 2);
            if (kv.length != 2 || kv[0].isBlank()) {
                continue;
            }
            List<String> members = Arrays.stream(kv[1].split("\\|"))
                    .map(String::trim)
                    .filter(s -> !s.isBlank())
                    .toList();
            map.put(kv[0].trim(), members);
        }
        return map;
    }

    private static int envInt(String key, int defaultValue) {
        String s = System.getenv(key);
        if (s == null || s.isBlank()) {
            return defaultValue;
        }
        try {
            return Integer.parseInt(s.trim());
        } catch (NumberFormatException e) {
            return defaultValue;
        }
    }

    private static boolean envBool(String key, boolean defaultValue) {
        String s = System.getenv(key);
        if (s == null || s.isBlank()) {
            return defaultValue;
        }
        return "1".equals(s.trim()) || "true".equalsIgnoreCase(s.trim()) || "yes".equalsIgnoreCase(s.trim());
    }
}

