package com.mics.hooksdk.http;

import java.util.Objects;
import java.util.function.Function;

public final class MicsHookServerOptions {
    private final Function<String, String> tenantSecretProvider;
    private final boolean requireSign;

    public MicsHookServerOptions(Function<String, String> tenantSecretProvider, boolean requireSign) {
        this.tenantSecretProvider = Objects.requireNonNull(tenantSecretProvider, "tenantSecretProvider");
        this.requireSign = requireSign;
    }

    public Function<String, String> getTenantSecretProvider() {
        return tenantSecretProvider;
    }

    public boolean isRequireSign() {
        return requireSign;
    }
}

