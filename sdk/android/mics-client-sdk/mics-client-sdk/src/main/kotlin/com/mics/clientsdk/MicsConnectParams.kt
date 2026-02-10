package com.mics.clientsdk

data class MicsConnectParams(
    val url: String,
    val tenantId: String,
    val token: String,
    val deviceId: String,
)

