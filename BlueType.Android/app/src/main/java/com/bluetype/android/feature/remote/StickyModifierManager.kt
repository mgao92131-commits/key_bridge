package com.bluetype.android.feature.remote

import com.bluetype.android.domain.RemoteAction
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

internal class StickyModifierManager(
    private val scope: CoroutineScope,
    private val sendKeyDown: suspend (String) -> Unit,
    private val sendKeyUp: suspend (String) -> Unit,
    private val sendKeyTap: suspend (String) -> Unit,
    private val sendCombo: suspend (List<String>) -> Unit,
    private val postAction: suspend (RemoteAction) -> Unit,
    private val stickyComboDurationMs: Long = 300L,
) {
    private val stickyComboModifiers = linkedSetOf<String>()
    private val explicitHeldModifiers = linkedSetOf<String>()
    private var stickyComboReleaseJob: Job? = null
    private var stickyComboGeneration = 0L

    suspend fun flush() {
        releaseStickyComboModifiersExcept(emptySet())
    }

    suspend fun handleKeyDown(key: String) {
        val modifier = canonicalModifierOrNull(key)
        if (modifier != null) {
            releaseStickyComboModifiersExcept(setOf(modifier))
            stickyComboModifiers.remove(modifier)
            explicitHeldModifiers.add(modifier)
            refreshStickyComboRelease()
            sendKeyDown(modifier)
            return
        }

        flush()
        sendKeyDown(key)
    }

    suspend fun handleKeyUp(key: String) {
        val modifier = canonicalModifierOrNull(key)
        if (modifier != null) {
            explicitHeldModifiers.remove(modifier)
            stickyComboModifiers.remove(modifier)
            refreshStickyComboRelease()
            sendKeyUp(modifier)
            return
        }

        sendKeyUp(key)
    }

    suspend fun handleCombo(keys: List<String>) {
        if (keys.isEmpty()) {
            return
        }

        val modifiers = keys.mapNotNull(::canonicalModifierOrNull).distinct()
        val mainKeys = keys.filter { canonicalModifierOrNull(it) == null }
        if (modifiers.isEmpty()) {
            flush()
            sendCombo(keys)
            return
        }

        releaseStickyComboModifiersExcept(modifiers.toSet())

        for (modifier in modifiers) {
            if (!explicitHeldModifiers.contains(modifier) && !stickyComboModifiers.contains(modifier)) {
                sendKeyDown(modifier)
            }
        }

        if (mainKeys.isEmpty()) {
            throw IllegalStateException("Combo must include at least one non-modifier key.")
        }

        for (mainKey in mainKeys) {
            sendKeyTap(mainKey)
        }

        stickyComboModifiers.clear()
        stickyComboModifiers.addAll(modifiers.filterNot(explicitHeldModifiers::contains))
        refreshStickyComboRelease()
    }

    suspend fun handleStickyRelease(generation: Long) {
        if (generation != stickyComboGeneration) {
            return
        }

        releaseStickyComboModifiersExcept(emptySet())
    }

    fun reset() {
        stickyComboReleaseJob?.cancel()
        stickyComboReleaseJob = null
        stickyComboGeneration++
        stickyComboModifiers.clear()
        explicitHeldModifiers.clear()
    }

    private suspend fun releaseStickyComboModifiersExcept(keep: Set<String>) {
        val staleModifiers = stickyComboModifiers
            .filterNot(keep::contains)
            .filterNot(explicitHeldModifiers::contains)

        if (staleModifiers.isEmpty()) {
            refreshStickyComboRelease()
            return
        }

        for (modifier in staleModifiers) {
            sendKeyUp(modifier)
            stickyComboModifiers.remove(modifier)
        }

        refreshStickyComboRelease()
    }

    private fun refreshStickyComboRelease() {
        stickyComboReleaseJob?.cancel()
        stickyComboReleaseJob = null

        if (stickyComboModifiers.isEmpty()) {
            stickyComboGeneration++
            return
        }

        val generation = ++stickyComboGeneration
        stickyComboReleaseJob = scope.launch {
            delay(stickyComboDurationMs)
            postAction(RemoteAction.StickyRelease(generation))
        }
    }

    private fun canonicalModifierOrNull(key: String): String? {
        return when (key.trim().uppercase()) {
            "CTRL", "CONTROL" -> "CTRL"
            "ALT" -> "ALT"
            "SHIFT" -> "SHIFT"
            "CMD", "COMMAND" -> "CMD"
            "WIN", "LWIN", "RWIN" -> "WIN"
            else -> null
        }
    }
}
