package com.bluetype.android.bluetooth

import com.bluetype.android.feature.connection.*
import com.bluetype.android.data.device.DeviceType
import com.bluetype.android.data.device.StoredDevice
import com.bluetype.android.data.session.PersistedSession
import com.bluetype.android.domain.model.ConnectionTarget
import com.bluetype.android.domain.model.UiRoute
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class PersistedSessionCoordinatorTest {
    @Test
    fun hydrateSnapshot_returnsMappedTargetAndMetadata() = runTest {
        val persisted = PersistedSession(
            target = StoredDevice(id = "computer-id-1", name = "Host", type = DeviceType.WIFI, host = "192.168.0.10", port = 24862),
            uiRoute = UiRoute.REMOTE_SESSION,
            lastError = "oops",
        )
        val store = FakePersistedSessionStore(current = persisted)
        val coordinator = store.createCoordinator()

        val snapshot = coordinator.hydrateSnapshot()

        assertNotNull(snapshot)
        assertEquals("computer-id-1", snapshot?.computer?.id)
        assertEquals(ConnectionTarget.Wifi("192.168.0.10", 24862), snapshot?.target)
        assertEquals(UiRoute.REMOTE_SESSION, snapshot?.uiRoute)
        assertEquals("oops", snapshot?.lastError)
    }

    @Test
    fun resolveRestoreTarget_clearsBrokenPersistedSession() = runTest {
        val persisted = PersistedSession(
            target = StoredDevice(name = "Broken", type = DeviceType.WIFI, host = "", port = 24862),
        )
        val store = FakePersistedSessionStore(current = persisted)
        val coordinator = store.createCoordinator()

        val result = coordinator.resolveRestoreProfile(
            manualDisconnect = false,
            hasActiveConnection = false,
            hasReconnectJob = false,
        )

        assertNull(result)
        assertTrue(store.cleared)
    }

    @Test
    fun persistSession_writesExpectedSnapshot() = runTest {
        val store = FakePersistedSessionStore()
        val coordinator = store.createCoordinator()

        val device = StoredDevice(id = "id-pixel", name = "Pixel", type = DeviceType.BLUETOOTH, address = "AA:BB:CC")
        coordinator.persistSession(
            device = device,
            lastError = "failed",
            autoRestore = false,
            manuallyDisconnected = true,
        )

        val saved = store.saved.single()
        assertEquals("id-pixel", saved.target.id)
        assertEquals("Pixel", saved.target.name)
        assertEquals(DeviceType.BLUETOOTH, saved.target.type)
        assertEquals("AA:BB:CC", saved.target.address)
        assertEquals("failed", saved.lastError)
        assertFalse(saved.autoRestore)
        assertTrue(saved.manuallyDisconnected)
    }

    @Test
    fun persistLastError_updatesExistingSnapshot_withoutChangingTarget() = runTest {
        val persisted = PersistedSession(
            target = StoredDevice(name = "Host", type = DeviceType.WIFI, host = "192.168.0.20", port = 24862),
            lastError = null,
        )
        val store = FakePersistedSessionStore(current = persisted)
        val coordinator = store.createCoordinator()

        coordinator.persistLastError("write failed")

        val saved = store.saved.single()
        assertEquals("192.168.0.20", saved.target.host)
        assertEquals(DeviceType.WIFI, saved.target.type)
        assertEquals("write failed", saved.lastError)
    }

    private class FakePersistedSessionStore(
        private var current: PersistedSession? = null,
    ) {
        var cleared = false
        val saved = mutableListOf<PersistedSession>()

        fun createCoordinator(): PersistedSessionCoordinator {
            return PersistedSessionCoordinator(
                currentPersistedSession = { current },
                clearPersistedSession = {
                    cleared = true
                    current = null
                },
                savePersistedSession = { session ->
                    saved += session
                    current = session
                },
            )
        }
    }
}
