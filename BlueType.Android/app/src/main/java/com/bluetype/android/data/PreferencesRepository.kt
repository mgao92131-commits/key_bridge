package com.bluetype.android.data

import android.content.Context
import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.MutablePreferences
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import com.bluetype.android.domain.ConnectionTarget
import java.util.UUID
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
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
    private val legacyGlobalEncryptedKey = stringPreferencesKey("saved_token_encrypted")
    private val legacyPlaintextKey = stringPreferencesKey("saved_token")

    private val migrationMutex = Mutex()
    @Volatile
    private var migrated = false

    fun recentDevices() = dataStore.data.map { prefs ->
        decodeStoredDevices(prefs[recentDevicesKey].orEmpty()).map { it.withStableId() }
    }

    fun persistedSession() = dataStore.data.map { prefs ->
        val session = decodePersistedSession(prefs[persistedSessionKey].orEmpty()) ?: return@map null
        migratedPersistedSession(session, prefs[recentDevicesKey].orEmpty())
    }

    fun draftText() = dataStore.data.map { prefs ->
        prefs[draftTextKey].orEmpty()
    }

    override suspend fun currentToken(computerId: String): String? {
        ensureMigrated()
        val key = profileTokenPrefsKey(computerId)
        val encrypted = dataStore.data.first()[key] ?: return null
        return runCatching { secureTokenStore.decrypt(encrypted) }.getOrNull()
    }

    override suspend fun resolveTokenCandidate(
        computerId: String,
        target: ConnectionTarget,
    ): TokenCandidate? {
        ensureMigrated()
        val prefs = dataStore.data.first()

        readComputerProfileToken(prefs, computerId)?.let { return it }
        readLegacyEndpointToken(prefs, target)?.let { return it }
        readLegacyGlobalEncryptedToken(prefs)?.let { return it }
        readLegacyPlaintextToken(prefs)?.let { return it }
        return null
    }

    override suspend fun saveToken(computerId: String, token: String) {
        ensureMigrated()
        val key = profileTokenPrefsKey(computerId)
        val encrypted = secureTokenStore.encrypt(token)
        dataStore.edit { prefs ->
            prefs[key] = encrypted
        }
    }

    override suspend fun commitSuccessfulMigration(
        computerId: String,
        candidate: TokenCandidate,
    ) {
        ensureMigrated()
        when (candidate.source) {
            is TokenSource.ComputerProfile -> {
                // Already stored under this computer; nothing to migrate.
                if (candidate.source.computerId == computerId) {
                    return
                }
            }
            else -> Unit
        }

        val encrypted = secureTokenStore.encrypt(candidate.token)
        val profileKey = profileTokenPrefsKey(computerId)
        dataStore.edit { prefs ->
            prefs[profileKey] = encrypted
            removeTokenSourceLocked(prefs, candidate.source)
        }
    }

    override suspend fun clearRejectedCandidate(
        computerId: String,
        candidate: TokenCandidate?,
    ) {
        ensureMigrated()
        if (candidate == null) {
            return
        }

        dataStore.edit { prefs ->
            removeTokenSourceLocked(prefs, candidate.source)
        }
    }

    override suspend fun clearToken(computerId: String) {
        ensureMigrated()
        val key = profileTokenPrefsKey(computerId)
        dataStore.edit { prefs ->
            prefs.remove(key)
        }
    }

    override suspend fun currentPersistedSession(): PersistedSession? {
        ensureMigrated()
        val prefs = dataStore.data.first()
        val session = decodePersistedSession(prefs[persistedSessionKey].orEmpty()) ?: return null
        return migratedPersistedSession(session, prefs[recentDevicesKey].orEmpty())
    }

    /**
     * Atomically persists an authorized computer: profile token (optional), recent device list,
     * and persisted session (optional). Optionally commits a successful legacy token migration.
     */
    suspend fun persistAuthorizedComputer(
        device: StoredDevice,
        token: String?,
        persistedSession: PersistedSession?,
        migrationCandidate: TokenCandidate? = null,
    ) {
        ensureMigrated()
        val deviceToSave = device.withStableId()
        val encryptedToken = token?.let { secureTokenStore.encrypt(it) }
        val migrationEncrypted = when {
            migrationCandidate == null -> null
            migrationCandidate.source is TokenSource.ComputerProfile &&
                migrationCandidate.source.computerId == deviceToSave.id -> null
            token != null -> null // New token takes precedence; do not migrate unverified legacy.
            else -> secureTokenStore.encrypt(migrationCandidate.token)
        }

        dataStore.edit { prefs ->
            val current = decodeStoredDevices(prefs[recentDevicesKey].orEmpty()).map { it.withStableId() }
            prefs[recentDevicesKey] = encodeStoredDevices(upsertDevice(current, deviceToSave))

            if (persistedSession != null) {
                prefs[persistedSessionKey] = json.encodeToString(
                    PersistedSession.serializer(),
                    persistedSession.copy(target = deviceToSave),
                )
            }

            val profileKey = profileTokenPrefsKey(deviceToSave.id)
            when {
                encryptedToken != null -> prefs[profileKey] = encryptedToken
                migrationEncrypted != null && migrationCandidate != null -> {
                    prefs[profileKey] = migrationEncrypted
                    removeTokenSourceLocked(prefs, migrationCandidate.source)
                }
            }
        }
    }

    suspend fun saveRecentDevice(device: StoredDevice) {
        ensureMigrated()
        val deviceToSave = device.withStableId()
        dataStore.edit { prefs ->
            val current = decodeStoredDevices(prefs[recentDevicesKey].orEmpty()).map { it.withStableId() }
            prefs[recentDevicesKey] = encodeStoredDevices(upsertDevice(current, deviceToSave))
        }
    }

    suspend fun removeRecentDevice(device: StoredDevice) {
        ensureMigrated()
        val deviceId = device.withStableId().id
        val profileKey = profileTokenPrefsKey(deviceId)
        dataStore.edit { prefs ->
            val current = decodeStoredDevices(prefs[recentDevicesKey].orEmpty()).map { it.withStableId() }
            prefs[recentDevicesKey] = encodeStoredDevices(current.filterNot { it.id == deviceId })

            prefs.remove(profileKey)

            val session = decodePersistedSession(prefs[persistedSessionKey].orEmpty())
            if (session != null && session.target.withStableId().id == deviceId) {
                prefs.remove(persistedSessionKey)
            }
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
        ensureMigrated()
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

    private suspend fun ensureMigrated() {
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

    private fun readComputerProfileToken(
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

    private fun readLegacyEndpointToken(
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

    private fun readLegacyGlobalEncryptedToken(prefs: Preferences): TokenCandidate? {
        val encrypted = prefs[legacyGlobalEncryptedKey] ?: return null
        val decrypted = runCatching { secureTokenStore.decrypt(encrypted) }.getOrNull() ?: return null
        return TokenCandidate(
            token = decrypted,
            source = TokenSource.LegacyGlobalEncrypted,
        )
    }

    private fun readLegacyPlaintextToken(prefs: Preferences): TokenCandidate? {
        val plaintext = prefs[legacyPlaintextKey]?.takeIf { it.isNotBlank() } ?: return null
        return TokenCandidate(
            token = plaintext,
            source = TokenSource.LegacyPlaintext,
        )
    }

    private fun removeTokenSourceLocked(prefs: MutablePreferences, source: TokenSource) {
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

    private fun profileTokenPrefsKey(computerId: String): Preferences.Key<String> {
        return secureTokenStore.getPrefsKeyForTokenKey(computerIdToTokenKey(computerId))
    }

    private fun computerIdToTokenKey(computerId: String): String {
        return "profile_$computerId"
    }

    private fun decodeStoredDevices(raw: String): List<StoredDevice> {
        if (raw.isBlank()) {
            return emptyList()
        }
        return runCatching {
            json.decodeFromString(ListSerializer(StoredDevice.serializer()), raw)
        }.getOrDefault(emptyList())
    }

    private fun encodeStoredDevices(devices: List<StoredDevice>): String {
        return json.encodeToString(ListSerializer(StoredDevice.serializer()), devices)
    }

    private fun upsertDevice(current: List<StoredDevice>, device: StoredDevice): List<StoredDevice> {
        val idx = current.indexOfFirst { it.id == device.id }
        return if (idx >= 0) {
            val updated = current.toMutableList()
            updated[idx] = device
            updated.sortedByDescending { it.lastConnectedAt }
        } else {
            (listOf(device) + current).sortedByDescending { it.lastConnectedAt }
        }
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
}
