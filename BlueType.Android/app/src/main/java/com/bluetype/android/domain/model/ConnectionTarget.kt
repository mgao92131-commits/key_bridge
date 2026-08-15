package com.bluetype.android.domain.model

sealed interface ConnectionTarget {
    val label: String

    data class Bluetooth(
        val name: String,
        val address: String,
    ) : ConnectionTarget {
        override val label: String = "$name ($address)"
    }

    data class Wifi(
        val host: String,
        val port: Int,
    ) : ConnectionTarget {
        override val label: String = "$host:$port"
    }
}
