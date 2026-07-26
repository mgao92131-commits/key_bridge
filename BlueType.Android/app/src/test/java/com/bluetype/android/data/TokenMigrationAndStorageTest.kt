package com.bluetype.android.data

import androidx.datastore.preferences.core.PreferenceDataStoreFactory
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import com.bluetype.android.domain.ConnectionTarget
import com.bluetype.android.domain.UiRoute
import java.io.File
import java.util.Base64
import java.util.UUID
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.test.runTest
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

class TokenMigrationAndStorageTest {
    private lateinit var tempFile: File
    private lateinit var dataStore: androidx.datastore.core.DataStore<Preferences>
    private lateinit var fakeEncryptedTokenStore: FakeEncryptedTokenStore
    private lateinit var repository: PreferencesRepository

    @Before
    fun setUp() {
        tempFile = File.createTempFile("test_datastore", ".preferences_pb")
        dataStore = PreferenceDataStoreFactory.create(
            produceFile = { tempFile }
        )
        fakeEncryptedTokenStore = FakeEncryptedTokenStore()
        // PreferenceRepository in test does not need Context since we bypass context.dataStore by injecting
        repository = PreferencesRepository(
            context = null,
            dataStore = dataStore,
            secureTokenStore = fakeEncryptedTokenStore
        )
    }

    @After
    fun tearDown() {
        tempFile.delete()
    }

    @Test
    fun testTwoDifferentComputersSaveDifferentTokens() = runTest {
        val computerId1 = "computer-1"
        val computerId2 = "computer-2"

        repository.saveToken(computerId1, "Token-W")
        repository.saveToken(computerId2, "Token-M")

        assertEquals("Token-W", repository.currentToken(computerId1))
        assertEquals("Token-M", repository.currentToken(computerId2))
    }

    @Test
    fun testSameHostDifferentIdIsSeparated() = runTest {
        val target = ConnectionTarget.Wifi(host = "192.168.1.100", port = 24862)
        val id1 = "id-office"
        val id2 = "id-home"

        repository.saveToken(id1, "Token-Office")
        repository.saveToken(id2, "Token-Home")

        // fetch with target won't fallback because target has no migration keys saved
        val tokenOffice = repository.getAndMigrateToken(id1, target)
        val tokenHome = repository.getAndMigrateToken(id2, target)

        assertEquals("Token-Office", tokenOffice)
        assertEquals("Token-Home", tokenHome)
    }

    @Test
    fun testRenameComputerDoesNotLoseToken() = runTest {
        val originalDevice = StoredDevice(id = "comp-id", name = "Old Name", type = DeviceType.WIFI, host = "1.2.3.4")
        repository.saveToken(originalDevice.id, "Token-Val")

        // Rename
        val renamedDevice = originalDevice.copy(name = "New Name")
        repository.saveRecentDevice(renamedDevice)

        // Read token with original ID
        assertEquals("Token-Val", repository.currentToken("comp-id"))
    }

    @Test
    fun testRemoveRecentDeviceDeletesItsTokenAndPersistedSession() = runTest {
        val device = StoredDevice(id = "target-comp", name = "Office", type = DeviceType.WIFI, host = "192.168.1.10")
        repository.saveRecentDevice(device)
        repository.saveToken(device.id, "secret-token")

        // Persist session
        repository.savePersistedSession(
            PersistedSession(target = device, uiRoute = UiRoute.REMOTE_SESSION)
        )

        // Remove
        repository.removeRecentDevice(device)

        // Token should be cleared
        assertNull(repository.currentToken(device.id))
        // Persisted session should be cleared
        assertNull(repository.currentPersistedSession())
    }

    @Test
    fun testGetAndMigrateTokenFromOldEndpoint() = runTest {
        val target = ConnectionTarget.Wifi(host = "192.168.1.50", port = 24862)
        val computerId = "migrated-comp"

        // Write endpoint token manually using legacy format
        val legacyKey = fakeEncryptedTokenStore.getPrefsKeyForTokenKey("wifi:192.168.1.50:24862")
        dataStore.edit { prefs ->
            prefs[legacyKey] = fakeEncryptedTokenStore.encrypt("legacy-endpoint-token")
        }

        // Fetch using getAndMigrateToken
        val token = repository.getAndMigrateToken(computerId, target)
        assertEquals("legacy-endpoint-token", token)

        // Verify it was migrated to new computerId token
        assertEquals("legacy-endpoint-token", repository.currentToken(computerId))

        // Verify the old endpoint token was deleted
        val checkPrefs = dataStore.data.first()
        assertNull(checkPrefs[legacyKey])
    }

    @Test
    fun testGetAndMigrateTokenFromOldGlobalEncrypted() = runTest {
        val target = ConnectionTarget.Wifi(host = "192.168.1.60", port = 24862)
        val computerId = "migrated-global-comp"

        // Write global encrypted token manually
        val legacyGlobalKey = stringPreferencesKey("saved_token_encrypted")
        dataStore.edit { prefs ->
            prefs[legacyGlobalKey] = fakeEncryptedTokenStore.encrypt("legacy-global-encrypted")
        }

        // Fetch
        val token = repository.getAndMigrateToken(computerId, target)
        assertEquals("legacy-global-encrypted", token)

        // Verify migration
        assertEquals("legacy-global-encrypted", repository.currentToken(computerId))

        // Verify global encrypted is removed
        val checkPrefs = dataStore.data.first()
        assertNull(checkPrefs[legacyGlobalKey])
    }

