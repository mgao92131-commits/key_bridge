package com.bluetype.android.domain

import com.bluetype.android.domain.model.DefaultShortcutProfileFactory
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class DefaultShortcutProfileFactoryTest {
    @Test
    fun create_returnsExpectedDefaultShortcutButtons() {
        val profile = DefaultShortcutProfileFactory.create()

        assertEquals(listOf("ALT"), profile.leftRail.stickyModifiers)
        assertEquals(listOf("CTRL"), profile.rightRail.stickyModifiers)
        assertEquals(listOf("WIN", "CTRL"), profile.bottomRail.stickyModifiers)
        assertEquals(8, profile.customButtons.size)
        assertTrue(profile.customButtons.any { it.id == "copy" && it.label == "COPY" })
        assertTrue(profile.customButtons.any { it.id == "find" && it.label == "FIND" })
    }
}
