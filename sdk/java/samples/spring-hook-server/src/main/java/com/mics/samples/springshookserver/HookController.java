package com.mics.samples.springshookserver;

import com.google.protobuf.ByteString;
import com.mics.contracts.hook.v1.AuthRequest;
import com.mics.contracts.hook.v1.AuthResponse;
import com.mics.contracts.hook.v1.CheckMessageRequest;
import com.mics.contracts.hook.v1.CheckMessageResponse;
import com.mics.contracts.hook.v1.GetGroupMembersRequest;
import com.mics.contracts.hook.v1.GetGroupMembersResponse;
import com.mics.contracts.hook.v1.GetOfflineMessagesRequest;
import com.mics.contracts.hook.v1.GetOfflineMessagesResponse;
import com.mics.contracts.hook.v1.HookMeta;
import com.mics.contracts.hook.v1.TenantRuntimeConfig;
import com.mics.contracts.message.v1.MessageRequest;
import com.mics.hooksdk.HookSigner;
import org.springframework.http.HttpHeaders;
import org.springframework.http.MediaType;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RestController;

import java.util.Arrays;
import java.util.Collections;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

@RestController
public class HookController {
    private final Map<String, String> tenantSecrets;
    private final Map<String, List<String>> groupMembers;
    private final boolean requireSign;
    private final String publicUrl;

    public HookController() {
        this.tenantSecrets = parseTenantSecrets(System.getenv("TENANT_SECRETS"));
        if (tenantSecrets.isEmpty()) {
            tenantSecrets.put("t1", "secret");
        }

        this.groupMembers = parseGroupMembers(System.getenv("GROUP_MEMBERS"));
        if (groupMembers.isEmpty()) {
            groupMembers.put("group-1", List.of("u1", "u2", "u3"));
        }

        this.requireSign = envBool("REQUIRE_SIGN", true);
        String url = System.getenv("PUBLIC_URL");
        this.publicUrl = (url == null || url.isBlank()) ? "http://127.0.0.1:8092" : url;
    }

    @PostMapping(value = "/auth", consumes = "application/protobuf", produces = "application/protobuf")
    public ResponseEntity<byte[]> auth(@RequestBody byte[] body) throws Exception {
        AuthRequest req = AuthRequest.parseFrom(body);
        HookMeta meta = req.hasMeta() ? req.getMeta() : HookMeta.getDefaultInstance();

        String secretOrReason = resolveSecretOrReason(meta.getTenantId());
        if (secretOrReason.startsWith("reason:")) {
            return protobuf(AuthResponse.newBuilder()
                    .setMeta(echoMeta(meta))
                    .setOk(false)
                    .setReason(secretOrReason.substring("reason:".length()))
                    .build());
        }

        AuthRequest payloadForSign = clearMetaSign(req);
        if (!HookSigner.verify(secretOrReason, meta, payloadForSign, requireSign)) {
            return protobuf(AuthResponse.newBuilder()
                    .setMeta(echoMeta(meta))
                    .setOk(false)
                    .setReason("invalid sign")
                    .build());
        }

        String token = req.getToken();
        if (token == null || !token.startsWith("valid:")) {
            return protobuf(AuthResponse.newBuilder()
                    .setMeta(echoMeta(meta))
                    .setOk(false)
                    .setReason("invalid token")
                    .build());
        }

        String userId = token.substring("valid:".length());
        boolean offlineUseHookPull = envBool("OFFLINE_USE_HOOK_PULL", false);
        TenantRuntimeConfig.Builder cfgBuilder = TenantRuntimeConfig.newBuilder()
                .setHookBaseUrl(publicUrl)
                .setHeartbeatTimeoutSeconds(30)
                .setOfflineBufferTtlSeconds(30)
                .setTenantMaxConnections(100_000)
                .setUserMaxConnections(3)
                .setTenantMaxMessageQps(10_000)
                .setTenantSecret(secretOrReason);
        if (offlineUseHookPull) {
            cfgBuilder.setOfflineUseHookPull(true);
        }
        TenantRuntimeConfig cfg = cfgBuilder.build();

        return protobuf(AuthResponse.newBuilder()
                .setMeta(echoMeta(meta))
                .setOk(true)
                .setUserId(userId)
                .setDeviceId(req.getDeviceId())
                .setConfig(cfg)
                .build());
    }

