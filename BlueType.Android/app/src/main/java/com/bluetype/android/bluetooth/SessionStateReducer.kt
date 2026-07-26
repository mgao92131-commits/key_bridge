package com.bluetype.android.bluetooth

import com.bluetype.android.domain.ConnectionPhase
import com.bluetype.android.domain.ConnectionState
import com.bluetype.android.domain.ConnectionTarget

internal object SessionStateReducer {
    fun reduce(event: Event): Transition {
        return when (event) {
            is Event.ConnectRequested -> {
                if (event.restoreAttempt) {
                    Transition(
                        state = ConnectionState.Reconnecting(
                            target = event.target,
                            attempt = event.attempt,
                            displayName = event.displayName,
                            computerId = event.computerId,
                            attemptId = event.attemptId,
                        ),
                        statusMessage = "Restoring connection to ${event.displayName}",
                        showRemoteSession = true,
                        sessionTarget = event.target,
                    )
                } else {
                    Transition(
                        state = ConnectionState.Connecting(
                            target = event.target,
                            displayName = event.displayName,
                            computerId = event.computerId,
                            attemptId = event.attemptId,
                            phase = event.phase,
                        ),
                        statusMessage = connectingStatus(event.target, event.displayName, event.phase),
                        showRemoteSession = true,
                        sessionTarget = event.target,
                    )
                }
            }

            is Event.TransportPhaseChanged -> Transition(
                state = ConnectionState.Connecting(
                    target = event.target,
                    displayName = event.displayName,
                    computerId = event.computerId,
                    attemptId = event.attemptId,
                    phase = event.phase,
                ),
                statusMessage = connectingStatus(event.target, event.displayName, event.phase),
                showRemoteSession = true,
                sessionTarget = event.target,
            )

            is Event.AuthPending -> Transition(
                state = ConnectionState.AwaitingApproval(
                    target = event.target,
                    timeoutSec = event.timeoutSec,
                    displayName = event.displayName,
                    computerId = event.computerId,
                    attemptId = event.attemptId,
                ),
                statusMessage = "Please confirm this device on the computer (${event.timeoutSec}s).",
                showRemoteSession = true,
                sessionTarget = event.target,
            )

            is Event.AuthSucceeded -> Transition(
                state = ConnectionState.Connected(
                    target = event.target,
                    displayName = event.displayName,
                    computerId = event.computerId,
                ),
                statusMessage = "Connected to ${event.displayName}",
                showRemoteSession = true,
                sessionTarget = event.target,
            )

            is Event.ReconnectStarted -> Transition(
                state = ConnectionState.Reconnecting(
                    target = event.target,
                    attempt = event.attempt,
                    displayName = event.displayName,
                    computerId = event.computerId,
                    attemptId = event.attemptId,
                ),
                statusMessage = "Reconnecting to ${event.displayName}… (attempt ${event.attempt})",
                showRemoteSession = true,
                sessionTarget = event.target,
            )

            is Event.AuthFailed -> Transition(
                state = ConnectionState.Error(
                    message = event.message,
                    target = event.target,
                    displayName = event.displayName,
                    computerId = event.computerId,
                ),
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

    private fun connectingStatus(
        target: ConnectionTarget,
        displayName: String,
        phase: ConnectionPhase,
    ): String {
        val transportHint = when (target) {
            is ConnectionTarget.Wifi -> "via Wi-Fi"
            is ConnectionTarget.Bluetooth -> "via Bluetooth"
        }
        return when (phase) {
            ConnectionPhase.PREPARING -> "Preparing connection to $displayName…"
            ConnectionPhase.OPENING_TRANSPORT -> "Connecting to $displayName $transportHint…"
            ConnectionPhase.AUTHENTICATING -> "Authenticating with $displayName…"
        }
    }

    sealed interface Event {
        data class ConnectRequested(
            val target: ConnectionTarget,
            val displayName: String,
            val computerId: String,
            val attemptId: Long,
            val restoreAttempt: Boolean,
            val attempt: Int = 1,
            val phase: ConnectionPhase = ConnectionPhase.OPENING_TRANSPORT,
        ) : Event

        data class TransportPhaseChanged(
            val target: ConnectionTarget,
            val displayName: String,
            val computerId: String,
            val attemptId: Long,
            val phase: ConnectionPhase,
        ) : Event

        data class AuthPending(
            val target: ConnectionTarget,
            val displayName: String,
            val computerId: String,
            val attemptId: Long,
            val timeoutSec: Int,
        ) : Event

        data class AuthSucceeded(
            val target: ConnectionTarget,
            val displayName: String,
            val computerId: String,
        ) : Event

        data class ReconnectStarted(
            val target: ConnectionTarget,
            val displayName: String,
            val computerId: String,
            val attemptId: Long,
            val attempt: Int,
        ) : Event

        data class AuthFailed(
            val message: String,
            val target: ConnectionTarget?,
            val displayName: String? = null,
            val computerId: String? = null,
        ) : Event

        data object ManualDisconnect : Event
    }

    data class Transition(
        val state: ConnectionState,
        val statusMessage: String?,
        val showRemoteSession: Boolean,
        val sessionTarget: ConnectionTarget? = null,
    )
}
