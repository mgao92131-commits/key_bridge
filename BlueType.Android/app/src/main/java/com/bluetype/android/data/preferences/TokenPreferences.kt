package com.bluetype.android.data.preferences

import androidx.datastore.preferences.core.edit
import com.bluetype.android.data.TokenCandidate
import com.bluetype.android.data.TokenRepository
import com.bluetype.android.data.TokenSource
import com.bluetype.android.domain.ConnectionTarget
import kotlinx.coroutines.flow.first

internal class TokenPreferences(
    private val store: PreferencesBackingStore,
) : TokenRepository {
    override suspend fun currentToken(computerId: String): String? {
        store.ensureMigrated()
        val key = store.profileTokenPrefsKey(computerId)
        val encrypted = store.dataStore.data.first()[key] ?: return null
        return runCatching { store.secureTokenStore.decrypt(encrypted) }.getOrNull()
    }

    override suspend fun resolveTokenCandidate(
        computerId: String,
        target: ConnectionTarget,
    ): TokenCandidate? {
        store.ensureMigrated()
        val prefs = store.dataStore.data.first()

        store.readComputerProfileToken(prefs, computerId)?.let { return it }
        store.readLegacyEndpointToken(prefs, target)?.let { return it }
        store.readLegacyGlobalEncryptedToken(prefs)?.let { return it }
        store.readLegacyPlaintextToken(prefs)?.let { return it }
        return null
    }

    override suspend fun saveToken(computerId: String, token: String) {
        store.ensureMigrated()
        val key = store.profileTokenPrefsKey(computerId)
        val encrypted = store.secureTokenStore.encrypt(token)
        store.dataStore.edit { prefs ->
            prefs[key] = encrypted
        }
    }

    override suspend fun commitSuccessfulMigration(
        computerId: String,
        candidate: TokenCandidate,
    ) {
        store.ensureMigrated()
        when (candidate.source) {
            is TokenSource.ComputerProfile -> {
                if (candidate.source.computerId == computerId) {
                    return
                }
            }
            else -> Unit
        }

        val encrypted = store.secureTokenStore.encrypt(candidate.token)
        val profileKey = store.profileTokenPrefsKey(computerId)
        store.dataStore.edit { prefs ->
            prefs[profileKey] = encrypted
            store.removeTokenSourceLocked(prefs, candidate.source)
        }
    }

    override suspend fun clearRejectedCandidate(
        computerId: String,
        candidate: TokenCandidate?,
    ) {
        store.ensureMigrated()
        if (candidate == null) {
            return
        }

        store.dataStore.edit { prefs ->
            store.removeTokenSourceLocked(prefs, candidate.source)
        }
    }

    override suspend fun clearToken(computerId: String) {
        store.ensureMigrated()
        val key = store.profileTokenPrefsKey(computerId)
        store.dataStore.edit { prefs ->
            prefs.remove(key)
        }
    }
}
