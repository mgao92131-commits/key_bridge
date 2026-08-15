package com.bluetype.android.data.preferences

import com.bluetype.android.data.DeviceIdentityRepository
import com.bluetype.android.data.StoredDevice
import com.bluetype.android.data.withStableId
import java.util.UUID
import androidx.datastore.preferences.core.edit
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.map

internal class DevicePreferences(
    private val store: PreferencesBackingStore,
) : DeviceIdentityRepository {
    fun recentDevices() = store.dataStore.data.map { prefs ->
        store.decodeStoredDevices(prefs[store.recentDevicesKey].orEmpty())
            .map { it.withStableId() }
    }

    suspend fun saveRecentDevice(device: StoredDevice) {
        store.ensureMigrated()
        val deviceToSave = device.withStableId()
        store.dataStore.edit { prefs ->
            val current = store.decodeStoredDevices(prefs[store.recentDevicesKey].orEmpty())
                .map { it.withStableId() }
            prefs[store.recentDevicesKey] = store.encodeStoredDevices(
                store.upsertDevice(current, deviceToSave),
            )
        }
    }

    /**
     * Removing a device also removes its token and persisted session, preserving the old
     * repository's atomic cleanup behavior.
     */
    suspend fun removeRecentDevice(device: StoredDevice) {
        store.ensureMigrated()
        val deviceId = device.withStableId().id
        val profileKey = store.profileTokenPrefsKey(deviceId)
        store.dataStore.edit { prefs ->
            val current = store.decodeStoredDevices(prefs[store.recentDevicesKey].orEmpty())
                .map { it.withStableId() }
            prefs[store.recentDevicesKey] = store.encodeStoredDevices(
                current.filterNot { it.id == deviceId },
            )
            prefs.remove(profileKey)

            val session = store.decodePersistedSession(prefs[store.persistedSessionKey].orEmpty())
            if (session != null && session.target.withStableId().id == deviceId) {
                prefs.remove(store.persistedSessionKey)
            }
        }
    }

    override suspend fun getOrCreateDeviceId(): String {
        val current = store.dataStore.data.first()[store.deviceIdKey]
        if (!current.isNullOrBlank()) {
            return current
        }

        val generated = "android-${UUID.randomUUID().toString().take(8)}"
        store.dataStore.edit { prefs ->
            prefs[store.deviceIdKey] = generated
        }
        return generated
    }
}
