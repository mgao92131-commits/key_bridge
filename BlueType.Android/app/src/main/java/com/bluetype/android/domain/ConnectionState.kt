package com.bluetype.android.domain

sealed interface ConnectionState {
    data object Idle : ConnectionState
    data class Connecting(val target: ConnectionTarget) : ConnectionState
    data class AwaitingApproval(val target: ConnectionTarget, val timeoutSec: Int) : ConnectionState
    data class Connected(val target: ConnectionTarget) : ConnectionState
    data class Reconnecting(val target: ConnectionTarget, val attempt: Int) : ConnectionState
    data class Error(val message: String) : ConnectionState
}
