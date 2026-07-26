package com.bluetype.android.bluetooth

import com.bluetype.android.data.DeviceType
import com.bluetype.android.data.StoredDevice
import com.bluetype.android.data.TokenCandidate
import com.bluetype.android.data.TokenSource
import com.bluetype.android.domain.ConnectionTarget
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class ComputerProfileFactoryTest {
    @Test
    fun createNewWifiProfile_alwaysGeneratesProvidedId() {
        val profile = ComputerProfileFactory.createNewWifiProfile(
            displayName = "Office",
            host = "192.168.1.100",
            idGenerator = { "new-wifi-id" },
        )

        assertEquals("new-wifi-id", profile.computerId)
        assertEquals("Office", profile.displayName)
        assertEquals(ConnectionTarget.Wifi("192.168.1.100", 24862), profile.target)
        assertEquals(ProfilePersistenceIntent.NEW_COMPUTER, profile.persistenceIntent)
    }

    @Test
    fun createNewWifiProfile_sameHostStillGetsDistinctIds() {
        var counter = 0
        val first = ComputerProfileFactory.createNewWifiProfile(
            displayName = "Office Windows",
            host = "192.168.1.100",
            idGenerator = { "office-id" },
        )
        val second = ComputerProfileFactory.createNewWifiProfile(
            displayName = "Home Mac",
            host = "192.168.1.100",
            idGenerator = {
                counter += 1
                "home-id-$counter"
            },
        )

        assertEquals("office-id", first.computerId)
        assertEquals("home-id-1", second.computerId)
        assertNotEquals(first.computerId, second.computerId)
        assertEquals(first.target, second.target)
    }

    @Test
    fun createNewWifiProfile_sameNameStillGetsDistinctIds() {
        val first = ComputerProfileFactory.createNewWifiProfile(
            displayName = "Desktop",
            host = "10.0.0.1",
            idGenerator = { "id-a" },
        )
        val second = ComputerProfileFactory.createNewWifiProfile(
            displayName = "Desktop",
            host = "10.0.0.2",
            idGenerator = { "id-b" },
        )

        assertEquals("Desktop", first.displayName)
        assertEquals("Desktop", second.displayName)
        assertNotEquals(first.computerId, second.computerId)
    }

    @Test
    fun createFromSavedDevice_reusesExistingId() {
        val device = StoredDevice(
            id = "office-id",
            name = "Office Windows",
            type = DeviceType.WIFI,
            host = "192.168.1.100",
            port = 24862,
        )

        val profile = ComputerProfileFactory.createFromSavedDevice(device)

        assertEquals("office-id", profile.computerId)
        assertEquals("Office Windows", profile.displayName)
        assertEquals(ProfilePersistenceIntent.EXISTING_SAVED_COMPUTER, profile.persistenceIntent)
    }

    @Test
    fun createFromSavedDevice_renameDoesNotChangeId() {
        val renamed = StoredDevice(
            id = "stable-id",
            name = "New Name",
            type = DeviceType.WIFI,
            host = "192.168.1.100",
        )

        val profile = ComputerProfileFactory.createFromSavedDevice(renamed)
        assertEquals("stable-id", profile.computerId)
        assertEquals("New Name", profile.displayName)
    }
}

class AuthenticationOutcomeResolverTest {
    @Test
    fun authResult_allowOnceIsTemporary() {
        val outcome = AuthenticationOutcomeResolver.fromAuthResult(
            token = null,
            persistToken = false,
            candidate = null,
        )
        assertEquals(AuthenticationOutcome.Temporary, outcome)
    }

    @Test
    fun authResult_newPersistentToken() {
        val outcome = AuthenticationOutcomeResolver.fromAuthResult(
            token = "Token-M",
            persistToken = true,
            candidate = TokenCandidate("Token-W", TokenSource.LegacyGlobalEncrypted),
        )
        assertTrue(outcome is AuthenticationOutcome.Persistent)
        assertEquals("Token-M", (outcome as AuthenticationOutcome.Persistent).token)
    }

    @Test
    fun authResult_echoedCandidateIsExistingCredential() {
        val candidate = TokenCandidate("Token-W", TokenSource.ComputerProfile("office"))
        val outcome = AuthenticationOutcomeResolver.fromAuthResult(
            token = "Token-W",
            persistToken = true,
            candidate = candidate,
        )
        assertTrue(outcome is AuthenticationOutcome.ExistingCredential)
    }

    @Test
    fun helloAck_withCandidateIsExistingCredential() {
        val candidate = TokenCandidate("Token-W", TokenSource.LegacyEndpoint("wifi:1.2.3.4:24862"))
        val outcome = AuthenticationOutcomeResolver.fromHelloAck(candidate)
        assertEquals(AuthenticationOutcome.ExistingCredential("Token-W"), outcome)
    }
}
