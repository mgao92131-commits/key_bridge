package com.bluetype.android.network

import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableStateFlow

class WifiConnectionRepository {
    private val recentDevices = MutableStateFlow<List<WifiDevice>>(emptyList())

    fun recentDevices(): Flow<List<WifiDevice>> = recentDevices

    fun save(device: WifiDevice) {
        recentDevices.value = (listOf(device) + recentDevices.value.filterNot { it.host == device.host && it.port == device.port })
            .take(5)
    }
}
