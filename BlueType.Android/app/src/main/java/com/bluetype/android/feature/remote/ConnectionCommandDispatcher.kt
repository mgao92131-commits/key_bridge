package com.bluetype.android.feature.remote

import com.bluetype.android.domain.CommandFeedback
import com.bluetype.android.domain.CommandFeedbackState
import com.bluetype.android.domain.ConnectionState
import com.bluetype.android.feature.connection.ActiveConnection
import com.bluetype.android.feature.connection.PendingRequest
import com.bluetype.android.protocol.*
import kotlinx.coroutines.CompletableDeferred
import java.util.UUID
import kotlinx.coroutines.channels.ClosedSendChannelException
import kotlinx.coroutines.withTimeoutOrNull
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonArray
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put

internal class ConnectionCommandDispatcher(
    private val stateProvider: () -> ConnectionState,
    private val connectionProvider: () -> ActiveConnection?,
    private val onError: (String) -> Unit,
    private val onQueuedFeedback: (CommandFeedback) -> Unit,
) {
    suspend fun send(
        action: String,
        payload: kotlinx.serialization.json.JsonObject,
        type: MsgType,
        trackFeedback: Boolean = true,
    ) {
        val connection = connectionProvider()
        if (connection == null || stateProvider() !is ConnectionState.Connected) {
            onError("No active connection for $action")
            return
        }

        val requestId = UUID.randomUUID().toString()
        connection.pendingRequests[requestId] = PendingRequest(action = action, trackFeedback = trackFeedback)

        try {
            connection.session.send(
                Envelope(
                    id = requestId,
                    type = type.wireName,
                    token = connection.token,
                    payload = payload,
                ),
            )
            if (trackFeedback) {
                onQueuedFeedback(
                    CommandFeedback(
                        requestId = requestId,
                        action = action,
                        state = CommandFeedbackState.QUEUED,
                        message = "$action sent. Waiting for server reply.",
                    ),
                )
            }
        } catch (_: ClosedSendChannelException) {
            connection.pendingRequests.remove(requestId)
            onError("Connection closed before sending $action.")
        }
    }

    suspend fun send(command: EncodedRemoteCommand) {
        send(
            action = command.action,
            payload = command.payload,
            type = command.type,
            trackFeedback = command.trackFeedback,
        )
    }

    suspend fun sendAwaitAck(
        command: EncodedRemoteCommand,
        timeoutMs: Long = ACK_TIMEOUT_MS,
    ): Boolean {
        val connection = connectionProvider()
        if (connection == null || stateProvider() !is ConnectionState.Connected) {
            onError("No active connection for ${command.action}")
            return false
        }

        val requestId = UUID.randomUUID().toString()
        val completion = CompletableDeferred<Boolean>()
        connection.pendingRequests[requestId] = PendingRequest(
            action = command.action,
            trackFeedback = command.trackFeedback,
            ackCompletion = completion,
        )

        try {
            connection.session.send(
                Envelope(
                    id = requestId,
                    type = command.type.wireName,
                    token = connection.token,
                    payload = command.payload,
                ),
            )
            if (command.trackFeedback) {
                onQueuedFeedback(
                    CommandFeedback(
                        requestId = requestId,
                        action = command.action,
                        state = CommandFeedbackState.QUEUED,
                        message = "${command.action} sent. Waiting for server reply.",
                    ),
                )
            }
        } catch (_: ClosedSendChannelException) {
            connection.pendingRequests.remove(requestId)?.ackCompletion?.complete(false)
            onError("Connection closed before sending ${command.action}.")
            return false
        }

        val acked = withTimeoutOrNull(timeoutMs) {
            completion.await()
        }
        if (acked != null) {
            return acked
        }

        val timedOutRequest = connection.pendingRequests.remove(requestId)
        if (timedOutRequest?.ackCompletion === completion) {
            completion.complete(false)
            if (timedOutRequest.trackFeedback) {
                onQueuedFeedback(
                    CommandFeedback(
                        requestId = requestId,
                        action = timedOutRequest.action,
                        state = CommandFeedbackState.FAILED,
                        message = "${timedOutRequest.action} timed out waiting for server reply.",
                    ),
                )
            }
        }
        return false
    }

    suspend fun sendKeyTap(key: String, trackFeedback: Boolean = false) {
        send(
            action = "KEY_TAP",
            payload = buildJsonObject { put("key", key) },
            type = MsgType.KEY_TAP,
            trackFeedback = trackFeedback,
        )
    }

    suspend fun sendKeyDown(key: String, trackFeedback: Boolean = false) {
        send(
            action = "KEY_DOWN",
            payload = buildJsonObject { put("key", key) },
            type = MsgType.KEY_DOWN,
            trackFeedback = trackFeedback,
        )
    }

    suspend fun sendKeyUp(key: String, trackFeedback: Boolean = false) {
        send(
            action = "KEY_UP",
            payload = buildJsonObject { put("key", key) },
            type = MsgType.KEY_UP,
            trackFeedback = trackFeedback,
        )
    }

    suspend fun sendCombo(keys: List<String>, trackFeedback: Boolean = true) {
        send(
            action = "COMBO",
            payload = buildJsonObject {
                put(
                    "keys",
                    buildJsonArray {
                        keys.forEach { add(JsonPrimitive(it)) }
                    },
                )
            },
            type = MsgType.COMBO,
            trackFeedback = trackFeedback,
        )
    }

    private companion object {
        private const val ACK_TIMEOUT_MS = 5_000L
    }
}
