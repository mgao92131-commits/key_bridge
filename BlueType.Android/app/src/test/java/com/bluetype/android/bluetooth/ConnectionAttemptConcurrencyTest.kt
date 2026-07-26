package com.bluetype.android.bluetooth

import com.bluetype.android.domain.ConnectionTarget
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import java.util.concurrent.atomic.AtomicInteger
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.async
import kotlinx.coroutines.delay
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class ConnectionAttemptTrackerTest {
    @Test
    fun newerAttemptSupersedesOlder() {
        val tracker = ConnectionAttemptTracker()
        val first = tracker.begin("office")
        val second = tracker.begin("home")

        assertTrue(tracker.isCurrent(second, "home", manualDisconnect = false))
        assertFalse(tracker.isCurrent(first, "office", manualDisconnect = false))
        assertFalse(tracker.isCurrent(first, "home", manualDisconnect = false))
    }

    @Test
    fun rapidSelectionKeepsOnlyLatestAttempt() {
        val tracker = ConnectionAttemptTracker()
        val a = tracker.begin("a")
        val b = tracker.begin("b")
        val c = tracker.begin("c")

        assertFalse(tracker.isCurrent(a, "a", false))
        assertFalse(tracker.isCurrent(b, "b", false))
        assertTrue(tracker.isCurrent(c, "c", false))
        assertEquals(c, tracker.currentAttemptId)
    }

    @Test
    fun manualDisconnectInvalidatesCurrentAttempt() {
        val tracker = ConnectionAttemptTracker()
        val id = tracker.begin("office")
        tracker.clearDesired()
        assertFalse(tracker.isCurrent(id, "office", manualDisconnect = true))
    }

    @Test
    fun invalidateMakesPriorAttemptStale() {
        val tracker = ConnectionAttemptTracker()
        val id = tracker.begin("office")
        tracker.invalidate()
        assertFalse(tracker.isCurrent(id, "office", manualDisconnect = false))
    }
}

class ControllableTransportConnectorTest {
    @Test
    fun connectingStatePublishedBeforeTransportOpens() = runTest {
        val openStarted = CompletableDeferred<Unit>()
        val allowOpen = CompletableDeferred<Unit>()
        val closeCount = AtomicInteger(0)

        val connector = ControllableTransportConnector(
            onOpen = {
                openStarted.complete(Unit)
                allowOpen.await()
                OpenedTransport(
                    input = ByteArrayInputStream(ByteArray(0)),
                    output = ByteArrayOutputStream(),
                    close = { closeCount.incrementAndGet() },
                )
            },
        )

        val states = mutableListOf<String>()
        val tracker = ConnectionAttemptTracker()

        // Simulate explicit connect short critical section:
        states += "connecting"
        val attemptId = tracker.begin("device-b")
        assertEquals("connecting", states.single())
        assertFalse(openStarted.isCompleted)

        val openJob = async {
            connector.open(
                target = ConnectionTarget.Wifi("1.1.1.1", 24862),
                isReconnectAttempt = false,
                lastBluetoothDisconnectAtMs = 0L,
                preferredLanNetworkProvider = { null },
            )
        }

        openStarted.await()
        assertTrue(tracker.isCurrent(attemptId, "device-b", false))

        // Supersede before open completes.
        val newer = tracker.begin("device-c")
        allowOpen.complete(Unit)
        val transport = openJob.await()

        assertFalse(tracker.isCurrent(attemptId, "device-b", false))
        assertTrue(tracker.isCurrent(newer, "device-c", false))
        // Stale success must be closed by caller.
        transport.close()
        assertEquals(1, closeCount.get())
    }

    @Test
    fun newerAttemptIgnoresOlderFailure() = runTest {
        val tracker = ConnectionAttemptTracker()
        val older = tracker.begin("a")
        tracker.begin("b")

        val olderFailed = !tracker.isCurrent(older, "a", false)
        assertTrue(olderFailed)
    }

    @Test
    fun explicitConnectSupersedesForegroundRestoreAttempt() {
        val tracker = ConnectionAttemptTracker()
        val restore = tracker.begin("office")
        val explicit = tracker.begin("home")

        assertFalse(tracker.isCurrent(restore, "office", false))
        assertTrue(tracker.isCurrent(explicit, "home", false))
    }

    @Test
    fun cancelInvalidatesAttemptAndClearsDesired() {
        val tracker = ConnectionAttemptTracker()
        val id = tracker.begin("office")
        tracker.clearDesired()
        tracker.invalidate()
        assertFalse(tracker.isCurrent(id, "office", manualDisconnect = true))
    }

    private class ControllableTransportConnector(
        private val onOpen: suspend () -> OpenedTransport,
    ) : TransportConnector {
        override suspend fun open(
            target: ConnectionTarget,
            isReconnectAttempt: Boolean,
            lastBluetoothDisconnectAtMs: Long,
            preferredLanNetworkProvider: () -> android.net.Network?,
        ): OpenedTransport = onOpen()
    }
}
