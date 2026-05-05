package com.bluetype.android.data

import com.bluetype.android.domain.ConnectionTarget
import com.bluetype.android.domain.UiRoute
import kotlinx.serialization.Serializable

@Serializable
data class PersistedSession(
    val target: StoredDevice,
    val uiRoute: UiRoute = UiRoute.REMOTE_SESSION,
    val autoRestore: Boolean = true,
    val manuallyDisconnected: Boolean = false,
    val lastError: String? = null,
    val updatedAt: Long = System.currentTimeMillis(),
)

fun ConnectionTarget.toStoredDevice(): StoredDevice {
    return when (this) {
        is ConnectionTarget.Wifi -> StoredDevice(
            name = host,
            type = DeviceType.WIFI,
            host = host,
            port = port,
        )

        is ConnectionTarget.Bluetooth -> StoredDevice(
            name = name,
            type = DeviceType.BLUETOOTH,
            address = address,
        )
    }
}

fun StoredDevice.toConnectionTarget(): ConnectionTarget? {
    return when (type) {
        DeviceType.WIFI -> {
            val currentHost = host?.takeIf { it.isNotBlank() } ?: return null
            ConnectionTarget.Wifi(
                host = currentHost,
                port = port ?: 24862,
            )
        }

        DeviceType.BLUETOOTH -> {
            val currentAddress = address?.takeIf { it.isNotBlank() } ?: return null
            ConnectionTarget.Bluetooth(
                name = name,
                address = currentAddress,
            )
        }
    }
}
