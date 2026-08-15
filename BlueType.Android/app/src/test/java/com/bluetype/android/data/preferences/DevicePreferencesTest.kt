package com.bluetype.android.data.preferences

import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.PreferenceDataStoreFactory
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.stringPreferencesKey
import com.bluetype.android.data.device.DeviceType
import com.bluetype.android.data.device.StoredDevice
import com.bluetype.android.data.security.EncryptedTokenStore
import java.io.File
import java.util.Base64
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.test.runTest
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

class DevicePreferencesTest {
    private lateinit var tempFile: File
    private lateinit var dataStore: DataStore<Preferences>
    private lateinit var preferences: DevicePreferences

    @Before
    fun setUp() {
        tempFile = File.createTempFile("device_preferences", ".preferences_pb")
        dataStore = PreferenceDataStoreFactory.create(produceFile = { tempFile })
        preferences = DevicePreferences(
            PreferencesBackingStore(dataStore, FakeEncryptedTokenStore()),
        )
    }

    @After
    fun tearDown() {
        tempFile.delete()
    }

    @Test
    fun saveUpdateAndDeleteRoundTrip() = runTest {
        val device = StoredDevice(
            id = "office",
            name = "Office",
            type = DeviceType.WIFI,
            host = "192.168.1.20",
            port = 24862,
        )

        preferences.saveRecentDevice(device)
        preferences.saveRecentDevice(device.copy(name = "Renamed Office"))

        val saved = preferences.recentDevices().first()
        assertEquals(1, saved.size)
        assertEquals("Renamed Office", saved.single().name)

        preferences.removeRecentDevice(device)
        assertTrue(preferences.recentDevices().first().isEmpty())
    }

    @Test
    fun legacyDeviceGetsStableId() = runTest {
        preferences.saveRecentDevice(
            StoredDevice(
                name = "Legacy PC",
                type = DeviceType.WIFI,
                host = "192.168.1.21",
                port = 24862,
            ),
        )

        val saved = preferences.recentDevices().first().single()
        assertFalse(saved.id.isBlank())
        assertTrue(saved.id == preferences.recentDevices().first().single().id)
    }

    private class FakeEncryptedTokenStore : EncryptedTokenStore {
        override fun getPrefsKeyForTokenKey(tokenKey: String): Preferences.Key<String> {
            val encoded = Base64.getUrlEncoder()
                .withoutPadding()
                .encodeToString(tokenKey.toByteArray(Charsets.UTF_8))
            return stringPreferencesKey("saved_token_encrypted_$encoded")
        }

        override fun encrypt(token: String): String = "encrypted:$token"

        override fun decrypt(value: String): String {
            require(value.startsWith("encrypted:"))
            return value.removePrefix("encrypted:")
        }
    }
}
