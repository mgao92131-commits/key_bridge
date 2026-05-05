package com.bluetype.android.bluetooth

import kotlinx.serialization.Serializable
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject

@Serializable
enum class MsgType(val wireName: String) {
    HELLO("hello"),
    AUTH_PENDING("auth_pending"),
    AUTH_RESULT("auth_result"),
    TEXT_INSERT("text_insert"),
    KEY_TAP("key_tap"),
    KEY_DOWN("key_down"),
    KEY_UP("key_up"),
    COMBO("combo"),
    MOUSE_MOVE("mouse_move"),
    MOUSE_BUTTON("mouse_button"),
    MOUSE_CLICK("mouse_click"),
    MOUSE_SCROLL("mouse_scroll"),
    CLIPBOARD_SET("clipboard_set"),
    CLIPBOARD_GET("clipboard_get"),
    CLIPBOARD_VALUE("clipboard_value"),
    SHORTCUT_PROFILE("shortcut_profile"),
    PING("ping"),
    PONG("pong"),
    ACK("ack"),
    ERROR("error");

    companion object {
        fun fromWire(type: String): MsgType? = entries.firstOrNull { it.wireName == type }
    }
}

@Serializable
data class Envelope(
    val v: Int = 1,
    val id: String,
    val type: String,
    val token: String? = null,
    val payload: JsonObject = JsonObject(emptyMap()),
)

@Serializable
data class HelloPayload(
    val deviceId: String,
    val deviceName: String,
    val appVersion: String,
)

@Serializable
data class AuthPendingPayload(
    val timeoutSec: Int,
    val message: String,
)

@Serializable
data class AuthResultPayload(
    val token: String? = null,
    val trusted: Boolean = false,
    val persistToken: Boolean = false,
)

@Serializable
data class ErrorPayload(
    val code: String,
    val message: String,
)

@Serializable
data class ClipboardValuePayload(
    val text: String,
)

val ProtocolJson = Json {
    ignoreUnknownKeys = true
    explicitNulls = false
    encodeDefaults = true
}

fun jsonObjectOf(vararg pairs: Pair<String, JsonElement>): JsonObject = JsonObject(mapOf(*pairs))
