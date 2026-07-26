package com.bluetype.android.data

data class TokenCandidate(
    val token: String,
    val source: TokenSource,
)

sealed interface TokenSource {
    data class ComputerProfile(
        val computerId: String,
    ) : TokenSource

    data class LegacyEndpoint(
        val storageKey: String,
    ) : TokenSource

    data object LegacyGlobalEncrypted : TokenSource

    data object LegacyPlaintext : TokenSource
}
