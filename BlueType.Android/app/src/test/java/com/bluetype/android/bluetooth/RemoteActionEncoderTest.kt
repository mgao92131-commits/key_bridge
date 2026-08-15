package com.bluetype.android.bluetooth

import com.bluetype.android.feature.remote.*
import com.bluetype.android.protocol.*
import com.bluetype.android.domain.RemoteAction
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonPrimitive
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class RemoteActionEncoderTest {
    @Test
    fun textInsert_encodesTrackedCommand() {
        val command = requireEncoded(RemoteAction.TextInsert("hello"))

        assertEquals("TEXT_INSERT", command.action)
        assertEquals(MsgType.TEXT_INSERT, command.type)
        assertEquals("hello", command.payload["text"]?.jsonPrimitive?.content)
        assertTrue(command.trackFeedback)
    }

    @Test
    fun keyActions_encodeKeyboardPayloads() {
        val tap = requireEncoded(RemoteAction.KeyTap("ENTER"))
        val down = requireEncoded(RemoteAction.KeyDown("CTRL"))
        val up = requireEncoded(RemoteAction.KeyUp("CTRL"))
        val combo = requireEncoded(RemoteAction.Combo(listOf("CTRL", "C")))

        assertEquals(MsgType.KEY_TAP, tap.type)
        assertEquals("ENTER", tap.payload["key"]?.jsonPrimitive?.content)
        assertFalse(tap.trackFeedback)

        assertEquals(MsgType.KEY_DOWN, down.type)
        assertEquals("CTRL", down.payload["key"]?.jsonPrimitive?.content)
        assertFalse(down.trackFeedback)

        assertEquals(MsgType.KEY_UP, up.type)
        assertEquals("CTRL", up.payload["key"]?.jsonPrimitive?.content)
        assertFalse(up.trackFeedback)

        assertEquals(MsgType.COMBO, combo.type)
        assertEquals(listOf("CTRL", "C"), combo.payload["keys"]?.jsonArray?.map { it.jsonPrimitive.content })
        assertTrue(combo.trackFeedback)
    }

    @Test
    fun mouseActions_encodeUntrackedPayloads() {
        val move = requireEncoded(RemoteAction.MouseMove(dx = 4, dy = -3))
        val button = requireEncoded(RemoteAction.MouseButton(button = "LEFT", isDown = true))
        val click = requireEncoded(RemoteAction.MouseClick(button = "RIGHT", repeat = 2))
        val scroll = requireEncoded(RemoteAction.MouseScroll(deltaX = 1, deltaY = -2))

        assertEquals(MsgType.MOUSE_MOVE, move.type)
        assertEquals("4", move.payload["dx"]?.jsonPrimitive?.content)
        assertEquals("-3", move.payload["dy"]?.jsonPrimitive?.content)
        assertFalse(move.trackFeedback)

        assertEquals(MsgType.MOUSE_BUTTON, button.type)
        assertEquals("LEFT", button.payload["button"]?.jsonPrimitive?.content)
        assertEquals("down", button.payload["action"]?.jsonPrimitive?.content)
        assertFalse(button.trackFeedback)

        assertEquals(MsgType.MOUSE_CLICK, click.type)
        assertEquals("RIGHT", click.payload["button"]?.jsonPrimitive?.content)
        assertEquals("2", click.payload["repeat"]?.jsonPrimitive?.content)
        assertFalse(click.trackFeedback)

        assertEquals(MsgType.MOUSE_SCROLL, scroll.type)
        assertEquals("1", scroll.payload["deltaX"]?.jsonPrimitive?.content)
        assertEquals("-2", scroll.payload["deltaY"]?.jsonPrimitive?.content)
        assertFalse(scroll.trackFeedback)
    }

    @Test
    fun clipboardActions_encodeTrackedCommands() {
        val set = requireEncoded(RemoteAction.ClipboardSet("copy"))
        val get = requireEncoded(RemoteAction.ClipboardGet)

        assertEquals(MsgType.CLIPBOARD_SET, set.type)
        assertEquals("copy", set.payload["text"]?.jsonPrimitive?.content)
        assertTrue(set.trackFeedback)

        assertEquals(MsgType.CLIPBOARD_GET, get.type)
        assertTrue(get.payload.isEmpty())
        assertTrue(get.trackFeedback)
    }

    @Test
    fun stickyRelease_isNotWireCommand() {
        assertNull(RemoteActionEncoder.encode(RemoteAction.StickyRelease(generation = 1)))
    }

    private fun requireEncoded(action: RemoteAction): EncodedRemoteCommand {
        return checkNotNull(RemoteActionEncoder.encode(action))
    }
}
