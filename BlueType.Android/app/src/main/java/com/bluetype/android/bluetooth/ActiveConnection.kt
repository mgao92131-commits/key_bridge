package com.bluetype.android.bluetooth

import com.bluetype.android.domain.ConnectionTarget
import kotlinx.coroutines.CompletableDeferred
import java.util.concurrent.ConcurrentHashMap

internal data class ActiveConnection(
    val target: ConnectionTarget,
    val helloId: String,
    val session: SessionClient,
    val pendingRequests: MutableMap<String, PendingRequest> = ConcurrentHashMap(),
) {
    var token: String?
        get() = session.token
        set(value) {
            session.updateToken(value)
        }
}

internal data class PendingRequest(
    val action: String,
    val trackFeedback: Boolean = true,
    val ackCompletion: CompletableDeferred<Boolean>? = null,
)