    @PostMapping(value = "/check-message", consumes = "application/protobuf", produces = "application/protobuf")
    public ResponseEntity<byte[]> checkMessage(@RequestBody byte[] body) throws Exception {
        CheckMessageRequest req = CheckMessageRequest.parseFrom(body);
        HookMeta meta = req.hasMeta() ? req.getMeta() : HookMeta.getDefaultInstance();

        String secretOrReason = resolveSecretOrReason(meta.getTenantId());
        if (secretOrReason.startsWith("reason:")) {
            return protobuf(CheckMessageResponse.newBuilder()
                    .setMeta(echoMeta(meta))
                    .setAllow(false)
                    .setReason(secretOrReason.substring("reason:".length()))
                    .build());
        }

        CheckMessageRequest payloadForSign = clearMetaSign(req);
        if (!HookSigner.verify(secretOrReason, meta, payloadForSign, requireSign)) {
            return protobuf(CheckMessageResponse.newBuilder()
                    .setMeta(echoMeta(meta))
                    .setAllow(false)
                    .setReason("invalid sign")
                    .build());
        }

        MessageRequest msg = req.getMessage();
        boolean allow = msg.getMsgBody() != null && !msg.getMsgBody().isEmpty();
        return protobuf(CheckMessageResponse.newBuilder()
                .setMeta(echoMeta(meta))
                .setAllow(allow)
                .setReason(allow ? "" : "empty msg_body")
                .build());
    }

    @PostMapping(value = "/get-group-members", consumes = "application/protobuf", produces = "application/protobuf")
    public ResponseEntity<byte[]> getGroupMembers(@RequestBody byte[] body) throws Exception {
        GetGroupMembersRequest req = GetGroupMembersRequest.parseFrom(body);
        HookMeta meta = req.hasMeta() ? req.getMeta() : HookMeta.getDefaultInstance();

        String secretOrReason = resolveSecretOrReason(meta.getTenantId());
        if (secretOrReason.startsWith("reason:")) {
            return protobuf(GetGroupMembersResponse.newBuilder().setMeta(echoMeta(meta)).build());
        }

        GetGroupMembersRequest payloadForSign = clearMetaSign(req);
        if (!HookSigner.verify(secretOrReason, meta, payloadForSign, requireSign)) {
            return protobuf(GetGroupMembersResponse.newBuilder().setMeta(echoMeta(meta)).build());
        }

        List<String> members = groupMembers.getOrDefault(req.getGroupId(), Collections.emptyList());
        return protobuf(GetGroupMembersResponse.newBuilder()
                .setMeta(echoMeta(meta))
                .addAllUserIds(members)
                .build());
    }

    @PostMapping(value = "/get-offline-messages", consumes = "application/protobuf", produces = "application/protobuf")
    public ResponseEntity<byte[]> getOfflineMessages(@RequestBody byte[] body) throws Exception {
        GetOfflineMessagesRequest req = GetOfflineMessagesRequest.parseFrom(body);
        HookMeta meta = req.hasMeta() ? req.getMeta() : HookMeta.getDefaultInstance();

        String secretOrReason = resolveSecretOrReason(meta.getTenantId());
        if (secretOrReason.startsWith("reason:")) {
            return protobuf(GetOfflineMessagesResponse.newBuilder()
                    .setMeta(echoMeta(meta))
                    .setOk(false)
                    .setReason(secretOrReason.substring("reason:".length()))
                    .build());
        }

        GetOfflineMessagesRequest payloadForSign = clearMetaSign(req);
        if (!HookSigner.verify(secretOrReason, meta, payloadForSign, requireSign)) {
            return protobuf(GetOfflineMessagesResponse.newBuilder()
                    .setMeta(echoMeta(meta))
                    .setOk(false)
                    .setReason("invalid sign")
                    .build());
        }

        // Sample server: return empty list (business should query its own storage).
        return protobuf(GetOfflineMessagesResponse.newBuilder()
                .setMeta(echoMeta(meta))
                .setOk(true)
                .setReason("")
                .setNextCursor("")
                .setHasMore(false)
                .build());
    }

    private ResponseEntity<byte[]> protobuf(com.google.protobuf.Message message) {
        HttpHeaders headers = new HttpHeaders();
        headers.setContentType(MediaType.parseMediaType("application/protobuf"));
        return ResponseEntity.ok().headers(headers).body(message.toByteArray());
    }

    private String resolveSecretOrReason(String tenantId) {
        if (tenantId == null || tenantId.isBlank()) {
            return "reason:invalid tenant";
        }
        String secret = tenantSecrets.get(tenantId);
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

    private static boolean envBool(String key, boolean defaultValue) {
        String s = System.getenv(key);
        if (s == null || s.isBlank()) {
            return defaultValue;
        }
        return "1".equals(s.trim()) || "true".equalsIgnoreCase(s.trim()) || "yes".equalsIgnoreCase(s.trim());
    }
}
