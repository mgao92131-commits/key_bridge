package com.bluetype.android.bluetooth

import com.bluetype.android.data.TokenCandidate
import com.bluetype.android.domain.ConnectionTarget
import kotlinx.coroutines.CompletableDeferred
import java.util.concurrent.ConcurrentHashMap

internal data class ActiveConnection(
    val profile: ComputerConnectionProfile,
    val attemptId: Long,
    val helloId: String,
    val session: SessionClient,
    val tokenCandidate: TokenCandidate? = null,
    val pendingRequests: MutableMap<String, PendingRequest> = ConcurrentHashMap(),
) {
    val computerId: String get() = profile.computerId
    val displayName: String get() = profile.displayName
    val target: ConnectionTarget get() = profile.target

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
