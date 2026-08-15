package com.bluetype.android.feature.connection

import com.bluetype.android.data.PersistedSession
import com.bluetype.android.data.PersistedSessionRepository
import com.bluetype.android.data.toConnectionTarget
import com.bluetype.android.data.StoredDevice
import com.bluetype.android.domain.ConnectionTarget
import com.bluetype.android.domain.UiRoute

internal data class PersistedSessionSnapshot(
    val computer: StoredDevice,
    val target: ConnectionTarget,
    val uiRoute: UiRoute,
    val lastError: String?,
)

internal class PersistedSessionCoordinator(
    private val currentPersistedSession: suspend () -> PersistedSession?,
    private val clearPersistedSession: suspend () -> Unit,
    private val savePersistedSession: suspend (PersistedSession) -> Unit,
) {
    constructor(repository: PersistedSessionRepository) : this(
        currentPersistedSession = { repository.currentPersistedSession() },
        clearPersistedSession = { repository.clearPersistedSession() },
        savePersistedSession = { session -> repository.savePersistedSession(session) },
    )

    suspend fun hydrateSnapshot(): PersistedSessionSnapshot? {
        val persisted = currentPersistedSession() ?: return null
        val target = persisted.target.toConnectionTarget() ?: return null
        return PersistedSessionSnapshot(
            computer = persisted.target,
            target = target,
            uiRoute = persisted.uiRoute,
            lastError = persisted.lastError,
        )
    }

    suspend fun resolveRestoreProfile(
        manualDisconnect: Boolean,
        hasActiveConnection: Boolean,
        hasReconnectJob: Boolean,
    ): ComputerConnectionProfile? {
        if (manualDisconnect || hasActiveConnection || hasReconnectJob) {
            return null
        }

        val persisted = currentPersistedSession() ?: return null
        if (persisted.manuallyDisconnected || !persisted.autoRestore) {
            return null
        }

        val target = persisted.target.toConnectionTarget() ?: run {
            clearPersistedSession()
            null
        } ?: return null

        return ComputerConnectionProfile(
            computerId = persisted.target.id,
            displayName = persisted.target.name,
            target = target,
            persistenceIntent = ProfilePersistenceIntent.EXISTING_SAVED_COMPUTER,
        )
    }

    suspend fun persistSession(
        device: StoredDevice,
        lastError: String? = null,
        autoRestore: Boolean = true,
        manuallyDisconnected: Boolean = false,
    ) {
        savePersistedSession(
            PersistedSession(
                target = device,
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
