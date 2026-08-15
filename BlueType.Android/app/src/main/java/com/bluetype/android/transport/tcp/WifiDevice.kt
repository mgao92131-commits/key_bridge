package com.bluetype.android.transport.tcp

import kotlinx.serialization.Serializable

@Serializable
data class WifiDevice(
    val name: String,
    val host: String,
    val port: Int = 24862,
)
