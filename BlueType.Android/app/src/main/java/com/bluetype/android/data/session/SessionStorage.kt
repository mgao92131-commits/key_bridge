package com.bluetype.android.data.session

import androidx.datastore.preferences.core.edit
import com.bluetype.android.data.device.withStableId
import com.bluetype.android.data.preferences.PreferencesBackingStore
import com.bluetype.android.data.session.PersistedSession
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.first

internal class SessionStorage(
    private val store: PreferencesBackingStore,
) : PersistedSessionRepository {
    fun persistedSession() = store.dataStore.data.map { prefs ->
        val session = store.decodePersistedSession(prefs[store.persistedSessionKey].orEmpty())
            ?: return@map null
        store.migratedPersistedSession(session, prefs[store.recentDevicesKey].orEmpty())
    }

    override suspend fun currentPersistedSession(): PersistedSession? {
        store.ensureMigrated()
        val prefs = store.dataStore.data.first()
        val session = store.decodePersistedSession(prefs[store.persistedSessionKey].orEmpty()) ?: return null
        return store.migratedPersistedSession(session, prefs[store.recentDevicesKey].orEmpty())
    }

    override suspend fun savePersistedSession(session: PersistedSession) {
        store.ensureMigrated()
        store.dataStore.edit { prefs ->
            prefs[store.persistedSessionKey] = store.json.encodeToString(
                PersistedSession.serializer(),
                session.copy(target = session.target.withStableId()),
            )
        }
    }

    override suspend fun clearPersistedSession() {
        store.dataStore.edit { prefs ->
            prefs.remove(store.persistedSessionKey)
        }
    }
}
