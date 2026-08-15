package com.bluetype.android.feature.remote

import com.bluetype.android.domain.model.RemoteAction
import com.bluetype.android.protocol.*
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonArray
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put

internal data class EncodedRemoteCommand(
    val action: String,
    val payload: kotlinx.serialization.json.JsonObject,
    val type: MsgType,
    val trackFeedback: Boolean = true,
)

internal object RemoteActionEncoder {
    fun encode(action: RemoteAction): EncodedRemoteCommand? {
        return when (action) {
            is RemoteAction.TextInsert -> EncodedRemoteCommand(
                action = "TEXT_INSERT",
                payload = buildJsonObject { put("text", action.text) },
                type = MsgType.TEXT_INSERT,
            )

            is RemoteAction.KeyTap -> EncodedRemoteCommand(
                action = "KEY_TAP",
                payload = buildJsonObject { put("key", action.key) },
                type = MsgType.KEY_TAP,
                trackFeedback = false,
            )

            is RemoteAction.KeyDown -> EncodedRemoteCommand(
                action = "KEY_DOWN",
                payload = buildJsonObject { put("key", action.key) },
                type = MsgType.KEY_DOWN,
                trackFeedback = false,
            )

            is RemoteAction.KeyUp -> EncodedRemoteCommand(
                action = "KEY_UP",
                payload = buildJsonObject { put("key", action.key) },
                type = MsgType.KEY_UP,
                trackFeedback = false,
            )

            is RemoteAction.Combo -> EncodedRemoteCommand(
                action = "COMBO",
                payload = buildJsonObject {
                    put(
                        "keys",
                        buildJsonArray {
                            action.keys.forEach { add(JsonPrimitive(it)) }
                        },
                    )
                },
                type = MsgType.COMBO,
            )

            is RemoteAction.MouseMove -> EncodedRemoteCommand(
                action = "MOUSE_MOVE",
                payload = buildJsonObject {
                    put("dx", action.dx)
                    put("dy", action.dy)
                },
                type = MsgType.MOUSE_MOVE,
                trackFeedback = false,
            )

            is RemoteAction.MouseButton -> EncodedRemoteCommand(
                action = "MOUSE_BUTTON",
                payload = buildJsonObject {
                    put("button", action.button)
                    put("action", if (action.isDown) "down" else "up")
                },
                type = MsgType.MOUSE_BUTTON,
                trackFeedback = false,
            )

            is RemoteAction.MouseClick -> EncodedRemoteCommand(
                action = "MOUSE_CLICK",
                payload = buildJsonObject {
                    put("button", action.button)
                    put("repeat", action.repeat)
                },
                type = MsgType.MOUSE_CLICK,
                trackFeedback = false,
            )

            is RemoteAction.MouseScroll -> EncodedRemoteCommand(
                action = "MOUSE_SCROLL",
                payload = buildJsonObject {
                    put("deltaX", action.deltaX)
                    put("deltaY", action.deltaY)
                },
                type = MsgType.MOUSE_SCROLL,
                trackFeedback = false,
            )

            is RemoteAction.ClipboardSet -> EncodedRemoteCommand(
                action = "CLIPBOARD_SET",
                payload = buildJsonObject { put("text", action.text) },
                type = MsgType.CLIPBOARD_SET,
            )

            RemoteAction.ClipboardGet -> EncodedRemoteCommand(
                action = "CLIPBOARD_GET",
                payload = buildJsonObject { },
                type = MsgType.CLIPBOARD_GET,
            )

            is RemoteAction.StickyRelease -> null
        }
    }
}
