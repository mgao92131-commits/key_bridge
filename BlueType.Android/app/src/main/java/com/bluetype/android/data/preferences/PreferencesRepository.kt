package com.bluetype.android.data.preferences

import android.content.Context
import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.Preferences
import com.bluetype.android.data.device.StoredDevice
import com.bluetype.android.data.security.EncryptedTokenStore
import com.bluetype.android.data.security.SecureTokenStore
import com.bluetype.android.data.security.TokenCandidate
import com.bluetype.android.data.session.PersistedSession
import com.bluetype.android.data.session.DeviceIdentityRepository
import com.bluetype.android.data.session.PersistedSessionRepository
import com.bluetype.android.data.session.TokenRepository

/**
 * Temporary compatibility facade for callers that still need the legacy aggregate API.
 * New code should depend on the focused stores exposed by PreferenceStores.
 */
class PreferencesRepository internal constructor(
    context: Context? = null,
    dataStore: DataStore<Preferences> = context!!.dataStore,
    secureTokenStore: EncryptedTokenStore = SecureTokenStore(context!!),
) : TokenRepository,
    DeviceIdentityRepository,
    PersistedSessionRepository {
    private val stores = PreferenceStores(context, dataStore, secureTokenStore)

    fun recentDevices() = stores.devices.recentDevices()

    fun persistedSession() = stores.sessions.persistedSession()

    fun draftText() = stores.ui.draftText()

    override suspend fun currentToken(computerId: String): String? =
        stores.tokens.currentToken(computerId)

    override suspend fun resolveTokenCandidate(
        computerId: String,
        target: com.bluetype.android.domain.model.ConnectionTarget,
    ): TokenCandidate? = stores.tokens.resolveTokenCandidate(computerId, target)

    override suspend fun saveToken(computerId: String, token: String) =
        stores.tokens.saveToken(computerId, token)

    override suspend fun commitSuccessfulMigration(
        computerId: String,
        candidate: TokenCandidate,
    ) = stores.tokens.commitSuccessfulMigration(computerId, candidate)

    override suspend fun clearRejectedCandidate(
        computerId: String,
        candidate: TokenCandidate?,
    ) = stores.tokens.clearRejectedCandidate(computerId, candidate)

    override suspend fun clearToken(computerId: String) = stores.tokens.clearToken(computerId)

    override suspend fun currentPersistedSession(): PersistedSession? =
        stores.sessions.currentPersistedSession()

    suspend fun persistAuthorizedComputer(
        device: StoredDevice,
        token: String?,
        persistedSession: PersistedSession?,
        migrationCandidate: TokenCandidate? = null,
    ) = stores.authorizedComputers.persistAuthorizedComputer(
        device = device,
        token = token,
        persistedSession = persistedSession,
        migrationCandidate = migrationCandidate,
    )

    suspend fun saveRecentDevice(device: StoredDevice) = stores.devices.saveRecentDevice(device)

    suspend fun removeRecentDevice(device: StoredDevice) = stores.devices.removeRecentDevice(device)

    suspend fun saveDraftText(value: String) = stores.ui.saveDraftText(value)

    override suspend fun savePersistedSession(session: PersistedSession) =
        stores.sessions.savePersistedSession(session)

    override suspend fun clearPersistedSession() = stores.sessions.clearPersistedSession()

    override suspend fun getOrCreateDeviceId(): String = stores.devices.getOrCreateDeviceId()
}
