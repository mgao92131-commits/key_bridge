package com.bluetype.android.feature.connection

import java.util.concurrent.atomic.AtomicLong

/**
 * Tracks connection attempt generations so stale transport/auth callbacks cannot
 * mutate UI or activeConnection after the user switches targets.
 */
internal class ConnectionAttemptTracker {
    private val sequence = AtomicLong(0L)

    @Volatile
    var currentAttemptId: Long = 0L
        private set

    @Volatile
    var desiredComputerId: String? = null
        private set

    fun begin(computerId: String): Long {
        desiredComputerId = computerId
        val id = sequence.incrementAndGet()
        currentAttemptId = id
        return id
    }

    fun isCurrent(
        attemptId: Long,
        computerId: String,
        manualDisconnect: Boolean,
    ): Boolean {
        return !manualDisconnect &&
            currentAttemptId == attemptId &&
            desiredComputerId == computerId
    }

    fun clearDesired() {
        desiredComputerId = null
    }

    fun invalidate() {
        currentAttemptId = sequence.incrementAndGet()
    }
}
