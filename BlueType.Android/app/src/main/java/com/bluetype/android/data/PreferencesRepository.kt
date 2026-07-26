package com.bluetype.android.data

import android.content.Context
import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import com.bluetype.android.domain.ConnectionTarget
import java.util.UUID
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.launch
import kotlinx.serialization.builtins.ListSerializer
import kotlinx.serialization.json.Json

internal val Context.dataStore by preferencesDataStore(name = "blue_type_preferences")

class PreferencesRepository internal constructor(
    private val context: Context? = null,
    private val dataStore: DataStore<Preferences> = context!!.dataStore,
    private val secureTokenStore: EncryptedTokenStore = SecureTokenStore(context!!),
) : TokenRepository,
    DeviceIdentityRepository,
    PersistedSessionRepository {
    private val json = Json {
        ignoreUnknownKeys = true
        explicitNulls = false
    }

    private val deviceIdKey = stringPreferencesKey("device_id")
    private val recentDevicesKey = stringPreferencesKey("recent_devices")
    private val persistedSessionKey = stringPreferencesKey("persisted_session")
    private val draftTextKey = stringPreferencesKey("draft_text")

    fun recentDevices() = dataStore.data.map { prefs ->
        val raw = prefs[recentDevicesKey].orEmpty()
        if (raw.isBlank()) {
            emptyList()
        } else {
            val decoded = runCatching {
                json.decodeFromString(ListSerializer(StoredDevice.serializer()), raw)
            }.getOrDefault(emptyList())

            val needsMigration = decoded.any { it.id.isBlank() }
            if (needsMigration) {
                val migrated = decoded.map { it.withStableId() }
                CoroutineScope(Dispatchers.IO).launch {
                    dataStore.edit { editPrefs ->
                        editPrefs[recentDevicesKey] = json.encodeToString(ListSerializer(StoredDevice.serializer()), migrated)
                    }
                }
                migrated
            } else {
                decoded
            }
        }
    }

    fun persistedSession() = dataStore.data.map { prefs ->
        val raw = prefs[persistedSessionKey].orEmpty()
        val session = decodePersistedSession(raw) ?: return@map null
        val migrated = migratedPersistedSession(session, prefs[recentDevicesKey].orEmpty())
        if (migrated.target.id != session.target.id) {
            CoroutineScope(Dispatchers.IO).launch {
                dataStore.edit { editPrefs ->
                    editPrefs[persistedSessionKey] = json.encodeToString(PersistedSession.serializer(), migrated)
                }
            }
        }
        migrated
    }

    fun draftText() = dataStore.data.map { prefs ->
        prefs[draftTextKey].orEmpty()
    }

    override suspend fun currentToken(computerId: String): String? {
        val key = secureTokenStore.getPrefsKeyForTokenKey(computerIdToTokenKey(computerId))
        val encrypted = dataStore.data.first()[key] ?: return null
        return runCatching { secureTokenStore.decrypt(encrypted) }.getOrNull()
    }

    override suspend fun currentPersistedSession(): PersistedSession? {
        val raw = dataStore.data.first()[persistedSessionKey].orEmpty()
        val session = decodePersistedSession(raw) ?: return null
        val migrated = migratedPersistedSession(session, dataStore.data.first()[recentDevicesKey].orEmpty())
        if (migrated.target.id != session.target.id) {
            dataStore.edit { prefs ->
                prefs[persistedSessionKey] = json.encodeToString(PersistedSession.serializer(), migrated)
            }
        }
        return migrated
    }

    override suspend fun saveToken(computerId: String, token: String) {
        val key = secureTokenStore.getPrefsKeyForTokenKey(computerIdToTokenKey(computerId))
        val encrypted = secureTokenStore.encrypt(token)
        dataStore.edit { prefs ->
            prefs[key] = encrypted
        }
    }

    override suspend fun clearToken(computerId: String) {
        val key = secureTokenStore.getPrefsKeyForTokenKey(computerIdToTokenKey(computerId))
        dataStore.edit { prefs ->
            prefs.remove(key)
        }
    }

    override suspend fun getAndMigrateToken(computerId: String, target: ConnectionTarget): String? {
        val newKey = computerIdToTokenKey(computerId)
        val newPrefsKey = secureTokenStore.getPrefsKeyForTokenKey(newKey)

        val prefs = dataStore.data.first()

        // 1. 尝试读取新 Token
        val currentNewEncrypted = prefs[newPrefsKey]
        if (currentNewEncrypted != null) {
            val decrypted = runCatching {
                secureTokenStore.decrypt(currentNewEncrypted)
            }.getOrNull()
            if (decrypted != null) {
                return decrypted
            } else {
                dataStore.edit { it.remove(newPrefsKey) }
            }
        }

        // 2. 尝试读取当前 endpoint 对应的旧 Token
        val oldEndpointKey = target.tokenStorageKey()
        val oldEndpointPrefsKey = secureTokenStore.getPrefsKeyForTokenKey(oldEndpointKey)
        val currentOldEndpointEncrypted = prefs[oldEndpointPrefsKey]
        if (currentOldEndpointEncrypted != null) {
            val decrypted = runCatching {
                secureTokenStore.decrypt(currentOldEndpointEncrypted)
            }.getOrNull()
            if (decrypted != null) {
                val newEncrypted = secureTokenStore.encrypt(decrypted)
                dataStore.edit { editPrefs ->
                    editPrefs[newPrefsKey] = newEncrypted
                    editPrefs.remove(oldEndpointPrefsKey)
                }
                return decrypted
            } else {
                dataStore.edit { it.remove(oldEndpointPrefsKey) }
            }
        }

        // 3. 尝试读取旧全局加密 Token
        val legacyEncryptedKey = stringPreferencesKey("saved_token_encrypted")
        val currentLegacyEncrypted = prefs[legacyEncryptedKey]
        if (currentLegacyEncrypted != null) {
            val decrypted = runCatching {
                secureTokenStore.decrypt(currentLegacyEncrypted)
            }.getOrNull()
            if (decrypted != null) {
                val newEncrypted = secureTokenStore.encrypt(decrypted)
                dataStore.edit { editPrefs ->
                    editPrefs[newPrefsKey] = newEncrypted
                    editPrefs.remove(legacyEncryptedKey)
                }
                return decrypted
            } else {
                dataStore.edit { it.remove(legacyEncryptedKey) }
            }
        }

        // 4. 尝试读取旧明文 Token
        val legacyTokenKey = stringPreferencesKey("saved_token")
        val legacyPlaintext = prefs[legacyTokenKey]?.takeIf { it.isNotBlank() }
        if (legacyPlaintext != null) {
            val newEncrypted = secureTokenStore.encrypt(legacyPlaintext)
            dataStore.edit { editPrefs ->
                editPrefs[newPrefsKey] = newEncrypted
                editPrefs.remove(legacyTokenKey)
            }
            return legacyPlaintext
        }

        return null
    }

    override suspend fun clearOldGlobalToken() {
        dataStore.edit { editPrefs ->
            editPrefs.remove(stringPreferencesKey("saved_token_encrypted"))
            editPrefs.remove(stringPreferencesKey("saved_token"))
        }
    }

    private fun computerIdToTokenKey(computerId: String): String {
        return "profile_$computerId"
    }

    suspend fun saveRecentDevice(device: StoredDevice) {
        val existing = dataStore.data.first()[recentDevicesKey].orEmpty()
        val current = if (existing.isBlank()) {
            emptyList()
        } else {
            runCatching {
                json.decodeFromString(ListSerializer(StoredDevice.serializer()), existing)
            }.getOrDefault(emptyList())
        }

        val deviceToSave = device.withStableId()
        val updatedCurrent = current.map { it.withStableId() }

        val idx = updatedCurrent.indexOfFirst { it.id == deviceToSave.id }
        val next = if (idx >= 0) {
            val updated = updatedCurrent.toMutableList()
            updated[idx] = deviceToSave
            updated.sortedByDescending { it.lastConnectedAt }
        } else {
            (listOf(deviceToSave) + updatedCurrent).sortedByDescending { it.lastConnectedAt }
        }

        dataStore.edit { prefs ->
            prefs[recentDevicesKey] = json.encodeToString(ListSerializer(StoredDevice.serializer()), next)
        }
    }

    suspend fun removeRecentDevice(device: StoredDevice) {
        val existing = dataStore.data.first()[recentDevicesKey].orEmpty()
        if (existing.isBlank()) return

        val current = runCatching {
            json.decodeFromString(ListSerializer(StoredDevice.serializer()), existing)
        }.getOrDefault(emptyList())

        val deviceId = device.withStableId().id
        val next = current.map { it.withStableId() }.filterNot { it.id == deviceId }

        // 删除该电脑在 Android 本地的 Token
        clearToken(deviceId)

        // 检查 PersistedSession 是否引用了该电脑，若是则清除该持久会话
        val persisted = currentPersistedSession()
        if (persisted != null && persisted.target.id == deviceId) {
            clearPersistedSession()
        }

        dataStore.edit { prefs ->
            prefs[recentDevicesKey] = json.encodeToString(ListSerializer(StoredDevice.serializer()), next)
        }
    }

    suspend fun saveDraftText(value: String) {
        dataStore.edit { prefs ->
            if (value.isEmpty()) {
                prefs.remove(draftTextKey)
            } else {
                prefs[draftTextKey] = value
            }
        }
    }

    override suspend fun savePersistedSession(session: PersistedSession) {
        dataStore.edit { prefs ->
            prefs[persistedSessionKey] = json.encodeToString(
                PersistedSession.serializer(),
                session.copy(target = session.target.withStableId()),
            )
        }
    }

    override suspend fun clearPersistedSession() {
        dataStore.edit { prefs ->
            prefs.remove(persistedSessionKey)
        }
    }

    override suspend fun getOrCreateDeviceId(): String {
        val current = dataStore.data.first()[deviceIdKey]
        if (!current.isNullOrBlank()) {
            return current
        }

        val generated = "android-${UUID.randomUUID().toString().take(8)}"
        dataStore.edit { prefs ->
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

    private fun migratedPersistedSession(
        session: PersistedSession,
        devicesRaw: String,
    ): PersistedSession {
        if (session.target.id.isNotBlank()) {
            return session
        }

        val devices = if (devicesRaw.isBlank()) {
            emptyList()
        } else {
            runCatching {
                json.decodeFromString(ListSerializer(StoredDevice.serializer()), devicesRaw)
            }.getOrDefault(emptyList())
        }

        val matchedDevice = devices.firstOrNull {
            (it.type == session.target.type) &&
                ((it.type == DeviceType.WIFI && it.host == session.target.host) ||
                    (it.type == DeviceType.BLUETOOTH && it.address == session.target.address))
        }

        // Prefer an already-assigned recent-device id; otherwise use the deterministic endpoint id
        // so concurrent recent/session migrations always converge on the same value.
        val targetId = matchedDevice?.id?.takeIf { it.isNotBlank() }
            ?: matchedDevice?.withStableId()?.id
            ?: session.target.withStableId().id
        return session.copy(target = session.target.copy(id = targetId))
    }
}
