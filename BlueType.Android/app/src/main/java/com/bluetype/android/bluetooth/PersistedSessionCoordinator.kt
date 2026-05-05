package com.bluetype.android.bluetooth

import com.bluetype.android.data.PersistedSession
import com.bluetype.android.data.PersistedSessionRepository
import com.bluetype.android.data.PreferencesRepository
import com.bluetype.android.data.toConnectionTarget
import com.bluetype.android.data.toStoredDevice
import com.bluetype.android.domain.ConnectionTarget
import com.bluetype.android.domain.UiRoute

internal data class PersistedSessionSnapshot(
    val target: ConnectionTarget,
    val uiRoute: UiRoute,
    val lastError: String?,
)

internal class PersistedSessionCoordinator(
    private val currentPersistedSession: suspend () -> PersistedSession?,
    private val clearPersistedSession: suspend () -> Unit,
    private val savePersistedSession: suspend (PersistedSession) -> Unit,
) {
    constructor(preferencesRepository: PreferencesRepository) : this(
        currentPersistedSession = { preferencesRepository.currentPersistedSession() },
        clearPersistedSession = { preferencesRepository.clearPersistedSession() },
        savePersistedSession = { session -> preferencesRepository.savePersistedSession(session) },
    )

    constructor(repository: PersistedSessionRepository) : this(
        currentPersistedSession = { repository.currentPersistedSession() },
        clearPersistedSession = { repository.clearPersistedSession() },
        savePersistedSession = { session -> repository.savePersistedSession(session) },
    )

    suspend fun hydrateSnapshot(): PersistedSessionSnapshot? {
        val persisted = currentPersistedSession() ?: return null
        val target = persisted.target.toConnectionTarget() ?: return null
        return PersistedSessionSnapshot(
            target = target,
            uiRoute = persisted.uiRoute,
            lastError = persisted.lastError,
        )
    }

    suspend fun resolveRestoreTarget(
        manualDisconnect: Boolean,
        hasActiveConnection: Boolean,
        hasReconnectJob: Boolean,
    ): ConnectionTarget? {
        if (manualDisconnect || hasActiveConnection || hasReconnectJob) {
            return null
        }

        val persisted = currentPersistedSession() ?: return null
        if (persisted.manuallyDisconnected || !persisted.autoRestore) {
            return null
        }

        return persisted.target.toConnectionTarget() ?: run {
            clearPersistedSession()
            null
        }
    }

    suspend fun persistSession(
        target: ConnectionTarget,
        lastError: String? = null,
        autoRestore: Boolean = true,
        manuallyDisconnected: Boolean = false,
    ) {
        savePersistedSession(
            PersistedSession(
                target = target.toStoredDevice(),
                uiRoute = UiRoute.REMOTE_SESSION,
                autoRestore = autoRestore,
                manuallyDisconnected = manuallyDisconnected,
                lastError = lastError,
            ),
        )
    }

    suspend fun persistLastError(message: String) {
        val persisted = currentPersistedSession() ?: return
        savePersistedSession(
            persisted.copy(
                lastError = message,
                updatedAt = System.currentTimeMillis(),
            ),
        )
    }
}
