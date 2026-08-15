package com.bluetype.android.bluetooth

import com.bluetype.android.feature.connection.*
import com.bluetype.android.domain.model.ConnectionPhase
import com.bluetype.android.domain.model.ConnectionState
import com.bluetype.android.domain.model.ConnectionTarget
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
                displayName = "Office",
                computerId = "office-id",
                attemptId = 7L,
                restoreAttempt = false,
            ),
        )

        assertEquals(
            ConnectionState.Connecting(
                target = target,
                displayName = "Office",
                computerId = "office-id",
                attemptId = 7L,
                phase = ConnectionPhase.OPENING_TRANSPORT,
            ),
            transition.state,
        )
        assertTrue(transition.statusMessage!!.contains("Office"))
        assertTrue(transition.showRemoteSession)
        assertEquals(target, transition.sessionTarget)
    }

    @Test
    fun connectRequested_restore_entersReconnecting() {
        val transition = SessionStateReducer.reduce(
            SessionStateReducer.Event.ConnectRequested(
                target = target,
                displayName = "Office",
                computerId = "office-id",
                attemptId = 3L,
                restoreAttempt = true,
                attempt = 2,
            ),
        )

        assertEquals(
            ConnectionState.Reconnecting(
                target = target,
                attempt = 2,
                displayName = "Office",
                computerId = "office-id",
                attemptId = 3L,
            ),
            transition.state,
        )
        assertEquals("Restoring connection to Office", transition.statusMessage)
    }

    @Test
    fun authPending_entersAwaitingApproval() {
        val transition = SessionStateReducer.reduce(
            SessionStateReducer.Event.AuthPending(
                target = target,
                displayName = "Office",
                computerId = "office-id",
                attemptId = 9L,
                timeoutSec = 60,
            ),
        )

        assertEquals(
            ConnectionState.AwaitingApproval(
                target = target,
                timeoutSec = 60,
                displayName = "Office",
                computerId = "office-id",
                attemptId = 9L,
            ),
            transition.state,
        )
        assertTrue(transition.statusMessage!!.contains("60"))
        assertTrue(transition.showRemoteSession)
    }

    @Test
    fun authSucceeded_entersConnected() {
        val transition = SessionStateReducer.reduce(
            SessionStateReducer.Event.AuthSucceeded(
                target = target,
                displayName = "Office",
                computerId = "office-id",
            ),
        )

        assertEquals(
            ConnectionState.Connected(
                target = target,
                displayName = "Office",
                computerId = "office-id",
            ),
            transition.state,
        )
        assertEquals("Connected to Office", transition.statusMessage)
    }

    @Test
    fun reconnectStarted_entersReconnectingWithReconnectMessage() {
        val transition = SessionStateReducer.reduce(
            SessionStateReducer.Event.ReconnectStarted(
                target = target,
                displayName = "Office",
                computerId = "office-id",
                attemptId = 4L,
                attempt = 1,
            ),
        )

        assertEquals(
            ConnectionState.Reconnecting(
                target = target,
                attempt = 1,
                displayName = "Office",
                computerId = "office-id",
                attemptId = 4L,
            ),
            transition.state,
        )
        assertTrue(transition.statusMessage!!.contains("Reconnecting"))
    }

    @Test
    fun authFailed_entersError() {
        val transition = SessionStateReducer.reduce(
            SessionStateReducer.Event.AuthFailed(
                message = "No",
                target = target,
                displayName = "Office",
                computerId = "office-id",
            ),
        )

        assertEquals(
            ConnectionState.Error(
                message = "No",
                target = target,
                displayName = "Office",
                computerId = "office-id",
            ),
            transition.state,
        )
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
