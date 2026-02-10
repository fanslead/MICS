package com.mics.clientsdk

import java.time.Duration

data class MicsClientOptions(
    val connectTimeout: Duration = Duration.ofSeconds(5),
    val ackTimeout: Duration = Duration.ofSeconds(3),
    val maxSendAttempts: Int = 3,
    val heartbeatInterval: Duration = Duration.ofSeconds(10),
    val autoReconnect: Boolean = true,
    val reconnectMinDelay: Duration = Duration.ofMillis(200),
    val reconnectMaxDelay: Duration = Duration.ofSeconds(5),
    val messageCrypto: MessageCrypto? = null,
)

