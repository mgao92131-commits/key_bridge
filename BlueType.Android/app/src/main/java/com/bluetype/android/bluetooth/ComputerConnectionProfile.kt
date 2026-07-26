package com.bluetype.android.bluetooth

import com.bluetype.android.domain.ConnectionTarget

data class ComputerConnectionProfile(
    val computerId: String,
    val displayName: String,
    val target: ConnectionTarget,
    val persistenceIntent: ProfilePersistenceIntent = ProfilePersistenceIntent.NEW_COMPUTER,
)
