package com.bluetype.android.data

import androidx.datastore.preferences.core.PreferenceDataStoreFactory
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import com.bluetype.android.domain.ConnectionTarget
import com.bluetype.android.domain.UiRoute
import java.io.File
import java.util.Base64
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.test.runTest
import org.junit.After
import org.junit.Assert.assertEquals
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
            produceFile = { tempFile },
        )
        fakeEncryptedTokenStore = FakeEncryptedTokenStore()
        repository = PreferencesRepository(
            context = null,
            dataStore = dataStore,
            secureTokenStore = fakeEncryptedTokenStore,
        )
    }

    @After
    fun tearDown() {
        tempFile.delete()
    }

    @Test
    fun testTwoDifferentComputersSaveDifferentTokens() = runTest {
        repository.saveToken("computer-1", "Token-W")
        repository.saveToken("computer-2", "Token-M")

        assertEquals("Token-W", repository.currentToken("computer-1"))
        assertEquals("Token-M", repository.currentToken("computer-2"))
    }

    @Test
    fun testSameHostDifferentIdIsSeparated() = runTest {
        val target = ConnectionTarget.Wifi(host = "192.168.1.100", port = 24862)
        repository.saveToken("id-office", "Token-Office")
        repository.saveToken("id-home", "Token-Home")

        assertEquals("Token-Office", repository.resolveTokenCandidate("id-office", target)?.token)
        assertEquals("Token-Home", repository.resolveTokenCandidate("id-home", target)?.token)
    }

    @Test
    fun testWindowsThenMacThenWindowsKeepsIndependentTokensSameHost() = runTest {
        val sharedHost = ConnectionTarget.Wifi(host = "192.168.1.100", port = 24862)
        val office = StoredDevice(id = "office", name = "Office Windows", type = DeviceType.WIFI, host = sharedHost.host, port = 24862)
        val home = StoredDevice(id = "home", name = "Home Mac", type = DeviceType.WIFI, host = sharedHost.host, port = 24862)

        repository.persistAuthorizedComputer(
            device = office,
            token = "Token-W",
            persistedSession = PersistedSession(target = office, autoRestore = true),
        )
        repository.persistAuthorizedComputer(
            device = home,
            token = "Token-M",
            persistedSession = PersistedSession(target = home, autoRestore = true),
        )

        assertEquals("Token-W", repository.resolveTokenCandidate("office", sharedHost)?.token)
        assertEquals("Token-M", repository.resolveTokenCandidate("home", sharedHost)?.token)

        val devices = repository.recentDevices().first()
        assertEquals(2, devices.size)
        assertTrue(devices.any { it.id == "office" })
        assertTrue(devices.any { it.id == "home" })
    }

    @Test
    fun testRenameComputerDoesNotLoseToken() = runTest {
        val originalDevice = StoredDevice(id = "comp-id", name = "Old Name", type = DeviceType.WIFI, host = "1.2.3.4")
        repository.saveToken(originalDevice.id, "Token-Val")
        repository.saveRecentDevice(originalDevice.copy(name = "New Name"))

        assertEquals("Token-Val", repository.currentToken("comp-id"))
        assertEquals("New Name", repository.recentDevices().first().single().name)
        assertEquals("comp-id", repository.recentDevices().first().single().id)
    }

    @Test
    fun testRemoveRecentDeviceDeletesItsTokenAndPersistedSession() = runTest {
        val device = StoredDevice(id = "target-comp", name = "Office", type = DeviceType.WIFI, host = "192.168.1.10")
        repository.saveRecentDevice(device)
        repository.saveToken(device.id, "secret-token")
        repository.savePersistedSession(PersistedSession(target = device, uiRoute = UiRoute.REMOTE_SESSION))

        repository.removeRecentDevice(device)

        assertNull(repository.currentToken(device.id))
        assertNull(repository.currentPersistedSession())
        assertTrue(repository.recentDevices().first().isEmpty())
    }

    @Test
    fun testResolveTokenCandidateDoesNotDeleteEndpointToken() = runTest {
        val target = ConnectionTarget.Wifi(host = "192.168.1.50", port = 24862)
        val legacyKey = fakeEncryptedTokenStore.getPrefsKeyForTokenKey("wifi:192.168.1.50:24862")
        dataStore.edit { prefs ->
            prefs[legacyKey] = fakeEncryptedTokenStore.encrypt("legacy-endpoint-token")
        }

        val candidate = repository.resolveTokenCandidate("migrated-comp", target)
        assertEquals("legacy-endpoint-token", candidate?.token)
        assertTrue(candidate?.source is TokenSource.LegacyEndpoint)

        val checkPrefs = dataStore.data.first()
        assertEquals(fakeEncryptedTokenStore.encrypt("legacy-endpoint-token"), checkPrefs[legacyKey])
        assertNull(repository.currentToken("migrated-comp"))
    }

    @Test
    fun testCommitSuccessfulMigrationMovesEndpointToken() = runTest {
        val target = ConnectionTarget.Wifi(host = "192.168.1.50", port = 24862)
        val legacyKey = fakeEncryptedTokenStore.getPrefsKeyForTokenKey("wifi:192.168.1.50:24862")
        dataStore.edit { prefs ->
            prefs[legacyKey] = fakeEncryptedTokenStore.encrypt("legacy-endpoint-token")
        }

        val candidate = repository.resolveTokenCandidate("migrated-comp", target)!!
        repository.commitSuccessfulMigration("migrated-comp", candidate)

        assertEquals("legacy-endpoint-token", repository.currentToken("migrated-comp"))
        assertNull(dataStore.data.first()[legacyKey])
    }

    @Test
    fun testClearRejectedCandidateOnlyRemovesEndpointSource() = runTest {
        val target = ConnectionTarget.Wifi(host = "192.168.1.50", port = 24862)
        val legacyKey = fakeEncryptedTokenStore.getPrefsKeyForTokenKey("wifi:192.168.1.50:24862")
        dataStore.edit { prefs ->
            prefs[legacyKey] = fakeEncryptedTokenStore.encrypt("legacy-endpoint-token")
            prefs[stringPreferencesKey("saved_token_encrypted")] =
                fakeEncryptedTokenStore.encrypt("global-token")
        }

        val candidate = repository.resolveTokenCandidate("comp", target)!!
        assertTrue(candidate.source is TokenSource.LegacyEndpoint)

        repository.clearRejectedCandidate("comp", candidate)

        assertNull(dataStore.data.first()[legacyKey])
        assertEquals(
            fakeEncryptedTokenStore.encrypt("global-token"),
            dataStore.data.first()[stringPreferencesKey("saved_token_encrypted")],
        )
    }

    @Test
    fun testBusyPathKeepsEndpointToken() = runTest {
        val target = ConnectionTarget.Wifi(host = "192.168.1.50", port = 24862)
        val legacyKey = fakeEncryptedTokenStore.getPrefsKeyForTokenKey("wifi:192.168.1.50:24862")
        dataStore.edit { prefs ->
            prefs[legacyKey] = fakeEncryptedTokenStore.encrypt("legacy-endpoint-token")
        }

        val candidate = repository.resolveTokenCandidate("comp", target)
        assertNotNull(candidate)
        // BUSY must not call clearRejectedCandidate — token remains.
        assertEquals(
            fakeEncryptedTokenStore.encrypt("legacy-endpoint-token"),
            dataStore.data.first()[legacyKey],
        )
    }

    @Test
    fun testResolveLegacyGlobalDoesNotDeleteUntilCommit() = runTest {
        val target = ConnectionTarget.Wifi(host = "192.168.1.60", port = 24862)
        val legacyGlobalKey = stringPreferencesKey("saved_token_encrypted")
        dataStore.edit { prefs ->
            prefs[legacyGlobalKey] = fakeEncryptedTokenStore.encrypt("legacy-global-encrypted")
        }

        val candidate = repository.resolveTokenCandidate("home-mac", target)
        assertEquals("legacy-global-encrypted", candidate?.token)
        assertEquals(TokenSource.LegacyGlobalEncrypted, candidate?.source)
        assertEquals(
            fakeEncryptedTokenStore.encrypt("legacy-global-encrypted"),
            dataStore.data.first()[legacyGlobalKey],
        )

        // Mac Always Allow with a NEW token must not consume the unverified global token.
        repository.persistAuthorizedComputer(
            device = StoredDevice(id = "home-mac", name = "Home Mac", type = DeviceType.WIFI, host = "192.168.1.60"),
            token = "Token-M",
            persistedSession = PersistedSession(
                target = StoredDevice(id = "home-mac", name = "Home Mac", type = DeviceType.WIFI, host = "192.168.1.60"),
            ),
            migrationCandidate = null,
        )

        assertEquals("Token-M", repository.currentToken("home-mac"))
        assertEquals(
            fakeEncryptedTokenStore.encrypt("legacy-global-encrypted"),
            dataStore.data.first()[legacyGlobalKey],
        )

        // Later Windows reconnect can still resolve and migrate the global token.
        val windowsCandidate = repository.resolveTokenCandidate("office", target)
        assertEquals("legacy-global-encrypted", windowsCandidate?.token)
        repository.commitSuccessfulMigration("office", windowsCandidate!!)
        assertEquals("legacy-global-encrypted", repository.currentToken("office"))
        assertNull(dataStore.data.first()[legacyGlobalKey])
    }

    @Test
    fun testClearRejectedGlobalOnlyWhenCandidateIsGlobal() = runTest {
        val legacyGlobalKey = stringPreferencesKey("saved_token_encrypted")
        dataStore.edit { prefs ->
            prefs[legacyGlobalKey] = fakeEncryptedTokenStore.encrypt("global-token")
        }
        repository.saveToken("office", "profile-token")

        repository.clearRejectedCandidate(
            "office",
            TokenCandidate("profile-token", TokenSource.ComputerProfile("office")),
        )

        assertNull(repository.currentToken("office"))
        assertEquals(
            fakeEncryptedTokenStore.encrypt("global-token"),
            dataStore.data.first()[legacyGlobalKey],
        )

        repository.clearRejectedCandidate(
            "home",
            TokenCandidate("global-token", TokenSource.LegacyGlobalEncrypted),
        )
        assertNull(dataStore.data.first()[legacyGlobalKey])
    }

    @Test
    fun testResolvePlaintextDoesNotDeleteUntilCommit() = runTest {
        val target = ConnectionTarget.Wifi(host = "192.168.1.70", port = 24862)
        val legacyPlaintextKey = stringPreferencesKey("saved_token")
        dataStore.edit { prefs ->
            prefs[legacyPlaintextKey] = "legacy-plaintext"
        }

        val candidate = repository.resolveTokenCandidate("migrated-plaintext-comp", target)
        assertEquals("legacy-plaintext", candidate?.token)
        assertEquals("legacy-plaintext", dataStore.data.first()[legacyPlaintextKey])

        repository.commitSuccessfulMigration("migrated-plaintext-comp", candidate!!)
        assertEquals("legacy-plaintext", repository.currentToken("migrated-plaintext-comp"))
        assertNull(dataStore.data.first()[legacyPlaintextKey])
    }

    @Test
    fun testPersistAuthorizedComputerWritesTokenDeviceAndSessionAtomically() = runTest {
        val device = StoredDevice(id = "office", name = "Office", type = DeviceType.WIFI, host = "10.0.0.1")
        repository.persistAuthorizedComputer(
            device = device,
            token = "Token-W",
            persistedSession = PersistedSession(target = device, autoRestore = true),
        )

        assertEquals("Token-W", repository.currentToken("office"))
        assertEquals("office", repository.recentDevices().first().single().id)
        assertEquals("office", repository.currentPersistedSession()?.target?.id)
        assertTrue(repository.currentPersistedSession()?.autoRestore == true)
    }

    @Test
    fun testAllowOnceDoesNotRequirePersistAuthorizedComputer() = runTest {
        // Simulate Allow Once by never calling persistAuthorizedComputer for a new computer.
        assertTrue(repository.recentDevices().first().isEmpty())
        assertNull(repository.currentPersistedSession())
        assertNull(repository.currentToken("temp-id"))
    }

    @Test
    fun testExistingComputerTemporaryAuthClearsAutoRestoreSession() = runTest {
        val device = StoredDevice(id = "office", name = "Office", type = DeviceType.WIFI, host = "10.0.0.1")
        repository.persistAuthorizedComputer(
            device = device,
            token = "Token-W",
            persistedSession = PersistedSession(target = device, autoRestore = true),
        )
        repository.clearToken("office")
        repository.clearPersistedSession()

        assertEquals(device.id, repository.recentDevices().first().single().id)
        assertNull(repository.currentPersistedSession())
        assertNull(repository.currentToken("office"))
    }

    @Test
    fun testOldStoredDeviceMigratesToStableIdWithoutBackgroundDelay() = runTest {
        val recentDevicesKey = stringPreferencesKey("recent_devices")
        dataStore.edit { prefs ->
            prefs[recentDevicesKey] =
                """[{"name":"Old PC","type":"WIFI","host":"192.168.1.10","port":24862}]"""
        }

        val expectedId = StoredDevice(
            name = "Old PC",
            type = DeviceType.WIFI,
            host = "192.168.1.10",
            port = 24862,
        ).withStableId().id

        // Trigger ensureMigrated through a suspend API.
        val session = repository.currentPersistedSession()
        assertNull(session)

        val devices = repository.recentDevices().first()
        assertEquals(expectedId, devices.single().id)

        // Disk should already be migrated after ensureMigrated.
        val raw = dataStore.data.first()[recentDevicesKey].orEmpty()
        assertTrue(raw.contains(expectedId))
    }

    @Test
    fun testLegacyRecentAndSessionMigrateToSameId() = runTest {
        val recentDevicesKey = stringPreferencesKey("recent_devices")
        val persistedSessionKey = stringPreferencesKey("persisted_session")
        dataStore.edit { prefs ->
            prefs[recentDevicesKey] =
                """[{"name":"Old PC","type":"WIFI","host":"192.168.1.10","port":24862}]"""
            prefs[persistedSessionKey] =
                """{"target":{"name":"Old PC","type":"WIFI","host":"192.168.1.10","port":24862},"uiRoute":"REMOTE_SESSION","autoRestore":true,"manuallyDisconnected":false}"""
        }

        val session = repository.currentPersistedSession()
        val devices = repository.recentDevices().first()
        assertNotNull(session)
        assertEquals(devices.single().id, session!!.target.id)
    }

    @Test
    fun testConcurrentSavesDoNotDropDevices() = runTest {
        coroutineScope {
            val jobs = (1..10).map { index ->
                async {
                    repository.saveRecentDevice(
                        StoredDevice(
                            id = "id-$index",
                            name = "PC $index",
                            type = DeviceType.WIFI,
                            host = "192.168.1.$index",
                            lastConnectedAt = index.toLong(),
                        ),
                    )
                }
            }
            jobs.awaitAll()
        }

        val devices = repository.recentDevices().first()
        assertEquals(10, devices.size)
        assertEquals((1..10).map { "id-$it" }.toSet(), devices.map { it.id }.toSet())
    }

    @Test
    fun testConcurrentUpdateAndDeleteKeepOtherDevice() = runTest {
        repository.saveRecentDevice(
            StoredDevice(id = "keep", name = "Keep", type = DeviceType.WIFI, host = "1.1.1.1"),
        )
        repository.saveRecentDevice(
            StoredDevice(id = "drop", name = "Drop", type = DeviceType.WIFI, host = "2.2.2.2"),
        )

        coroutineScope {
            val update = async {
                repository.saveRecentDevice(
                    StoredDevice(
                        id = "keep",
                        name = "Keep Updated",
                        type = DeviceType.WIFI,
                        host = "1.1.1.1",
                        lastConnectedAt = 99L,
                    ),
                )
            }
            val delete = async {
                repository.removeRecentDevice(
                    StoredDevice(id = "drop", name = "Drop", type = DeviceType.WIFI, host = "2.2.2.2"),
                )
            }
            awaitAll(update, delete)
        }

        val devices = repository.recentDevices().first()
        assertEquals(1, devices.size)
        assertEquals("keep", devices.single().id)
        assertEquals("Keep Updated", devices.single().name)
        assertNull(repository.currentToken("drop"))
    }

    @Test
    fun testMigrationThenSaveDoesNotLoseNewDevice() = runTest {
        val recentDevicesKey = stringPreferencesKey("recent_devices")
        dataStore.edit { prefs ->
            prefs[recentDevicesKey] =
                """[{"name":"Old PC","type":"WIFI","host":"192.168.1.10","port":24862}]"""
        }

        repository.saveRecentDevice(
            StoredDevice(id = "new-id", name = "New PC", type = DeviceType.WIFI, host = "10.0.0.5"),
        )

        val devices = repository.recentDevices().first()
        assertEquals(2, devices.size)
        assertTrue(devices.any { it.id == "new-id" })
        assertTrue(devices.any { it.name == "Old PC" })
    }

    private class FakeEncryptedTokenStore : EncryptedTokenStore {
        override fun getPrefsKeyForTokenKey(tokenKey: String): Preferences.Key<String> {
            val encoded = Base64.getUrlEncoder().withoutPadding().encodeToString(tokenKey.toByteArray(Charsets.UTF_8))
            return stringPreferencesKey("saved_token_encrypted_$encoded")
        }

        override fun encrypt(token: String): String = "encrypted:$token"

        override fun decrypt(value: String): String {
            require(value.startsWith("encrypted:")) { "Not encrypted" }
            return value.removePrefix("encrypted:")
        }
    }
}
