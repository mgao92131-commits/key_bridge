package com.bluetype.android.data

import com.bluetype.android.domain.ConnectionTarget

internal interface TokenRepository {
    suspend fun currentToken(computerId: String): String?

    suspend fun saveToken(computerId: String, token: String)

    suspend fun clearToken(computerId: String)

    suspend fun getAndMigrateToken(computerId: String, target: ConnectionTarget): String?

    suspend fun clearOldGlobalToken()
}

internal interface DeviceIdentityRepository {
    suspend fun getOrCreateDeviceId(): String
}

internal interface PersistedSessionRepository {
    suspend fun currentPersistedSession(): PersistedSession?

    suspend fun savePersistedSession(session: PersistedSession)

    suspend fun clearPersistedSession()
}
