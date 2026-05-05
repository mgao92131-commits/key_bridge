package com.bluetype.android.bluetooth

import com.bluetype.android.domain.RemoteAction
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.test.TestScope
import kotlinx.coroutines.test.advanceTimeBy
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

@OptIn(ExperimentalCoroutinesApi::class)
class StickyModifierManagerTest {
    @Test
    fun combo_postsStickyRelease_andReleaseActionSendsModifierUp() = runTest {
        val recorder = EventRecorder()
        val manager = createManager(this, recorder, stickyComboDurationMs = 300L)

        manager.handleCombo(listOf("CMD", "C"))

        assertEquals(listOf("down:CMD", "tap:C"), recorder.events)

        advanceTimeBy(300L)
        runCurrent()

        assertEquals(1, recorder.postedActions.size)
        val release = recorder.postedActions.single() as RemoteAction.StickyRelease

        manager.handleStickyRelease(release.generation)

        assertEquals(listOf("down:CMD", "tap:C", "up:CMD"), recorder.events)
    }

    @Test
    fun explicitModifier_doesNotBecomeSticky_forCombo() = runTest {
        val recorder = EventRecorder()
        val manager = createManager(this, recorder, stickyComboDurationMs = 300L)

        manager.handleKeyDown("CTRL")
        manager.handleCombo(listOf("CTRL", "C"))
        manager.handleKeyUp("CTRL")
        advanceTimeBy(300L)
        runCurrent()

        assertEquals(listOf("down:CTRL", "tap:C", "up:CTRL"), recorder.events)
        assertTrue(recorder.postedActions.isEmpty())
    }

    private fun createManager(
        scope: TestScope,
        recorder: EventRecorder,
        stickyComboDurationMs: Long,
    ): StickyModifierManager {
        return StickyModifierManager(
            scope = scope,
            sendKeyDown = { key -> recorder.events += "down:$key" },
            sendKeyUp = { key -> recorder.events += "up:$key" },
            sendKeyTap = { key -> recorder.events += "tap:$key" },
            sendCombo = { keys -> recorder.events += "combo:${keys.joinToString("+")}" },
            postAction = { action -> recorder.postedActions += action },
            stickyComboDurationMs = stickyComboDurationMs,
        )
    }

    private class EventRecorder {
        val events = mutableListOf<String>()
        val postedActions = mutableListOf<RemoteAction>()
    }
}
