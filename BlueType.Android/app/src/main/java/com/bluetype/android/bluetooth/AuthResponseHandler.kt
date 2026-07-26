package com.bluetype.android.bluetooth

import com.bluetype.android.domain.ConnectionTarget

internal object AuthResponseHandler {
    fun pendingApproval(target: ConnectionTarget, envelope: Envelope): SessionStateReducer.Transition {
        val payload = ProtocolJson.decodeFromJsonElement(AuthPendingPayload.serializer(), envelope.payload)
        return SessionStateReducer.reduce(
            SessionStateReducer.Event.AuthPending(target = target, timeoutSec = payload.timeoutSec),
        )
    }

    fun authResult(envelope: Envelope): AuthSuccess {
        val payload = ProtocolJson.decodeFromJsonElement(AuthResultPayload.serializer(), envelope.payload)
        val token = payload.token?.takeIf { it.isNotBlank() }
        return AuthSuccess(
            token = token,
            persistToken = token != null && (payload.persistToken || payload.trusted),
        )
    }

    fun helloError(payload: ErrorPayload): AuthErrorAction {
        val notAuthorized = payload.code == ErrorCodes.NotAuthorized
        val busy = payload.code == ErrorCodes.Busy
        val stopRestore = notAuthorized || busy
        return AuthErrorAction(
            message = payload.message.ifBlank { defaultErrorMessage(payload.code) },
            // Clear only the attempted candidate source; never wipe unrelated credentials.
            clearRejectedCandidate = notAuthorized,
            clearPersistedSession = stopRestore,
            clearDesiredTarget = stopRestore,
        )
    }

    fun commandAuthorizationError(payload: ErrorPayload): AuthErrorAction? {
        if (payload.code != ErrorCodes.NotAuthorized) {
            return null
        }

        return AuthErrorAction(
            message = "Authorization expired. Reconnect to approve this device again.",
            clearRejectedCandidate = true,
            clearPersistedSession = true,
            clearDesiredTarget = true,
        )
    }

    fun defaultErrorMessage(code: String): String {
        return when (code) {
            ErrorCodes.Busy -> "Another device is already controlling this PC."
            ErrorCodes.AuthTimeout -> "Authorization timed out on the remote computer."
            ErrorCodes.AuthUiUnavailable -> "The remote computer cannot show the authorization prompt right now."
            ErrorCodes.InputBlocked -> "Remote computer rejected the input command."
            ErrorCodes.ClipboardFailed -> "Clipboard synchronization failed on the remote computer."
            ErrorCodes.InvalidPayload -> "The request payload was rejected by the server."
            ErrorCodes.ServerError -> "The remote agent reported an internal error."
            else -> code
        }
    }

    data class AuthSuccess(
        val token: String?,
        val persistToken: Boolean,
    )

    data class AuthErrorAction(
        val message: String,
        val clearRejectedCandidate: Boolean,
        val clearPersistedSession: Boolean,
        val clearDesiredTarget: Boolean,
    )
}

internal object ErrorCodes {
    const val Busy = "BUSY"
    const val NotAuthorized = "NOT_AUTHORIZED"
    const val AuthTimeout = "AUTH_TIMEOUT"
    const val AuthUiUnavailable = "AUTH_UI_UNAVAILABLE"
    const val InvalidPayload = "INVALID_PAYLOAD"
    const val ServerError = "SERVER_ERROR"
    const val InputBlocked = "INPUT_BLOCKED"
    const val ClipboardFailed = "CLIPBOARD_FAILED"
}
