package com.bluetype.android.bluetooth

import com.bluetype.android.domain.ConnectionState
import com.bluetype.android.domain.ConnectionTarget
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class SessionStateReducerTest {
    private val target = ConnectionTarget.Wifi(host = "192.168.1.10", port = 24862)

    @Test
    fun connectRequested_explicit_entersConnecting() {
        val transition = SessionStateReducer.reduce(
            SessionStateReducer.Event.ConnectRequested(
                target = target,
                restoreAttempt = false,
            ),
        )

        assertEquals(ConnectionState.Connecting(target), transition.state)
        assertEquals("Connecting to ${target.label}", transition.statusMessage)
        assertTrue(transition.showRemoteSession)
        assertEquals(target, transition.sessionTarget)
    }

    @Test
    fun connectRequested_restore_entersReconnecting() {
        val transition = SessionStateReducer.reduce(
            SessionStateReducer.Event.ConnectRequested(
                target = target,
                restoreAttempt = true,
                attempt = 2,
            ),
        )

        assertEquals(ConnectionState.Reconnecting(target, 2), transition.state)
        assertEquals("Restoring connection to ${target.label}", transition.statusMessage)
    }

    @Test
    fun authPending_entersAwaitingApproval() {
        val transition = SessionStateReducer.reduce(SessionStateReducer.Event.AuthPending(target, timeoutSec = 60))

        assertEquals(ConnectionState.AwaitingApproval(target, 60), transition.state)
        assertEquals("Confirm this device on Windows within 60 seconds.", transition.statusMessage)
        assertTrue(transition.showRemoteSession)
    }

    @Test
    fun authSucceeded_entersConnected() {
        val transition = SessionStateReducer.reduce(SessionStateReducer.Event.AuthSucceeded(target))

        assertEquals(ConnectionState.Connected(target), transition.state)
        assertEquals("Connected to ${target.label}", transition.statusMessage)
    }

    @Test
    fun reconnectStarted_entersReconnectingWithReconnectMessage() {
        val transition = SessionStateReducer.reduce(SessionStateReducer.Event.ReconnectStarted(target, attempt = 1))

        assertEquals(ConnectionState.Reconnecting(target, 1), transition.state)
        assertEquals("Reconnecting to ${target.label}...", transition.statusMessage)
    }

    @Test
    fun authFailed_entersError() {
        val transition = SessionStateReducer.reduce(SessionStateReducer.Event.AuthFailed("No", target))

        assertEquals(ConnectionState.Error("No"), transition.state)
        assertEquals("No", transition.statusMessage)
        assertTrue(transition.showRemoteSession)
        assertEquals(target, transition.sessionTarget)
    }

    @Test
    fun manualDisconnect_entersIdle() {
        val transition = SessionStateReducer.reduce(SessionStateReducer.Event.ManualDisconnect)

        assertEquals(ConnectionState.Idle, transition.state)
        assertEquals(null, transition.statusMessage)
        assertFalse(transition.showRemoteSession)
        assertEquals(null, transition.sessionTarget)
    }
}
