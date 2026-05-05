package com.bluetype.android.data

internal interface TokenRepository {
    suspend fun currentToken(): String?

    suspend fun saveToken(token: String)

    suspend fun clearToken()
}

internal interface DeviceIdentityRepository {
    suspend fun getOrCreateDeviceId(): String
}

internal interface PersistedSessionRepository {
    suspend fun currentPersistedSession(): PersistedSession?

    suspend fun savePersistedSession(session: PersistedSession)

    suspend fun clearPersistedSession()
}
