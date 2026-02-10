package com.mics.clientsdk

import org.assertj.core.api.Assertions.assertThat
import org.junit.jupiter.api.Test

class WsUrlBuilderTest {
    @Test
    fun `buildWsUrl appends required query params`() {
        val url = buildWsUrl("ws://localhost:8080/ws", "t1", "tok", "dev1")
        assertThat(url).contains("tenantId=t1")
        assertThat(url).contains("token=tok")
        assertThat(url).contains("deviceId=dev1")
    }

    @Test
    fun `buildWsUrl preserves existing query params`() {
        val url = buildWsUrl("wss://example.com/ws?x=1", "t1", "tok", "dev1")
        assertThat(url).contains("x=1")
        assertThat(url).startsWith("wss://")
    }
}

