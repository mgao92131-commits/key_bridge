package com.bluetype.android.feature.remote

import com.bluetype.android.domain.model.RemoteAction
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock

internal class InputBackpressureController(
    private val scope: CoroutineScope,
    private val windowMs: Long = DEFAULT_WINDOW_MS,
    private val postAction: suspend (RemoteAction) -> Unit,
) {
    private val gate = Mutex()
    private var pendingMoveDx = 0
    private var pendingMoveDy = 0
    private var pendingScrollDeltaX = 0
    private var pendingScrollDeltaY = 0
    private var moveJob: Job? = null
    private var scrollJob: Job? = null

    suspend fun submit(action: RemoteAction): Boolean {
        return when (action) {
            is RemoteAction.MouseMove -> {
                enqueueMove(action.dx, action.dy)
                true
            }

            is RemoteAction.MouseScroll -> {
                enqueueScroll(action.deltaX, action.deltaY)
                true
            }

            else -> false
        }
    }

    fun trySubmit(action: RemoteAction): Boolean {
        return when (action) {
            is RemoteAction.MouseMove -> {
                scope.launch { enqueueMove(action.dx, action.dy) }
                true
            }

            is RemoteAction.MouseScroll -> {
                scope.launch { enqueueScroll(action.deltaX, action.deltaY) }
                true
            }

            else -> false
        }
    }

    suspend fun flush() {
        val actions = gate.withLock {
            val flushed = mutableListOf<RemoteAction>()
            if (pendingMoveDx != 0 || pendingMoveDy != 0) {
                flushed += RemoteAction.MouseMove(pendingMoveDx, pendingMoveDy)
                pendingMoveDx = 0
                pendingMoveDy = 0
            }
            if (pendingScrollDeltaX != 0 || pendingScrollDeltaY != 0) {
                flushed += RemoteAction.MouseScroll(pendingScrollDeltaX, pendingScrollDeltaY)
                pendingScrollDeltaX = 0
                pendingScrollDeltaY = 0
            }
            moveJob?.cancel()
            moveJob = null
            scrollJob?.cancel()
            scrollJob = null
            flushed
        }

        actions.forEach { postAction(it) }
    }

    private suspend fun enqueueMove(dx: Int, dy: Int) {
        if (dx == 0 && dy == 0) {
            return
        }

        gate.withLock {
            pendingMoveDx += dx
            pendingMoveDy += dy
            if (moveJob?.isActive != true) {
                moveJob = scope.launch {
                    delay(windowMs)
                    emitMove()
                }
            }
        }
    }

    private suspend fun enqueueScroll(deltaX: Int, deltaY: Int) {
        if (deltaX == 0 && deltaY == 0) {
            return
        }

        gate.withLock {
            pendingScrollDeltaX += deltaX
            pendingScrollDeltaY += deltaY
            if (scrollJob?.isActive != true) {
                scrollJob = scope.launch {
                    delay(windowMs)
                    emitScroll()
                }
            }
        }
    }

    private suspend fun emitMove() {
        val action = gate.withLock {
            if (pendingMoveDx == 0 && pendingMoveDy == 0) {
                moveJob = null
                return
            }

            RemoteAction.MouseMove(pendingMoveDx, pendingMoveDy).also {
                pendingMoveDx = 0
                pendingMoveDy = 0
                moveJob = null
            }
        }

        postAction(action)
    }

    private suspend fun emitScroll() {
        val action = gate.withLock {
            if (pendingScrollDeltaX == 0 && pendingScrollDeltaY == 0) {
                scrollJob = null
                return
            }

            RemoteAction.MouseScroll(pendingScrollDeltaX, pendingScrollDeltaY).also {
                pendingScrollDeltaX = 0
                pendingScrollDeltaY = 0
                scrollJob = null
            }
        }

        postAction(action)
    }

    private companion object {
        private const val DEFAULT_WINDOW_MS = 12L
    }
}
