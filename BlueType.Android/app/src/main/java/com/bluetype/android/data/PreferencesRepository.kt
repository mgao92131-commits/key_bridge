package com.bluetype.android.data

import android.content.Context
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import java.util.UUID
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.map
import kotlinx.serialization.builtins.ListSerializer
import kotlinx.serialization.json.Json

internal val Context.dataStore by preferencesDataStore(name = "blue_type_preferences")

class PreferencesRepository(
    private val context: Context,
) : TokenRepository,
    DeviceIdentityRepository,
    PersistedSessionRepository {
    private val json = Json {
        ignoreUnknownKeys = true
        explicitNulls = false
    }

    private val deviceIdKey = stringPreferencesKey("device_id")
    private val legacyTokenKey = stringPreferencesKey("saved_token")
    private val recentDevicesKey = stringPreferencesKey("recent_devices")
    private val persistedSessionKey = stringPreferencesKey("persisted_session")
    private val bluetoothRfcommChannelsKey = stringPreferencesKey("bluetooth_rfcomm_channels")
    private val draftTextKey = stringPreferencesKey("draft_text")
    private val secureTokenStore = SecureTokenStore(context)

    fun recentDevices() = context.dataStore.data.map { prefs ->
        val raw = prefs[recentDevicesKey].orEmpty()
        if (raw.isBlank()) {
            emptyList()
        } else {
            runCatching {
                json.decodeFromString(ListSerializer(StoredDevice.serializer()), raw)
            }.getOrDefault(emptyList())
        }
    }

    fun persistedSession() = context.dataStore.data.map { prefs ->
        decodePersistedSession(prefs[persistedSessionKey].orEmpty())
    }

    fun draftText() = context.dataStore.data.map { prefs ->
        prefs[draftTextKey].orEmpty()
    }

    override suspend fun currentToken(): String? = secureTokenStore.currentToken(legacyTokenKey)

    override suspend fun currentPersistedSession(): PersistedSession? {
        return decodePersistedSession(context.dataStore.data.first()[persistedSessionKey].orEmpty())
    }

    override suspend fun saveToken(token: String) {
        secureTokenStore.saveToken(legacyTokenKey, token)
    }

    override suspend fun clearToken() {
        secureTokenStore.clearToken(legacyTokenKey)
    }

    suspend fun saveRecentDevice(device: StoredDevice) {
        val existing = context.dataStore.data.first()[recentDevicesKey].orEmpty()
        val current = if (existing.isBlank()) {
            emptyList()
        } else {
            runCatching {
                json.decodeFromString(ListSerializer(StoredDevice.serializer()), existing)
            }.getOrDefault(emptyList())
        }

        val next = (listOf(device) + current.filterNot { it.host == device.host && it.address == device.address }).take(5)
        context.dataStore.edit { prefs ->
            prefs[recentDevicesKey] = json.encodeToString(ListSerializer(StoredDevice.serializer()), next)
        }
    }

    suspend fun removeRecentDevice(device: StoredDevice) {
        val existing = context.dataStore.data.first()[recentDevicesKey].orEmpty()
        if (existing.isBlank()) return

        val current = runCatching {
            json.decodeFromString(ListSerializer(StoredDevice.serializer()), existing)
        }.getOrDefault(emptyList())

        val next = current.filterNot { it.host == device.host && it.address == device.address }
        context.dataStore.edit { prefs ->
            prefs[recentDevicesKey] = json.encodeToString(ListSerializer(StoredDevice.serializer()), next)
        }
    }

    suspend fun saveDraftText(value: String) {
        context.dataStore.edit { prefs ->
            if (value.isEmpty()) {
                prefs.remove(draftTextKey)
            } else {
                prefs[draftTextKey] = value
            }
        }
    }

    override suspend fun savePersistedSession(session: PersistedSession) {
        context.dataStore.edit { prefs ->
            prefs[persistedSessionKey] = json.encodeToString(PersistedSession.serializer(), session)
        }
    }

    override suspend fun clearPersistedSession() {
        context.dataStore.edit { prefs ->
            prefs.remove(persistedSessionKey)
        }
    }

    suspend fun bluetoothRfcommChannel(address: String): Int? {
        val normalizedAddress = normalizeBluetoothAddress(address)
        if (normalizedAddress.isBlank()) {
            return null
        }

        return decodeBluetoothChannels(context.dataStore.data.first()[bluetoothRfcommChannelsKey].orEmpty())[normalizedAddress]
    }

    suspend fun saveBluetoothRfcommChannel(address: String, channel: Int) {
        val normalizedAddress = normalizeBluetoothAddress(address)
        if (normalizedAddress.isBlank() || channel !in 1..30) {
            return
        }

        val existing = decodeBluetoothChannels(context.dataStore.data.first()[bluetoothRfcommChannelsKey].orEmpty())
        context.dataStore.edit { prefs ->
            prefs[bluetoothRfcommChannelsKey] = json.encodeToString(existing + (normalizedAddress to channel))
        }
    }

    override suspend fun getOrCreateDeviceId(): String {
        val current = context.dataStore.data.first()[deviceIdKey]
        if (!current.isNullOrBlank()) {
            return current
        }

        val generated = "android-${UUID.randomUUID().toString().take(8)}"
        context.dataStore.edit { prefs ->
            prefs[deviceIdKey] = generated
        }
        return generated
    }

    private fun decodePersistedSession(raw: String): PersistedSession? {
        if (raw.isBlank()) {
            return null
        }

        return runCatching {
            json.decodeFromString(PersistedSession.serializer(), raw)
        }.getOrNull()
    }

    private fun decodeBluetoothChannels(raw: String): Map<String, Int> {
        if (raw.isBlank()) {
            return emptyMap()
        }

        return runCatching {
            json.decodeFromString<Map<String, Int>>(raw)
        }.getOrDefault(emptyMap())
    }

    private fun normalizeBluetoothAddress(address: String): String {
        return address.trim().uppercase()
    }
}
