package com.mics.clientsdk

import okhttp3.HttpUrl.Companion.toHttpUrl

fun buildWsUrl(baseUrl: String, tenantId: String, token: String, deviceId: String): String {
    val normalized = when {
        baseUrl.startsWith("ws://") -> "http://" + baseUrl.removePrefix("ws://")
        baseUrl.startsWith("wss://") -> "https://" + baseUrl.removePrefix("wss://")
        else -> baseUrl
    }

    val u = normalized.toHttpUrl().newBuilder()
        .setQueryParameter("tenantId", tenantId)
        .setQueryParameter("token", token)
        .setQueryParameter("deviceId", deviceId)
        .build()

    val s = u.toString()
    return when {
        baseUrl.startsWith("ws://") -> "ws://" + s.removePrefix("http://")
        baseUrl.startsWith("wss://") -> "wss://" + s.removePrefix("https://")
        else -> s
    }
}
