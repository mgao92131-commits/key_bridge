package com.bluetype.android.bluetooth

import com.bluetype.android.data.DeviceType
import com.bluetype.android.data.StoredDevice
import com.bluetype.android.domain.ConnectionTarget
import java.util.UUID

enum class ProfilePersistenceIntent {
    EXISTING_SAVED_COMPUTER,
    NEW_COMPUTER,
}

internal object ComputerProfileFactory {
    fun createNewWifiProfile(
        displayName: String,
        host: String,
        port: Int = 24862,
        idGenerator: () -> String = { UUID.randomUUID().toString() },
    ): ComputerConnectionProfile {
        val trimmedName = displayName.trim()
        return ComputerConnectionProfile(
            computerId = idGenerator(),
            displayName = trimmedName.ifBlank { host },
            target = ConnectionTarget.Wifi(host = host, port = port),
            persistenceIntent = ProfilePersistenceIntent.NEW_COMPUTER,
        )
    }

    fun createNewBluetoothProfile(
        displayName: String,
        address: String,
        idGenerator: () -> String = { UUID.randomUUID().toString() },
    ): ComputerConnectionProfile {
        return ComputerConnectionProfile(
            computerId = idGenerator(),
            displayName = displayName,
            target = ConnectionTarget.Bluetooth(name = displayName, address = address),
            persistenceIntent = ProfilePersistenceIntent.NEW_COMPUTER,
        )
    }

    fun createFromSavedDevice(device: StoredDevice): ComputerConnectionProfile {
        val target = when (device.type) {
            DeviceType.WIFI -> ConnectionTarget.Wifi(
                host = device.host.orEmpty(),
                port = device.port ?: 24862,
            )
            DeviceType.BLUETOOTH -> ConnectionTarget.Bluetooth(
                name = device.name,
                address = device.address.orEmpty(),
            )
        }
        return ComputerConnectionProfile(
            computerId = device.id,
            displayName = device.name,
            target = target,
            persistenceIntent = ProfilePersistenceIntent.EXISTING_SAVED_COMPUTER,
        )
    }
}
