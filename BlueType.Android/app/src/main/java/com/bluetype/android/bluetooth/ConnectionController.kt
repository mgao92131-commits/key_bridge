package com.bluetype.android.bluetooth

import android.content.Context
import com.bluetype.android.data.PreferencesRepository
import com.bluetype.android.domain.CommandFeedback
import com.bluetype.android.domain.CommandFeedbackState
import com.bluetype.android.domain.ConnectionState
import com.bluetype.android.domain.ConnectionTarget
import com.bluetype.android.domain.RemoteAction
import com.bluetype.android.domain.RemoteShortcutProfile
import com.bluetype.android.domain.ShortcutProfile
import com.bluetype.android.domain.UiRoute
import com.bluetype.android.data.StoredDevice
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.StateFlow

class ConnectionController private constructor(
    context: Context,
) {
    companion object {
        @Volatile
        private var instance: ConnectionController? = null

        fun getInstance(context: Context): ConnectionController {
            return instance ?: synchronized(this) {
                instance ?: ConnectionController(context.applicationContext).also { instance = it }
            }
        }
    }

    private val appContext = context.applicationContext
    private val preferencesRepository = PreferencesRepository(appContext)
    private val sessionRuntime = ConnectionSessionRuntime(appContext, preferencesRepository)

    val state: StateFlow<ConnectionState> = ConnectionUiStateStore.state
    val uiRoute: StateFlow<UiRoute> = ConnectionUiStateStore.uiRoute
    val sessionTarget: StateFlow<ConnectionTarget?> = ConnectionUiStateStore.sessionTarget
    val statusMessage: StateFlow<String?> = ConnectionUiStateStore.statusMessage
    val remoteClipboardText: StateFlow<String?> = ConnectionUiStateStore.remoteClipboardText
    val lastFeedback: StateFlow<CommandFeedback?> = ConnectionUiStateStore.lastFeedback
    val remoteShortcutProfile: StateFlow<RemoteShortcutProfile?> = ConnectionUiStateStore.remoteShortcutProfile
    val recentDevices: Flow<List<StoredDevice>> = preferencesRepository.recentDevices()

    suspend fun removeRecentDevice(device: StoredDevice) {
        preferencesRepository.removeRecentDevice(device)
    }

    suspend fun connect(profile: ComputerConnectionProfile) {
        sessionRuntime.connect(profile)
    }

    suspend fun disconnect() {
        sessionRuntime.disconnect()
    }

    suspend fun ensureForegroundSession() {
        sessionRuntime.ensureForegroundSession()
    }

    fun updateUiRoute(route: UiRoute) {
        ConnectionUiStateStore.setUiRoute(route)
    }

    suspend fun send(action: RemoteAction) {
        if (action is RemoteAction.TextInsert) {
            if (action.text.toByteArray(Charsets.UTF_8).size > 8 * 1024) {
                markError("Text payload exceeds 8 KB.")
                return
            }
        }
        sessionRuntime.send(action)
    }

    suspend fun sendAwaitAck(action: RemoteAction): Boolean {
        if (action is RemoteAction.TextInsert) {
            if (action.text.toByteArray(Charsets.UTF_8).size > 8 * 1024) {
                markError("Text payload exceeds 8 KB.")
                return false
            }
        }
        return sessionRuntime.sendAwaitAck(action)
    }

    fun sendImmediate(action: RemoteAction): Boolean {
        return sessionRuntime.trySend(action)
    }

    fun markError(message: String) {
        ConnectionUiStateStore.publishLocalIssue(
            message = message,
            feedback = CommandFeedback(
                requestId = "local",
                action = "local",
                state = CommandFeedbackState.FAILED,
                message = message,
            ),
        )
    }
}
