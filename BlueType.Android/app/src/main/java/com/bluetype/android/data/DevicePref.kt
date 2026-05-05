package com.bluetype.android.data

import kotlinx.serialization.Serializable

@Serializable
data class DevicePref(
    val deviceId: String,
    val displayName: String,
    val lastTransport: String,
    val bluetoothAddress: String? = null,
    val host: String? = null,
    val port: Int? = null,
    val token: String? = null,
)
