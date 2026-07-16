package com.bluetype.android.data

import com.bluetype.android.domain.ConnectionTarget
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotEquals
import org.junit.Test

class TokenStorageKeyTest {
    @Test
    fun tokenStorageKey_normalizesBluetoothAddress() {
        val upper = ConnectionTarget.Bluetooth(name = "Desktop", address = "AA:BB:CC:DD:EE:FF")
        val lower = ConnectionTarget.Bluetooth(name = "Renamed", address = "aa:bb:cc:dd:ee:ff")

        assertEquals("bt:aa:bb:cc:dd:ee:ff", upper.tokenStorageKey())
        assertEquals(upper.tokenStorageKey(), lower.tokenStorageKey())
    }

    @Test
    fun tokenStorageKey_normalizesWifiHostAndIncludesPort() {
        val host = ConnectionTarget.Wifi(host = " DESKTOP.Local ", port = 24862)
        val sameHostDifferentCase = ConnectionTarget.Wifi(host = "desktop.local", port = 24862)
        val differentPort = ConnectionTarget.Wifi(host = "desktop.local", port = 24863)

        assertEquals("wifi:desktop.local:24862", host.tokenStorageKey())
        assertEquals(host.tokenStorageKey(), sameHostDifferentCase.tokenStorageKey())
        assertNotEquals(host.tokenStorageKey(), differentPort.tokenStorageKey())
    }
}
