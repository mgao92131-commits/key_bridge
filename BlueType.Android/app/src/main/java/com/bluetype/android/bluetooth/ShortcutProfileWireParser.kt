package com.bluetype.android.bluetooth

import com.bluetype.android.domain.CustomShortcutBtn
import com.bluetype.android.domain.RailConfig
import com.bluetype.android.domain.RemoteShortcutProfile
import com.bluetype.android.domain.ShortcutAction
import com.bluetype.android.domain.ShortcutProfile
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.longOrNull

internal object ShortcutProfileWireParser {
    fun parsePayload(payload: JsonObject): RemoteShortcutProfile? {
        val name = payload.string("name")?.trim()?.takeIf { it.isNotEmpty() }
        val profile = when (val rawProfile = payload["profile"]) {
            null, is JsonNull -> null
            is JsonObject -> parseProfile(rawProfile)
            else -> null
        }

        if (name == null && profile == null) {
            return null
        }

        return RemoteShortcutProfile(
            name = name,
            profile = profile,
        )
    }

    private fun parseProfile(profile: JsonObject): ShortcutProfile {
        return ShortcutProfile(
            leftRail = parseRail(requiredObject(profile, "leftRail")),
            rightRail = parseRail(requiredObject(profile, "rightRail")),
            bottomRail = parseRail(requiredObject(profile, "bottomRail")),
            customButtons = (profile["customButtons"] as? JsonArray)
                ?.mapNotNull { parseButton(it as? JsonObject) }
                ?.take(8)
                .orEmpty(),
        )
    }

    private fun parseRail(rail: JsonObject): RailConfig {
        return RailConfig(
            primaryAction = parseNullableAction(rail["primaryAction"]),
            secondaryAction = parseNullableAction(rail["secondaryAction"]),
            stickyModifiers = parseStringArray(rail["stickyModifiers"]),
            stickyDurationMs = rail.long("stickyDurationMs") ?: 600L,
        )
    }

    private fun parseButton(button: JsonObject?): CustomShortcutBtn? {
        if (button == null) return null
        val id = button.string("id") ?: return null
        val label = button.string("label") ?: return null
        val action = parseNullableAction(button["action"]) ?: return null
        return CustomShortcutBtn(id = id, label = label, action = action)
    }

    private fun parseNullableAction(element: JsonElement?): ShortcutAction? {
        if (element == null || element is JsonNull) return null
        return parseAction(element as? JsonObject ?: return null)
    }

    private fun parseAction(action: JsonObject): ShortcutAction? {
        return when (action.string("kind")) {
            "key_tap" -> action.string("key")?.let(ShortcutAction::KeyTap)
            "combo" -> ShortcutAction.Combo(parseStringArray(action["keys"]))
            "text_insert" -> action.string("text")?.let(ShortcutAction::TextInsert)
            "delay" -> ShortcutAction.Delay(action.long("ms") ?: 0L)
            "macro" -> ShortcutAction.Macro(
                (action["sequence"] as? JsonArray)
                    ?.mapNotNull { parseNullableAction(it) }
                    .orEmpty(),
            )
            else -> null
        }
    }

    private fun requiredObject(parent: JsonObject, key: String): JsonObject {
        return parent[key] as? JsonObject ?: error("Missing shortcut profile.$key.")
    }

    private fun parseStringArray(element: JsonElement?): List<String> {
        return (element as? JsonArray)
            ?.mapNotNull { item -> item.jsonPrimitive.contentOrNull?.trim()?.takeIf { it.isNotEmpty() } }
            .orEmpty()
    }

    private fun JsonObject.string(key: String): String? {
        return this[key]?.jsonPrimitive?.contentOrNull
    }

    private fun JsonObject.long(key: String): Long? {
        return this[key]?.jsonPrimitive?.longOrNull
    }
}
