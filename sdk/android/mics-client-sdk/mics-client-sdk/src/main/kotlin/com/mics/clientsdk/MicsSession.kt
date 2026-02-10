package com.mics.clientsdk

data class MicsSession(
    val tenantId: String,
    val userId: String,
    val deviceId: String,
    val nodeId: String,
    val traceId: String,
)

