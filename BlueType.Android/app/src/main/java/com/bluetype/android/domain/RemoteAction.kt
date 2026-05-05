package com.bluetype.android.domain

sealed interface RemoteAction {
    data class TextInsert(val text: String) : RemoteAction
    data class KeyTap(val key: String) : RemoteAction
    data class KeyDown(val key: String) : RemoteAction
    data class KeyUp(val key: String) : RemoteAction
    data class Combo(val keys: List<String>) : RemoteAction
    data class StickyRelease(val generation: Long) : RemoteAction
    data class MouseMove(val dx: Int, val dy: Int) : RemoteAction
    data class MouseButton(val button: String, val isDown: Boolean) : RemoteAction
    data class MouseClick(val button: String, val repeat: Int = 1) : RemoteAction
    data class MouseScroll(val deltaX: Int = 0, val deltaY: Int = 0) : RemoteAction
    data class ClipboardSet(val text: String) : RemoteAction
    data object ClipboardGet : RemoteAction
}
