package com.mics.clientsdk

import com.mics.contracts.message.v1.AckStatus
import com.mics.contracts.message.v1.ClientFrame
import com.mics.contracts.message.v1.ConnectAck
import com.mics.contracts.message.v1.HeartbeatPing
import com.mics.contracts.message.v1.MessageAck
import com.mics.contracts.message.v1.MessageDelivery
import com.mics.contracts.message.v1.MessageRequest
import com.mics.contracts.message.v1.MessageType
import com.mics.contracts.message.v1.ServerError
import com.mics.contracts.message.v1.ServerFrame
import com.google.protobuf.ByteString as PbByteString
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.withTimeout
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.Response
import okhttp3.WebSocket
import okhttp3.WebSocketListener
import okio.ByteString
import okio.ByteString.Companion.toByteString
import java.io.Closeable
import java.time.Duration
import java.util.concurrent.ConcurrentHashMap
import kotlin.math.max
import kotlin.math.min
import kotlin.random.Random

class MicsClient(
    private val options: MicsClientOptions = MicsClientOptions(),
    private val okHttpClient: OkHttpClient = OkHttpClient(),
) : Closeable {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)

    private val _state = MutableStateFlow(MicsClientState.DISCONNECTED)
    val state: StateFlow<MicsClientState> = _state.asStateFlow()

    private val _connected = MutableSharedFlow<MicsSession>(extraBufferCapacity = 16)
    val connected: SharedFlow<MicsSession> = _connected.asSharedFlow()

    private val _deliveries = MutableSharedFlow<MessageDelivery>(extraBufferCapacity = 256)
    val deliveries: SharedFlow<MessageDelivery> = _deliveries.asSharedFlow()

    private val _acks = MutableSharedFlow<MessageAck>(extraBufferCapacity = 256)
    val acks: SharedFlow<MessageAck> = _acks.asSharedFlow()

    private val _errors = MutableSharedFlow<ServerError>(extraBufferCapacity = 64)
    val errors: SharedFlow<ServerError> = _errors.asSharedFlow()

    private var ws: WebSocket? = null
    private var session: MicsSession? = null
    private var connectParams: MicsConnectParams? = null
    private var connectAck: CompletableDeferred<MicsSession>? = null

    private val pendingAcks = ConcurrentHashMap<String, CompletableDeferred<MessageAck>>()
    private var nextMsgId = 0

    private var heartbeatJob: Job? = null
    private var reconnectJob: Job? = null

    suspend fun connect(params: MicsConnectParams): MicsSession {
        check(_state.value == MicsClientState.DISCONNECTED) { "client is not disconnected" }

        connectParams = params
        setState(MicsClientState.CONNECTING)
        return connectInternal(params)
    }

    suspend fun disconnect() {
        setState(MicsClientState.DISPOSING)
        stopHeartbeat()
        closeWs(code = 1000, reason = "dispose")
        session = null
        setState(MicsClientState.DISCONNECTED)
    }

    suspend fun sendSingleChat(
        toUserId: String,
        msgBody: ByteArray,
        msgId: String? = null,
    ): MessageAck {
        val id = if (!msgId.isNullOrBlank()) msgId else nextId()
        val (_, s) = waitConnectedPair()

        val body = prepareOutboundBody(msgBody)
        val msg = MessageRequest.newBuilder()
            .setTenantId(s.tenantId)
            .setUserId(s.userId)
            .setDeviceId(s.deviceId)
            .setMsgId(id)
            .setMsgType(MessageType.SINGLE_CHAT)
            .setToUserId(toUserId)
            .setGroupId("")
            .setMsgBody(PbByteString.copyFrom(body))
            .setTimestampMs(nowMs())
            .build()

        return sendWithRetry(msg)
    }

    suspend fun sendGroupChat(
        groupId: String,
        msgBody: ByteArray,
        msgId: String? = null,
    ): MessageAck {
        val id = if (!msgId.isNullOrBlank()) msgId else nextId()
        val (_, s) = waitConnectedPair()

        val body = prepareOutboundBody(msgBody)
        val msg = MessageRequest.newBuilder()
            .setTenantId(s.tenantId)
            .setUserId(s.userId)
            .setDeviceId(s.deviceId)
            .setMsgId(id)
            .setMsgType(MessageType.GROUP_CHAT)
            .setToUserId("")
            .setGroupId(groupId)
            .setMsgBody(PbByteString.copyFrom(body))
            .setTimestampMs(nowMs())
            .build()

        return sendWithRetry(msg)
    }

    override fun close() {
        scope.cancel()
        closeWs(code = 1000, reason = "dispose")
    }

    private suspend fun sendWithRetry(msg: MessageRequest): MessageAck {
        val ackDeferred = CompletableDeferred<MessageAck>()
        pendingAcks[msg.msgId] = ackDeferred

        val maxAttempts = max(1, options.maxSendAttempts)
        var attempts = 0
        var lastAck: MessageAck? = null

        try {
            while (attempts < maxAttempts) {
                if (ackDeferred.isCompleted) return ackDeferred.await()

                val pair = waitConnectedPair()
                val curWs = pair.first
                val updated = msg.toBuilder().setTimestampMs(nowMs()).build()
                val frame = ClientFrame.newBuilder().setMessage(updated).build().toByteArray()

                val sentOk = try {
                    curWs.send(frame.toByteString())
                } catch (_: Exception) {
                    false
                }

                if (!sentOk) {
                    tryStartReconnect()
                    attempts++
                    continue
                }

                val ack = try {
                    withTimeout(options.ackTimeout.toMillis()) { ackDeferred.await() }
                } catch (_: Exception) {
                    null
                }

                if (ack != null) return ack
                attempts++
            }

            lastAck = MessageAck.newBuilder()
                .setMsgId(msg.msgId)
                .setStatus(AckStatus.FAILED)
                .setTimestampMs(nowMs())
                .setReason("ack timeout")
                .build()
            return lastAck
        } finally {
            pendingAcks.remove(msg.msgId)
            if (lastAck != null && !ackDeferred.isCompleted) {
                ackDeferred.complete(lastAck)
            }
        }
    }

    private suspend fun connectInternal(params: MicsConnectParams): MicsSession {
        closeWs(code = 1000, reason = "reconnect")
        session = null

        val connectAck = CompletableDeferred<MicsSession>()
        this.connectAck = connectAck

        val url = buildWsUrl(params.url, params.tenantId, params.token, params.deviceId)
        val req = Request.Builder().url(url).build()

        val listener = object : WebSocketListener() {
            override fun onOpen(webSocket: WebSocket, response: Response) {
                // wait for ConnectAck
            }

            override fun onMessage(webSocket: WebSocket, bytes: ByteString) {
                handleMessage(bytes.toByteArray())
            }

            override fun onClosing(webSocket: WebSocket, code: Int, reason: String) {
                webSocket.close(code, reason)
            }

            override fun onClosed(webSocket: WebSocket, code: Int, reason: String) {
                handleClosed(code, reason)
            }

            override fun onFailure(webSocket: WebSocket, t: Throwable, response: Response?) {
                handleClosed(code = null, reason = t.message)
            }
        }

        val w = okHttpClient.newWebSocket(req, listener)
        ws = w

        val s = withTimeout(options.connectTimeout.toMillis()) { connectAck.await() }
        session = s
        setState(MicsClientState.CONNECTED)
        startHeartbeat()
        _connected.tryEmit(s)
        return s
    }

    private fun handleMessage(bytes: ByteArray) {
        val frame = try {
            ServerFrame.parseFrom(bytes)
        } catch (_: Exception) {
            return
        }

        when (frame.payloadCase) {
            ServerFrame.PayloadCase.CONNECT_ACK -> onConnectAck(frame.connectAck)
            ServerFrame.PayloadCase.ACK -> onAck(frame.ack)
            ServerFrame.PayloadCase.DELIVERY -> onDelivery(frame.delivery)
            ServerFrame.PayloadCase.ERROR -> _errors.tryEmit(frame.error)
            ServerFrame.PayloadCase.HEARTBEAT_PONG,
            ServerFrame.PayloadCase.PAYLOAD_NOT_SET -> Unit
            null -> Unit
        }
    }

    private fun onConnectAck(ack: ConnectAck) {
        val deferred = connectAck ?: return
        if (deferred.isCompleted) return

        if (ack.code != 1000) {
            deferred.completeExceptionally(IllegalStateException("connect rejected: ${ack.code}"))
            return
        }

        deferred.complete(
            MicsSession(
                tenantId = ack.tenantId,
                userId = ack.userId,
                deviceId = ack.deviceId,
                nodeId = ack.nodeId,
                traceId = ack.traceId,
            ),
        )
    }

    private fun onAck(ack: MessageAck) {
        pendingAcks[ack.msgId]?.complete(ack)
        _acks.tryEmit(ack)
    }

    private fun onDelivery(delivery: MessageDelivery) {
        val crypto = options.messageCrypto
        if (crypto == null || !delivery.hasMessage()) {
            _deliveries.tryEmit(delivery)
            return
        }

        val msg = delivery.message
        if (msg.msgBody.isEmpty) {
            _deliveries.tryEmit(delivery)
            return
        }

        try {
            val dec = crypto.decrypt(msg.msgBody.toByteArray())
            val patched = delivery.toBuilder()
                .setMessage(msg.toBuilder().setMsgBody(PbByteString.copyFrom(dec)).build())
                .build()
            _deliveries.tryEmit(patched)
        } catch (_: Exception) {
            _deliveries.tryEmit(delivery)
        }
    }

    private fun handleClosed(code: Int?, reason: String?) {
        stopHeartbeat()
        ws = null
        session = null
        connectAck?.cancel(CancellationException("socket closed"))
        connectAck = null

        if (_state.value == MicsClientState.DISPOSING) return
        tryStartReconnect()
    }

    private fun tryStartReconnect() {
        if (!options.autoReconnect) {
            setState(MicsClientState.DISCONNECTED)
            return
        }
        if (reconnectJob != null) return
        val params = connectParams ?: run {
            setState(MicsClientState.DISCONNECTED)
            return
        }

        setState(MicsClientState.RECONNECTING)

        reconnectJob = scope.launch {
            var backoff = options.reconnectMinDelay
            val maxDelay = options.reconnectMaxDelay

            while (true) {
                try {
                    connectAck = null
                    connectInternal(params)
                    return@launch
                } catch (_: Exception) {
                    val d = withJitter(backoff)
                    if (!d.isZero && !d.isNegative) {
                        delay(d.toMillis())
                    }
                    val nextMs = if (backoff.isZero) 50 else backoff.toMillis().toInt() * 2
                    backoff = Duration.ofMillis(min(maxDelay.toMillis(), nextMs.toLong()))
                }
            }
        }.also { job ->
            job.invokeOnCompletion { reconnectJob = null }
        }
    }

    private suspend fun waitConnectedPair(): Pair<WebSocket, MicsSession> {
        while (true) {
            val w = ws
            val s = session
            if (_state.value == MicsClientState.CONNECTED && w != null && s != null) {
                return w to s
            }

            if (_state.value == MicsClientState.DISCONNECTED || _state.value == MicsClientState.DISPOSING) {
                throw IllegalStateException("client is not connected")
            }

            if (!options.autoReconnect) {
                throw IllegalStateException("client is not connected")
            }

            delay(25)
        }
    }

    private fun startHeartbeat() {
        stopHeartbeat()
        val intervalMs = options.heartbeatInterval.toMillis()
        if (intervalMs <= 0) return

        heartbeatJob = scope.launch {
            while (true) {
                delay(intervalMs)
                val w = ws ?: continue
                val ping = HeartbeatPing.newBuilder().setTimestampMs(nowMs()).build()
                val frame = ClientFrame.newBuilder().setHeartbeatPing(ping).build().toByteArray()
                try {
                    w.send(frame.toByteString())
                } catch (_: Exception) {
                    // ignore
                }
            }
        }
    }

    private fun stopHeartbeat() {
        heartbeatJob?.cancel()
        heartbeatJob = null
    }

    private fun closeWs(code: Int, reason: String) {
        val w = ws
        ws = null
        if (w != null) {
            try {
                w.close(code, reason)
            } catch (_: Exception) {
                // ignore
            }
        }
    }

    private fun setState(s: MicsClientState) {
        if (_state.value == s) return
        _state.value = s
    }

    private fun nextId(): String {
        nextMsgId++
        return nextMsgId.toString()
    }

    private fun prepareOutboundBody(plaintext: ByteArray): ByteArray {
        if (plaintext.isEmpty()) return ByteArray(0)
        val crypto = options.messageCrypto ?: return plaintext
        return crypto.encrypt(plaintext)
    }

    private fun withJitter(d: Duration): Duration {
        val ms = d.toMillis()
        if (ms <= 0) return d
        val jitter = max(1, (ms / 4).toInt())
        return Duration.ofMillis(ms + Random.nextInt(jitter).toLong())
    }

    private fun nowMs(): Long = System.currentTimeMillis()
}
