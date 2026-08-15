package com.bluetype.android.transport

import android.util.Log
import com.bluetype.android.protocol.*
import java.io.InputStream
import java.io.OutputStream
import java.util.UUID
import java.util.concurrent.atomic.AtomicBoolean
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.channels.BufferOverflow
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import kotlinx.serialization.json.buildJsonObject

internal class SessionClient(
    private val logTag: String,
    parentScope: CoroutineScope,
    private val input: InputStream,
    private val output: OutputStream,
    initialToken: String?,
    private val closeTransport: () -> Unit,
    private val onEnvelope: suspend (Envelope) -> Unit,
    private val onDisconnected: (String) -> Unit,
    writerCapacity: Int = DEFAULT_WRITER_CAPACITY,
) {
    private val writer = Channel<Envelope>(
        capacity = writerCapacity,
        onBufferOverflow = BufferOverflow.SUSPEND,
    )
    private val sessionScope = CoroutineScope(parentScope.coroutineContext + SupervisorJob())
    private val disconnectNotified = AtomicBoolean(false)

    @Volatile
    var token: String? = initialToken
        private set

    @Volatile
    private var lastInboundAt: Long = System.currentTimeMillis()

    fun start() {
        sessionScope.launch { writerLoop() }
        sessionScope.launch { readerLoop() }
        sessionScope.launch { heartbeatLoop() }
    }

    suspend fun send(envelope: Envelope) {
        writer.send(envelope)
    }

    fun trySend(envelope: Envelope): Boolean {
        val sent = writer.trySend(envelope).isSuccess
        if (!sent) {
            runCatching {
                Log.w(logTag, "writer queue full, dropping type=${envelope.type} id=${envelope.id}")
            }
        }
        return sent
    }

    fun updateToken(value: String?) {
        token = value
    }

    fun lastInboundAtMillis(): Long = lastInboundAt

    fun close() {
        writer.close()
        sessionScope.cancel()
        runCatching(closeTransport)
    }

    private suspend fun readerLoop() {
        try {
            while (true) {
                val envelope = withContext(Dispatchers.IO) { FrameCodec.read(input) }
                lastInboundAt = System.currentTimeMillis()
                Log.d(logTag, "readerLoop received type=${envelope.type} id=${envelope.id}")
                onEnvelope(envelope)
            }
        } catch (_: CancellationException) {
        } catch (error: Exception) {
            Log.e(logTag, "readerLoop failed", error)
            notifyUnexpectedDisconnect("Connection lost: ${error.message ?: "stream closed"}")
        }
    }

    private suspend fun writerLoop() {
        try {
            for (envelope in writer) {
                Log.d(logTag, "writerLoop sending type=${envelope.type} id=${envelope.id}")
                withContext(Dispatchers.IO) { FrameCodec.write(output, envelope) }
            }
        } catch (_: CancellationException) {
        } catch (error: Exception) {
            Log.e(logTag, "writerLoop failed", error)
            notifyUnexpectedDisconnect("Connection lost: ${error.message ?: "write failed"}")
        }
    }

    private suspend fun heartbeatLoop() {
        try {
            while (sessionScope.coroutineContext.isActive) {
                delay(PING_INTERVAL_MS)

                val silenceMs = System.currentTimeMillis() - lastInboundAt
                if (silenceMs >= PONG_TIMEOUT_MS) {
                    throw IllegalStateException("Heartbeat timeout.")
                }

                trySend(
                    Envelope(
                        id = UUID.randomUUID().toString(),
                        type = MsgType.PING.wireName,
                        token = token,
                        payload = buildJsonObject { },
                    ),
                )
            }
        } catch (_: CancellationException) {
        } catch (error: Exception) {
            notifyUnexpectedDisconnect(error.message ?: "Heartbeat timeout.")
        }
    }

    private fun notifyUnexpectedDisconnect(message: String) {
        if (!disconnectNotified.compareAndSet(false, true)) {
            return
        }

        runCatching(closeTransport)
        onDisconnected(message)
    }

    private companion object {
        private const val DEFAULT_WRITER_CAPACITY = 64
        private const val PING_INTERVAL_MS = 15_000L
        private const val PONG_TIMEOUT_MS = 90_000L
    }
}
