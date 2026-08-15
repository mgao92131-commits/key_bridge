package com.bluetype.android.data.session

import com.bluetype.android.data.device.DeviceType
import com.bluetype.android.data.device.StoredDevice
import com.bluetype.android.data.device.withStableId
import com.bluetype.android.domain.model.ConnectionTarget
import com.bluetype.android.domain.model.UiRoute
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

fun ConnectionTarget.toStoredDevice(
    id: String = "",
    name: String = "",
): StoredDevice {
    val device = when (this) {
        is ConnectionTarget.Wifi -> StoredDevice(
            id = id,
            name = name.ifBlank { host },
            type = DeviceType.WIFI,
            host = host,
            port = port,
        )

        is ConnectionTarget.Bluetooth -> StoredDevice(
            id = id,
            name = name.ifBlank { this.name },
            type = DeviceType.BLUETOOTH,
            address = address,
        )
    }
    return device.withStableId()
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
