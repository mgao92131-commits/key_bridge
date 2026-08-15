package com.bluetype.android.data.session

import com.bluetype.android.domain.model.ConnectionTarget
import com.bluetype.android.data.security.TokenCandidate

internal interface TokenRepository {
    suspend fun currentToken(computerId: String): String?

    suspend fun resolveTokenCandidate(
        computerId: String,
        target: ConnectionTarget,
    ): TokenCandidate?

    suspend fun saveToken(
        computerId: String,
        token: String,
    )

    suspend fun commitSuccessfulMigration(
        computerId: String,
        candidate: TokenCandidate,
    )

    suspend fun clearRejectedCandidate(
        computerId: String,
        candidate: TokenCandidate?,
    )

    suspend fun clearToken(computerId: String)
}

internal interface DeviceIdentityRepository {
    suspend fun getOrCreateDeviceId(): String
}

internal interface PersistedSessionRepository {
    suspend fun currentPersistedSession(): PersistedSession?

    suspend fun savePersistedSession(session: PersistedSession)

    suspend fun clearPersistedSession()
}
