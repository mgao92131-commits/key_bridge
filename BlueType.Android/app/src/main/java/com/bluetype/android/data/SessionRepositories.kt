package com.bluetype.android.data

import com.bluetype.android.domain.ConnectionTarget

internal interface TokenRepository {
    suspend fun currentToken(target: ConnectionTarget): String?

    suspend fun saveToken(target: ConnectionTarget, token: String)

    suspend fun clearToken(target: ConnectionTarget)
}

internal interface DeviceIdentityRepository {
    suspend fun getOrCreateDeviceId(): String
}

internal interface PersistedSessionRepository {
    suspend fun currentPersistedSession(): PersistedSession?

    suspend fun savePersistedSession(session: PersistedSession)

    suspend fun clearPersistedSession()
}
