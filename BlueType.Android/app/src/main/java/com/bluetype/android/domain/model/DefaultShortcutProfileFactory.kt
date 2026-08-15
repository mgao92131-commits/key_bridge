package com.bluetype.android.domain.model

object DefaultShortcutProfileFactory {
    fun create(): ShortcutProfile {
        return ShortcutProfile(
            leftRail = RailConfig(
                primaryAction = ShortcutAction.Combo(listOf("SHIFT", "TAB")),
                secondaryAction = ShortcutAction.KeyTap("TAB"),
                stickyModifiers = listOf("ALT"),
            ),
            rightRail = RailConfig(
                primaryAction = ShortcutAction.Combo(listOf("SHIFT", "TAB")),
                secondaryAction = ShortcutAction.KeyTap("TAB"),
                stickyModifiers = listOf("CTRL"),
            ),
            bottomRail = RailConfig(
                primaryAction = ShortcutAction.KeyTap("LEFT"),
                secondaryAction = ShortcutAction.KeyTap("RIGHT"),
                stickyModifiers = listOf("WIN", "CTRL"),
            ),
            customButtons = listOf(
                CustomShortcutBtn("copy", "COPY", ShortcutAction.Combo(listOf("CMD", "C"))),
                CustomShortcutBtn("paste", "PASTE", ShortcutAction.Combo(listOf("CMD", "V"))),
                CustomShortcutBtn("cut", "CUT", ShortcutAction.Combo(listOf("CMD", "X"))),
                CustomShortcutBtn("undo", "UNDO", ShortcutAction.Combo(listOf("CMD", "Z"))),
                CustomShortcutBtn("redo", "REDO", ShortcutAction.Combo(listOf("CMD", "SHIFT", "Z"))),
                CustomShortcutBtn("all", "ALL", ShortcutAction.Combo(listOf("CMD", "A"))),
                CustomShortcutBtn("save", "SAVE", ShortcutAction.Combo(listOf("CMD", "S"))),
                CustomShortcutBtn("find", "FIND", ShortcutAction.Combo(listOf("CMD", "F"))),
            ),
        )
    }
}
