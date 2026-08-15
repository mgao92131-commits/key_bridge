package com.bluetype.android.domain.model

enum class ConnectionPhase {
    PREPARING,
    OPENING_TRANSPORT,
    AUTHENTICATING,
}

sealed interface ConnectionState {
    data object Idle : ConnectionState

    data class Connecting(
        val target: ConnectionTarget,
        val displayName: String = target.label,
        val computerId: String = "",
        val attemptId: Long = 0L,
        val phase: ConnectionPhase = ConnectionPhase.OPENING_TRANSPORT,
    ) : ConnectionState

    data class AwaitingApproval(
        val target: ConnectionTarget,
        val timeoutSec: Int,
        val displayName: String = target.label,
        val computerId: String = "",
        val attemptId: Long = 0L,
    ) : ConnectionState

    data class Connected(
        val target: ConnectionTarget,
        val displayName: String = target.label,
        val computerId: String = "",
    ) : ConnectionState

    data class Reconnecting(
        val target: ConnectionTarget,
        val attempt: Int,
        val displayName: String = target.label,
        val computerId: String = "",
        val attemptId: Long = 0L,
    ) : ConnectionState

    data class Error(
        val message: String,
        val target: ConnectionTarget? = null,
        val displayName: String? = null,
        val computerId: String? = null,
    ) : ConnectionState
}
