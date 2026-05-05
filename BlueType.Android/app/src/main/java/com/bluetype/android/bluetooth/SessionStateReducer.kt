package com.bluetype.android.bluetooth

import com.bluetype.android.domain.ConnectionState
import com.bluetype.android.domain.ConnectionTarget

internal object SessionStateReducer {
    fun reduce(event: Event): Transition {
        return when (event) {
            is Event.ConnectRequested -> {
                if (event.restoreAttempt) {
                    Transition(
                        state = ConnectionState.Reconnecting(event.target, event.attempt),
                        statusMessage = "Restoring connection to ${event.target.label}",
                        showRemoteSession = true,
                        sessionTarget = event.target,
                    )
                } else {
                    Transition(
                        state = ConnectionState.Connecting(event.target),
                        statusMessage = "Connecting to ${event.target.label}",
                        showRemoteSession = true,
                        sessionTarget = event.target,
                    )
                }
            }

            is Event.AuthPending -> Transition(
                state = ConnectionState.AwaitingApproval(event.target, event.timeoutSec),
                statusMessage = "Confirm this device on Windows within ${event.timeoutSec} seconds.",
                showRemoteSession = true,
                sessionTarget = event.target,
            )

            is Event.AuthSucceeded -> Transition(
                state = ConnectionState.Connected(event.target),
                statusMessage = "Connected to ${event.target.label}",
                showRemoteSession = true,
                sessionTarget = event.target,
            )

            is Event.ReconnectStarted -> Transition(
                state = ConnectionState.Reconnecting(event.target, event.attempt),
                statusMessage = "Reconnecting to ${event.target.label}...",
                showRemoteSession = true,
                sessionTarget = event.target,
            )

            is Event.AuthFailed -> Transition(
                state = ConnectionState.Error(event.message),
                statusMessage = event.message,
                showRemoteSession = event.target != null,
                sessionTarget = event.target,
            )

            Event.ManualDisconnect -> Transition(
                state = ConnectionState.Idle,
                statusMessage = null,
                showRemoteSession = false,
                sessionTarget = null,
            )
        }
    }

    sealed interface Event {
        data class ConnectRequested(
            val target: ConnectionTarget,
            val restoreAttempt: Boolean,
            val attempt: Int = 1,
        ) : Event

        data class AuthPending(val target: ConnectionTarget, val timeoutSec: Int) : Event

        data class AuthSucceeded(val target: ConnectionTarget) : Event

        data class ReconnectStarted(val target: ConnectionTarget, val attempt: Int) : Event

        data class AuthFailed(val message: String, val target: ConnectionTarget?) : Event

        data object ManualDisconnect : Event
    }

    data class Transition(
        val state: ConnectionState,
        val statusMessage: String?,
        val showRemoteSession: Boolean,
        val sessionTarget: ConnectionTarget? = null,
    )
}
