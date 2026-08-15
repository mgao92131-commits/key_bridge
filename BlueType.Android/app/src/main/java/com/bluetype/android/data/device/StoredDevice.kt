package com.bluetype.android.data.device

import java.util.UUID
import kotlinx.serialization.Serializable

@Serializable
enum class DeviceType {
    WIFI, BLUETOOTH
}

@Serializable
data class StoredDevice(
    val id: String = "",
    val name: String,
    val type: DeviceType,
    val host: String? = null,
    val port: Int? = null,
    val address: String? = null,
    val lastConnectedAt: Long = 0L,
)

/**
 * Ensures legacy devices without an id get a stable, deterministic id derived from
 * endpoint identity so recent-device and persisted-session migrations converge.
 */
fun StoredDevice.withStableId(): StoredDevice {
    if (id.isNotBlank()) {
        return this
    }
    return copy(id = stableComputerIdFromEndpoint())
}

fun StoredDevice.stableComputerIdFromEndpoint(): String {
    val seed = when (type) {
        DeviceType.WIFI -> "wifi:${host.orEmpty()}:${port ?: 24862}"
        DeviceType.BLUETOOTH -> "bt:${address.orEmpty()}"
    }
    return UUID.nameUUIDFromBytes(seed.toByteArray(Charsets.UTF_8)).toString()
}
