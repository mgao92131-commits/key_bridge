package com.bluetype.android.bluetooth

import com.bluetype.android.data.TokenCandidate

/**
 * Classifies remote HELLO authentication outcomes for local persistence decisions.
 */
internal sealed class AuthenticationOutcome {
    data class Persistent(
        val token: String,
    ) : AuthenticationOutcome()

    data object Temporary : AuthenticationOutcome()

    data class ExistingCredential(
        val token: String,
    ) : AuthenticationOutcome()
}

internal object AuthenticationOutcomeResolver {
    fun fromAuthResult(
        token: String?,
        persistToken: Boolean,
        candidate: TokenCandidate?,
    ): AuthenticationOutcome {
        if (token.isNullOrBlank() || !persistToken) {
            return AuthenticationOutcome.Temporary
        }

        // Known reconnect echoes the presented token; treat as existing credential.
        if (candidate != null && candidate.token == token) {
            return AuthenticationOutcome.ExistingCredential(token)
        }

        return AuthenticationOutcome.Persistent(token)
    }

    fun fromHelloAck(candidate: TokenCandidate?): AuthenticationOutcome {
        val token = candidate?.token
        return if (!token.isNullOrBlank()) {
            AuthenticationOutcome.ExistingCredential(token)
        } else {
            AuthenticationOutcome.Temporary
        }
    }
}
