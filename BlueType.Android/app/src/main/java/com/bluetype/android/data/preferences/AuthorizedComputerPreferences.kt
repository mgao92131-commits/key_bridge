package com.bluetype.android.data.preferences

import androidx.datastore.preferences.core.edit
import com.bluetype.android.data.PersistedSession
import com.bluetype.android.data.StoredDevice
import com.bluetype.android.data.TokenCandidate
import com.bluetype.android.data.TokenSource
import com.bluetype.android.data.withStableId

/** Coordinates the one atomic write used after successful authorization. */
internal class AuthorizedComputerPreferences(
    private val store: PreferencesBackingStore,
) {
    suspend fun persistAuthorizedComputer(
        device: StoredDevice,
        token: String?,
        persistedSession: PersistedSession?,
        migrationCandidate: TokenCandidate? = null,
    ) {
        store.ensureMigrated()
        val deviceToSave = device.withStableId()
        val encryptedToken = token?.let { store.secureTokenStore.encrypt(it) }
        val migrationEncrypted = when {
            migrationCandidate == null -> null
            migrationCandidate.source is TokenSource.ComputerProfile &&
                migrationCandidate.source.computerId == deviceToSave.id -> null
            token != null -> null
            else -> store.secureTokenStore.encrypt(migrationCandidate.token)
        }

        store.dataStore.edit { prefs ->
            val current = store.decodeStoredDevices(prefs[store.recentDevicesKey].orEmpty())
                .map { it.withStableId() }
            prefs[store.recentDevicesKey] = store.encodeStoredDevices(
                store.upsertDevice(current, deviceToSave),
            )

            if (persistedSession != null) {
                prefs[store.persistedSessionKey] = store.json.encodeToString(
                    PersistedSession.serializer(),
                    persistedSession.copy(target = deviceToSave),
                )
            }

            val profileKey = store.profileTokenPrefsKey(deviceToSave.id)
            when {
                encryptedToken != null -> prefs[profileKey] = encryptedToken
                migrationEncrypted != null && migrationCandidate != null -> {
                    prefs[profileKey] = migrationEncrypted
                    store.removeTokenSourceLocked(prefs, migrationCandidate.source)
                }
            }
        }
    }
}