    @Test
    fun testGetAndMigrateTokenFromOldPlaintext() = runTest {
        val target = ConnectionTarget.Wifi(host = "192.168.1.70", port = 24862)
        val computerId = "migrated-plaintext-comp"

        // Write legacy plaintext token manually
        val legacyPlaintextKey = stringPreferencesKey("saved_token")
        dataStore.edit { prefs ->
            prefs[legacyPlaintextKey] = "legacy-plaintext"
        }

        // Fetch
        val token = repository.getAndMigrateToken(computerId, target)
        assertEquals("legacy-plaintext", token)

        // Verify migration
        assertEquals("legacy-plaintext", repository.currentToken(computerId))

        // Verify legacy plaintext is removed
        val checkPrefs = dataStore.data.first()
        assertNull(checkPrefs[legacyPlaintextKey])
    }

    @Test
    fun testOldStoredDeviceGeneratesAndPersistsStableId() = kotlinx.coroutines.runBlocking {
        // Write raw JSON representing old stored devices (lacking 'id' and 'lastConnectedAt')
        val recentDevicesKey = stringPreferencesKey("recent_devices")
        val rawJson = """[{"name":"Old PC","type":"WIFI","host":"192.168.1.10","port":24862}]"""
        dataStore.edit { prefs ->
            prefs[recentDevicesKey] = rawJson
        }

        val expectedId = StoredDevice(
            name = "Old PC",
            type = DeviceType.WIFI,
            host = "192.168.1.10",
            port = 24862,
        ).withStableId().id

        // Fetch recent devices
        val devices = repository.recentDevices().first()
        assertEquals(1, devices.size)
        val device = devices[0]

        // Assert ID is generated deterministically from endpoint
        assertEquals(expectedId, device.id)
        assertEquals("Old PC", device.name)

        // Wait for async writeback
        kotlinx.coroutines.delay(200)

        // Fetch again, ensure ID remains the same (persisted)
        val devicesSecondFetch = repository.recentDevices().first()
        assertEquals(device.id, devicesSecondFetch[0].id)
    }

    @Test
    fun testOldPersistedSessionGeneratesAndPersistsStableId() = kotlinx.coroutines.runBlocking {
        // Write raw JSON representing old persisted session (lacking ID)
        val persistedSessionKey = stringPreferencesKey("persisted_session")
        val rawJson = """{"target":{"name":"Old PC","type":"WIFI","host":"192.168.1.10","port":24862},"uiRoute":"REMOTE_SESSION","autoRestore":true,"manuallyDisconnected":false}"""
        dataStore.edit { prefs ->
            prefs[persistedSessionKey] = rawJson
        }

        val expectedId = StoredDevice(
            name = "Old PC",
            type = DeviceType.WIFI,
            host = "192.168.1.10",
            port = 24862,
        ).withStableId().id

        // Fetch persisted session
        val session = repository.currentPersistedSession()
        assertNotNull(session)
        assertEquals(expectedId, session!!.target.id)

        // Wait for async writeback
        kotlinx.coroutines.delay(200)

        // Fetch again, ensure ID remains the same
        val sessionSecondFetch = repository.currentPersistedSession()
        assertEquals(expectedId, sessionSecondFetch?.target?.id)
    }

    @Test
    fun testLegacyRecentAndSessionMigrateToSameId() = kotlinx.coroutines.runBlocking {
        val recentDevicesKey = stringPreferencesKey("recent_devices")
        val persistedSessionKey = stringPreferencesKey("persisted_session")
        dataStore.edit { prefs ->
            prefs[recentDevicesKey] =
                """[{"name":"Old PC","type":"WIFI","host":"192.168.1.10","port":24862}]"""
            prefs[persistedSessionKey] =
                """{"target":{"name":"Old PC","type":"WIFI","host":"192.168.1.10","port":24862},"uiRoute":"REMOTE_SESSION","autoRestore":true,"manuallyDisconnected":false}"""
        }

        val devices = repository.recentDevices().first()
        val session = repository.currentPersistedSession()

        assertEquals(1, devices.size)
        assertNotNull(session)
        assertEquals(devices[0].id, session!!.target.id)
        assertTrue(devices[0].id.isNotEmpty())
    }

    // Helper classes for testing

    private class FakeEncryptedTokenStore : EncryptedTokenStore {
        override fun getPrefsKeyForTokenKey(tokenKey: String): Preferences.Key<String> {
            val encoded = Base64.getUrlEncoder().withoutPadding().encodeToString(tokenKey.toByteArray(Charsets.UTF_8))
            return stringPreferencesKey("saved_token_encrypted_$encoded")
        }

        override fun encrypt(token: String): String {
            return "encrypted:$token"
        }

        override fun decrypt(value: String): String {
            require(value.startsWith("encrypted:")) { "Not encrypted" }
            return value.removePrefix("encrypted:")
        }
    }
}
