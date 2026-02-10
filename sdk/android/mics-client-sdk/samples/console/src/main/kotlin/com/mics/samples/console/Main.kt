package com.mics.samples.console

import com.mics.clientsdk.MicsClient
import com.mics.clientsdk.MicsClientOptions
import com.mics.clientsdk.MicsConnectParams
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.cancel
import kotlinx.coroutines.flow.collect
import kotlinx.coroutines.launch
import kotlinx.coroutines.runBlocking
import java.nio.charset.StandardCharsets

fun main(args: Array<String>) = runBlocking {
    val map = parseArgs(args.toList())
    val url = map["url"] ?: "ws://localhost:8080/ws"
    val tenantId = map["tenantId"] ?: "t1"
    val token = map["token"] ?: "valid:u1"
    val deviceId = map["deviceId"] ?: "dev1"
    val toUserId = map["toUserId"] ?: "u2"
    val message = map["message"] ?: "hello from kotlin"

    val client = MicsClient(MicsClientOptions())
    val job = launchLoggers(client)

    val session = client.connect(MicsConnectParams(url, tenantId, token, deviceId))
    println("session user=${session.userId} node=${session.nodeId} traceId=${session.traceId}")

    val ack = client.sendSingleChat(toUserId, message.toByteArray(StandardCharsets.UTF_8))
    println("send ack status=${ack.status} reason=${ack.reason}")

    client.disconnect()
    job.cancel()
}

private fun parseArgs(args: List<String>): Map<String, String> {
    val out = LinkedHashMap<String, String>()
    var i = 0
    while (i < args.size) {
        val a = args[i]
        if (a.startsWith("--") && i + 1 < args.size) {
            out[a.substring(2)] = args[i + 1]
            i += 2
        } else {
            i++
        }
    }
    return out
}

private fun launchLoggers(client: MicsClient): Job = CoroutineScope(Dispatchers.Default).launch {
    launch { client.state.collect { println("state=$it") } }
    launch { client.connected.collect { println("connected user=${it.userId} node=${it.nodeId}") } }
    launch { client.deliveries.collect { println("delivery msgId=${it.message.msgId}") } }
    launch { client.acks.collect { println("ack msgId=${it.msgId} status=${it.status}") } }
    launch { client.errors.collect { println("server_error code=${it.code} message=${it.message}") } }
}
