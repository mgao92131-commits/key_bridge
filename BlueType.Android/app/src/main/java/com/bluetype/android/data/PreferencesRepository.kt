package com.bluetype.android.data

import android.content.Context
import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.Preferences
import com.bluetype.android.data.preferences.AuthorizedComputerPreferences
import com.bluetype.android.data.preferences.DevicePreferences
import com.bluetype.android.data.preferences.PreferencesBackingStore
import com.bluetype.android.data.preferences.TokenPreferences
import com.bluetype.android.data.preferences.UiPreferences
import com.bluetype.android.data.preferences.dataStore
import com.bluetype.android.data.session.SessionStorage

/**
 * Temporary compatibility facade for callers that still need the legacy aggregate API.
 * New code should depend on DevicePreferences, TokenPreferences, UiPreferences, or SessionStorage.
 */
class PreferencesRepository internal constructor(
    context: Context? = null,
    dataStore: DataStore<Preferences> = context!!.dataStore,
    secureTokenStore: EncryptedTokenStore = SecureTokenStore(context!!),
) : TokenRepository,
    DeviceIdentityRepository,
    PersistedSessionRepository {
    private val backingStore = PreferencesBackingStore(dataStore, secureTokenStore)
    private val devicePreferences = DevicePreferences(backingStore)
    private val tokenPreferences = TokenPreferences(backingStore)
    private val uiPreferences = UiPreferences(backingStore)
    private val sessionStorage = SessionStorage(backingStore)
    private val authorizedComputerPreferences = AuthorizedComputerPreferences(backingStore)

    fun recentDevices() = devicePreferences.recentDevices()

    fun persistedSession() = sessionStorage.persistedSession()

    fun draftText() = uiPreferences.draftText()

    override suspend fun currentToken(computerId: String): String? =
        tokenPreferences.currentToken(computerId)

    override suspend fun resolveTokenCandidate(
        computerId: String,
        target: com.bluetype.android.domain.ConnectionTarget,
    ): TokenCandidate? = tokenPreferences.resolveTokenCandidate(computerId, target)

    override suspend fun saveToken(computerId: String, token: String) =
        tokenPreferences.saveToken(computerId, token)

    override suspend fun commitSuccessfulMigration(
        computerId: String,
        candidate: TokenCandidate,
    ) = tokenPreferences.commitSuccessfulMigration(computerId, candidate)

    override suspend fun clearRejectedCandidate(
        computerId: String,
        candidate: TokenCandidate?,
    ) = tokenPreferences.clearRejectedCandidate(computerId, candidate)

    override suspend fun clearToken(computerId: String) = tokenPreferences.clearToken(computerId)

    override suspend fun currentPersistedSession(): PersistedSession? =
        sessionStorage.currentPersistedSession()

    suspend fun persistAuthorizedComputer(
        device: StoredDevice,
        token: String?,
        persistedSession: PersistedSession?,
        migrationCandidate: TokenCandidate? = null,
    ) = authorizedComputerPreferences.persistAuthorizedComputer(
        device = device,
        token = token,
        persistedSession = persistedSession,
        migrationCandidate = migrationCandidate,
    )

    suspend fun saveRecentDevice(device: StoredDevice) = devicePreferences.saveRecentDevice(device)

    suspend fun removeRecentDevice(device: StoredDevice) = devicePreferences.removeRecentDevice(device)

    suspend fun saveDraftText(value: String) = uiPreferences.saveDraftText(value)

    override suspend fun savePersistedSession(session: PersistedSession) =
        sessionStorage.savePersistedSession(session)

    override suspend fun clearPersistedSession() = sessionStorage.clearPersistedSession()

    override suspend fun getOrCreateDeviceId(): String = devicePreferences.getOrCreateDeviceId()
}
