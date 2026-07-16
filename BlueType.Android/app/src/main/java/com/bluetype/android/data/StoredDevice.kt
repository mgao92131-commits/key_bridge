package com.bluetype.android.data

import kotlinx.serialization.Serializable

@Serializable
enum class DeviceType {
    WIFI, BLUETOOTH
}

@Serializable
data class StoredDevice(
    val name: String,
    val type: DeviceType,
    val host: String? = null,
    val port: Int? = null,
    val address: String? = null,
)
