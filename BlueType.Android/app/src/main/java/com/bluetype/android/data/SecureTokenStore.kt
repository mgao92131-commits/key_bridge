package com.bluetype.android.data

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Log
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import java.security.KeyStore
import java.util.Base64
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec
import kotlinx.coroutines.flow.first

internal class SecureTokenStore(
    private val context: Context,
) {
    private val encryptedTokenKey = stringPreferencesKey("saved_token_encrypted")

    suspend fun currentToken(legacyTokenKey: Preferences.Key<String>): String? {
        val prefs = context.dataStore.data.first()
        prefs[encryptedTokenKey]?.let { encrypted ->
            return runCatching {
                decrypt(encrypted)
            }.getOrElse { error ->
                Log.w(LOG_TAG, "Failed to decrypt stored token. Clearing secure token.", error)
                clearStoredKeys(legacyTokenKey)
                null
            }
        }

        val legacyToken = prefs[legacyTokenKey]?.takeIf(String::isNotBlank) ?: return null
        migrateLegacyToken(legacyTokenKey, legacyToken)
        return legacyToken
    }

    suspend fun saveToken(legacyTokenKey: Preferences.Key<String>, token: String) {
        val encrypted = encrypt(token)
        context.dataStore.edit { prefs ->
            prefs[encryptedTokenKey] = encrypted
            prefs.remove(legacyTokenKey)
        }
    }

    suspend fun clearToken(legacyTokenKey: Preferences.Key<String>) {
        clearStoredKeys(legacyTokenKey)
    }

    private suspend fun migrateLegacyToken(legacyTokenKey: Preferences.Key<String>, token: String) {
        runCatching {
            saveToken(legacyTokenKey, token)
        }.onFailure { error ->
            Log.w(LOG_TAG, "Failed to migrate legacy token into secure storage.", error)
        }
    }

    private suspend fun clearStoredKeys(legacyTokenKey: Preferences.Key<String>) {
        context.dataStore.edit { prefs ->
            prefs.remove(encryptedTokenKey)
            prefs.remove(legacyTokenKey)
        }
    }

    private fun encrypt(token: String): String {
        val cipher = Cipher.getInstance(TRANSFORMATION)
        cipher.init(Cipher.ENCRYPT_MODE, getOrCreateSecretKey())
        val ciphertext = cipher.doFinal(token.toByteArray(Charsets.UTF_8))
        return buildString {
            append(VERSION_PREFIX)
            append(':')
            append(Base64.getEncoder().encodeToString(cipher.iv))
            append(':')
            append(Base64.getEncoder().encodeToString(ciphertext))
        }
    }

    private fun decrypt(value: String): String {
        val parts = value.split(':', limit = 3)
        require(parts.size == 3 && parts[0] == VERSION_PREFIX) { "Unsupported encrypted token format." }

        val iv = Base64.getDecoder().decode(parts[1])
        val ciphertext = Base64.getDecoder().decode(parts[2])
        val cipher = Cipher.getInstance(TRANSFORMATION)
        cipher.init(Cipher.DECRYPT_MODE, getOrCreateSecretKey(), GCMParameterSpec(GCM_TAG_LENGTH_BITS, iv))
        return cipher.doFinal(ciphertext).toString(Charsets.UTF_8)
    }

    private fun getOrCreateSecretKey(): SecretKey {
        val keyStore = KeyStore.getInstance(ANDROID_KEYSTORE).apply { load(null) }
        val existingKey = (keyStore.getEntry(KEY_ALIAS, null) as? KeyStore.SecretKeyEntry)?.secretKey
        if (existingKey != null) {
            return existingKey
        }

        val keyGenerator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, ANDROID_KEYSTORE)
        val spec = KeyGenParameterSpec.Builder(
            KEY_ALIAS,
            KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT,
        )
            .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
            .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
            .setKeySize(256)
            .build()
        keyGenerator.init(spec)
        return keyGenerator.generateKey()
    }

    private companion object {
        private const val LOG_TAG = "BlueTypeTokenStore"
        private const val ANDROID_KEYSTORE = "AndroidKeyStore"
        private const val KEY_ALIAS = "BlueType.SavedToken"
        private const val TRANSFORMATION = "AES/GCM/NoPadding"
        private const val GCM_TAG_LENGTH_BITS = 128
        private const val VERSION_PREFIX = "v1"
    }
}
