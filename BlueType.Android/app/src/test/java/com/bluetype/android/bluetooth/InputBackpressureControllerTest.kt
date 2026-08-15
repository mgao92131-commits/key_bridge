package com.bluetype.android.bluetooth

import com.bluetype.android.feature.remote.*
import com.bluetype.android.domain.RemoteAction
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.test.TestScope
import kotlinx.coroutines.test.advanceTimeBy
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

@OptIn(ExperimentalCoroutinesApi::class)
class InputBackpressureControllerTest {
    @Test
    fun submit_coalescesMouseMovesWithinWindow() = runTest {
        val posted = mutableListOf<RemoteAction>()
        val controller = createController(this, posted)

        assertTrue(controller.submit(RemoteAction.MouseMove(dx = 2, dy = 3)))
        assertTrue(controller.submit(RemoteAction.MouseMove(dx = -1, dy = 4)))

        advanceTimeBy(11)
        runCurrent()
        assertEquals(emptyList<RemoteAction>(), posted)

        advanceTimeBy(1)
        runCurrent()

        assertEquals(listOf(RemoteAction.MouseMove(dx = 1, dy = 7)), posted)
    }

    @Test
    fun submit_coalescesMouseScrollWithinWindow() = runTest {
        val posted = mutableListOf<RemoteAction>()
        val controller = createController(this, posted)

        assertTrue(controller.submit(RemoteAction.MouseScroll(deltaX = 1, deltaY = -1)))
        assertTrue(controller.submit(RemoteAction.MouseScroll(deltaX = 2, deltaY = -3)))
        advanceTimeBy(12)
        runCurrent()

        assertEquals(listOf(RemoteAction.MouseScroll(deltaX = 3, deltaY = -4)), posted)
    }

    @Test
    fun submit_ignoresZeroHighFrequencyDeltas() = runTest {
        val posted = mutableListOf<RemoteAction>()
        val controller = createController(this, posted)

        assertTrue(controller.submit(RemoteAction.MouseMove(dx = 0, dy = 0)))
        assertTrue(controller.submit(RemoteAction.MouseScroll(deltaX = 0, deltaY = 0)))
        advanceTimeBy(12)
        runCurrent()

        assertEquals(emptyList<RemoteAction>(), posted)
    }

    @Test
    fun submit_returnsFalseForReliableCommands() = runTest {
        val posted = mutableListOf<RemoteAction>()
        val controller = createController(this, posted)

        assertFalse(controller.submit(RemoteAction.KeyTap("ENTER")))
        assertFalse(controller.submit(RemoteAction.ClipboardGet))
    }

    @Test
    fun flush_emitsPendingDeltasImmediately() = runTest {
        val posted = mutableListOf<RemoteAction>()
        val controller = createController(this, posted)

        controller.submit(RemoteAction.MouseMove(dx = 2, dy = 3))
        controller.submit(RemoteAction.MouseScroll(deltaX = 0, deltaY = -2))
        controller.flush()
        advanceTimeBy(12)
        runCurrent()

        assertEquals(
            listOf(
                RemoteAction.MouseMove(dx = 2, dy = 3),
                RemoteAction.MouseScroll(deltaX = 0, deltaY = -2),
            ),
            posted,
        )
    }

    private fun createController(
        scope: TestScope,
        posted: MutableList<RemoteAction>,
    ): InputBackpressureController {
        return InputBackpressureController(
            scope = scope,
            windowMs = 12,
            postAction = { action -> posted += action },
        )
    }
}
