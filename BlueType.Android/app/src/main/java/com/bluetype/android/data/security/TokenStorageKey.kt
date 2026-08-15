package com.bluetype.android.data.security

import com.bluetype.android.domain.model.ConnectionTarget
import java.util.Locale

internal fun ConnectionTarget.tokenStorageKey(): String {
    return when (this) {
        is ConnectionTarget.Bluetooth -> "bt:${address.trim().lowercase(Locale.US)}"
        is ConnectionTarget.Wifi -> "wifi:${host.trim().lowercase(Locale.US)}:$port"
    }
}
