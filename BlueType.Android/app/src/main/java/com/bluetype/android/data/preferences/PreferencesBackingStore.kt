package com.bluetype.android.data.preferences

import android.content.Context
import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.MutablePreferences
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import com.bluetype.android.data.device.DeviceType
import com.bluetype.android.data.device.StoredDevice
import com.bluetype.android.data.device.withStableId
import com.bluetype.android.data.security.EncryptedTokenStore
import com.bluetype.android.data.security.TokenCandidate
import com.bluetype.android.data.security.TokenSource
import com.bluetype.android.data.security.tokenStorageKey
import com.bluetype.android.data.session.PersistedSession
import com.bluetype.android.domain.model.ConnectionTarget
import java.util.UUID
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.serialization.builtins.ListSerializer
import kotlinx.serialization.json.Json

internal val Context.dataStore by preferencesDataStore(name = "blue_type_preferences")

/**
 * Shared DataStore mechanics for the focused preference components.
 *
 * This class deliberately contains no application-facing behavior. It keeps the existing
 * preference keys, migration rules, and serialization in one place while the feature-specific
 * stores expose narrow APIs to callers.
 */
internal class PreferencesBackingStore(
    internal val dataStore: DataStore<Preferences>,
    internal val secureTokenStore: EncryptedTokenStore,
) {
    internal val json = Json {
        ignoreUnknownKeys = true
        explicitNulls = false
    }

    internal val deviceIdKey = stringPreferencesKey("device_id")
    internal val recentDevicesKey = stringPreferencesKey("recent_devices")
    internal val persistedSessionKey = stringPreferencesKey("persisted_session")
    internal val draftTextKey = stringPreferencesKey("draft_text")
    internal val legacyGlobalEncryptedKey = stringPreferencesKey("saved_token_encrypted")
    internal val legacyPlaintextKey = stringPreferencesKey("saved_token")

    private val migrationMutex = Mutex()

    @Volatile
    private var migrated = false

    internal suspend fun ensureMigrated() {
        if (migrated) {
            return
        }
        migrationMutex.withLock {
            if (migrated) {
                return
            }
            dataStore.edit { prefs ->
                val devicesRaw = prefs[recentDevicesKey].orEmpty()
                val devices = decodeStoredDevices(devicesRaw)
                if (devices.any { it.id.isBlank() }) {
                    prefs[recentDevicesKey] = encodeStoredDevices(devices.map { it.withStableId() })
                }

                val sessionRaw = prefs[persistedSessionKey].orEmpty()
                val session = decodePersistedSession(sessionRaw)
                if (session != null && session.target.id.isBlank()) {
                    val migratedSession = migratedPersistedSession(
                        session,
                        prefs[recentDevicesKey].orEmpty(),
                    )
                    prefs[persistedSessionKey] = json.encodeToString(
                        PersistedSession.serializer(),
                        migratedSession,
                    )
                }
            }
            migrated = true
        }
    }

    internal fun readComputerProfileToken(
        prefs: Preferences,
        computerId: String,
    ): TokenCandidate? {
        val key = profileTokenPrefsKey(computerId)
        val encrypted = prefs[key] ?: return null
        val decrypted = runCatching { secureTokenStore.decrypt(encrypted) }.getOrNull() ?: return null
        return TokenCandidate(
            token = decrypted,
            source = TokenSource.ComputerProfile(computerId),
        )
    }

    internal fun readLegacyEndpointToken(
        prefs: Preferences,
        target: ConnectionTarget,
    ): TokenCandidate? {
        val storageKey = target.tokenStorageKey()
        val prefsKey = secureTokenStore.getPrefsKeyForTokenKey(storageKey)
        val encrypted = prefs[prefsKey] ?: return null
        val decrypted = runCatching { secureTokenStore.decrypt(encrypted) }.getOrNull() ?: return null
        return TokenCandidate(
            token = decrypted,
            source = TokenSource.LegacyEndpoint(storageKey),
        )
    }

    internal fun readLegacyGlobalEncryptedToken(prefs: Preferences): TokenCandidate? {
        val encrypted = prefs[legacyGlobalEncryptedKey] ?: return null
        val decrypted = runCatching { secureTokenStore.decrypt(encrypted) }.getOrNull() ?: return null
        return TokenCandidate(
            token = decrypted,
            source = TokenSource.LegacyGlobalEncrypted,
        )
    }

    internal fun readLegacyPlaintextToken(prefs: Preferences): TokenCandidate? {
        val plaintext = prefs[legacyPlaintextKey]?.takeIf { it.isNotBlank() } ?: return null
        return TokenCandidate(
            token = plaintext,
            source = TokenSource.LegacyPlaintext,
        )
    }

    internal fun removeTokenSourceLocked(prefs: MutablePreferences, source: TokenSource) {
        when (source) {
            is TokenSource.ComputerProfile -> {
                prefs.remove(profileTokenPrefsKey(source.computerId))
            }
            is TokenSource.LegacyEndpoint -> {
                prefs.remove(secureTokenStore.getPrefsKeyForTokenKey(source.storageKey))
            }
            TokenSource.LegacyGlobalEncrypted -> {
                prefs.remove(legacyGlobalEncryptedKey)
            }
            TokenSource.LegacyPlaintext -> {
                prefs.remove(legacyPlaintextKey)
            }
        }
    }

    internal fun profileTokenPrefsKey(computerId: String): Preferences.Key<String> {
        return secureTokenStore.getPrefsKeyForTokenKey(computerIdToTokenKey(computerId))
    }

    internal fun decodeStoredDevices(raw: String): List<StoredDevice> {
        if (raw.isBlank()) {
            return emptyList()
        }
        return runCatching {
            json.decodeFromString(ListSerializer(StoredDevice.serializer()), raw)
        }.getOrDefault(emptyList())
    }

    internal fun encodeStoredDevices(devices: List<StoredDevice>): String {
        return json.encodeToString(ListSerializer(StoredDevice.serializer()), devices)
    }

    internal fun upsertDevice(current: List<StoredDevice>, device: StoredDevice): List<StoredDevice> {
        val idx = current.indexOfFirst { it.id == device.id }
        return if (idx >= 0) {
            val updated = current.toMutableList()
            updated[idx] = device
            updated.sortedByDescending { it.lastConnectedAt }
        } else {
            (listOf(device) + current).sortedByDescending { it.lastConnectedAt }
        }
    }

    internal fun decodePersistedSession(raw: String): PersistedSession? {
        if (raw.isBlank()) {
            return null
        }
        return runCatching {
            json.decodeFromString(PersistedSession.serializer(), raw)
        }.getOrNull()
    }

    internal fun migratedPersistedSession(
        session: PersistedSession,
        devicesRaw: String,
    ): PersistedSession {
        if (session.target.id.isNotBlank()) {
            return session
        }

        val devices = decodeStoredDevices(devicesRaw)
        val matchedDevice = devices.firstOrNull {
            (it.type == session.target.type) &&
                ((it.type == DeviceType.WIFI && it.host == session.target.host) ||
                    (it.type == DeviceType.BLUETOOTH && it.address == session.target.address))
        }

        val targetId = matchedDevice?.id?.takeIf { it.isNotBlank() }
            ?: matchedDevice?.withStableId()?.id
            ?: session.target.withStableId().id
        return session.copy(target = session.target.copy(id = targetId))
    }

    private fun computerIdToTokenKey(computerId: String): String {
        return "profile_$computerId"
    }
}
