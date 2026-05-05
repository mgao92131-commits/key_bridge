package com.bluetype.android.bluetooth

import com.bluetype.android.domain.CommandFeedback
import com.bluetype.android.domain.ConnectionState
import com.bluetype.android.domain.ConnectionTarget
import com.bluetype.android.domain.RemoteShortcutProfile
import com.bluetype.android.domain.UiRoute
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

object ConnectionUiStateStore {
    private val _state = MutableStateFlow<ConnectionState>(ConnectionState.Idle)
    val state: StateFlow<ConnectionState> = _state.asStateFlow()

    private val _statusMessage = MutableStateFlow<String?>(null)
    val statusMessage: StateFlow<String?> = _statusMessage.asStateFlow()

    private val _uiRoute = MutableStateFlow(UiRoute.DEVICE_LIST)
    val uiRoute: StateFlow<UiRoute> = _uiRoute.asStateFlow()

    private val _sessionTarget = MutableStateFlow<ConnectionTarget?>(null)
    val sessionTarget: StateFlow<ConnectionTarget?> = _sessionTarget.asStateFlow()

    private val _remoteClipboardText = MutableStateFlow<String?>(null)
    val remoteClipboardText: StateFlow<String?> = _remoteClipboardText.asStateFlow()

    private val _lastFeedback = MutableStateFlow<CommandFeedback?>(null)
    val lastFeedback: StateFlow<CommandFeedback?> = _lastFeedback.asStateFlow()

    private val _remoteShortcutProfile = MutableStateFlow<RemoteShortcutProfile?>(null)
    val remoteShortcutProfile: StateFlow<RemoteShortcutProfile?> = _remoteShortcutProfile.asStateFlow()

    fun setState(value: ConnectionState) {
        _state.value = value
    }

    fun setStatus(message: String?) {
        _statusMessage.value = message
    }

    fun setUiRoute(route: UiRoute) {
        _uiRoute.value = route
    }

    fun setSessionTarget(target: ConnectionTarget?) {
        _sessionTarget.value = target
    }

    fun showRemoteSession(target: ConnectionTarget) {
        _sessionTarget.value = target
        _uiRoute.value = UiRoute.REMOTE_SESSION
    }

    fun setRemoteClipboardText(text: String?) {
        _remoteClipboardText.value = text
    }

    fun setFeedback(feedback: CommandFeedback?) {
        _lastFeedback.value = feedback
    }

    fun setRemoteShortcutProfile(profile: RemoteShortcutProfile?) {
        _remoteShortcutProfile.value = profile
    }

    fun publishLocalIssue(message: String, feedback: CommandFeedback? = null) {
        _statusMessage.value = message
        if (feedback != null) {
            _lastFeedback.value = feedback
        }
    }

    fun clearForManualDisconnect() {
        _state.value = ConnectionState.Idle
        _uiRoute.value = UiRoute.DEVICE_LIST
        _sessionTarget.value = null
        _statusMessage.value = null
        _remoteClipboardText.value = null
        _lastFeedback.value = null
        _remoteShortcutProfile.value = null
    }
}
