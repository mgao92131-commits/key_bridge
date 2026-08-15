package com.bluetype.android.domain.model

import kotlinx.serialization.Serializable

@Serializable
sealed interface ShortcutAction {
    @Serializable
    data class KeyTap(val key: String) : ShortcutAction
    @Serializable
    data class Combo(val keys: List<String>) : ShortcutAction
    @Serializable
    data class TextInsert(val text: String) : ShortcutAction
    @Serializable
    data class Macro(val sequence: List<ShortcutAction>) : ShortcutAction
    @Serializable
    data class Delay(val ms: Long) : ShortcutAction
}

@Serializable
data class CustomShortcutBtn(
    val id: String,
    val label: String,
    val action: ShortcutAction
)

@Serializable
data class RailConfig(
    val primaryAction: ShortcutAction?,
    val secondaryAction: ShortcutAction?,
    val stickyModifiers: List<String>,
    val stickyDurationMs: Long = 600L
)

@Serializable
data class ShortcutProfile(
    val leftRail: RailConfig,
    val rightRail: RailConfig,
    val bottomRail: RailConfig,
    val customButtons: List<CustomShortcutBtn>
)
