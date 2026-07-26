package com.bluetype.android.ui.viewmodel

import android.app.Application
import android.bluetooth.BluetoothManager
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.bluetype.android.bluetooth.ConnectionController
import com.bluetype.android.domain.CommandFeedback
import com.bluetype.android.domain.ConnectionState
import com.bluetype.android.domain.ConnectionTarget
import com.bluetype.android.domain.DefaultShortcutProfileFactory
import com.bluetype.android.domain.RemoteAction
import com.bluetype.android.domain.UiRoute
import com.bluetype.android.domain.ShortcutProfile
import com.bluetype.android.data.StoredDevice
import com.bluetype.android.data.DeviceType
import com.bluetype.android.data.PreferencesRepository
import com.bluetype.android.util.PermissionHelper
import kotlinx.coroutines.Job
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.filterNotNull
import kotlinx.coroutines.launch

class MainViewModel(application: Application) : AndroidViewModel(application) {
    private val connectionController = ConnectionController.getInstance(application)
    private val preferencesRepository = PreferencesRepository(application)

    val connectionState: StateFlow<ConnectionState> = connectionController.state
    val uiRoute: StateFlow<UiRoute> = connectionController.uiRoute
    val sessionTarget: StateFlow<ConnectionTarget?> = connectionController.sessionTarget
    val statusMessage: StateFlow<String?> = connectionController.statusMessage
    val remoteClipboardText: StateFlow<String?> = connectionController.remoteClipboardText
    val lastFeedback: StateFlow<CommandFeedback?> = connectionController.lastFeedback

    private val clipboardManager =
        application.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager

    private val _wifiHost = MutableStateFlow("")
    val wifiHost: StateFlow<String> = _wifiHost.asStateFlow()

    private val _wifiName = MutableStateFlow("")
    val wifiName: StateFlow<String> = _wifiName.asStateFlow()

    private val _draftText = MutableStateFlow("")
    val draftText: StateFlow<String> = _draftText.asStateFlow()

    private val _pairedBluetoothDevices = MutableStateFlow<List<ConnectionTarget.Bluetooth>>(emptyList())
    val pairedBluetoothDevices: StateFlow<List<ConnectionTarget.Bluetooth>> = _pairedBluetoothDevices.asStateFlow()

    private val _recentDevices = MutableStateFlow<List<StoredDevice>>(emptyList())
    val recentDevices: StateFlow<List<StoredDevice>> = _recentDevices.asStateFlow()

    private val _shortcutProfile = MutableStateFlow(DefaultShortcutProfileFactory.create())
    val shortcutProfile: StateFlow<ShortcutProfile> = _shortcutProfile.asStateFlow()

    private val _remoteShortcutProfileName = MutableStateFlow<String?>(null)
    val remoteShortcutProfileName: StateFlow<String?> = _remoteShortcutProfileName.asStateFlow()

    private var draftSaveJob: Job? = null

    init {
        viewModelScope.launch {
            connectionController.remoteShortcutProfile.collect { remoteProfile ->
                _shortcutProfile.value = remoteProfile?.profile ?: DefaultShortcutProfileFactory.create()
                _remoteShortcutProfileName.value = remoteProfile?.name
            }
        }
        viewModelScope.launch {
            preferencesRepository.draftText().collect { text ->
                if (_draftText.value != text) {
                    _draftText.value = text
                }
            }
        }
        viewModelScope.launch {
            connectionController.remoteClipboardText
                .filterNotNull()
                .collect { text ->
                    clipboardManager.setPrimaryClip(ClipData.newPlainText("BlueType Remote Clipboard", text))
                    setDraftText(text)
                }
        }
        viewModelScope.launch {
            connectionController.recentDevices.collect { devices ->
                _recentDevices.value = devices
            }
        }
        refreshBluetoothDevices()
    }

    fun updateWifiHost(value: String) {
        _wifiHost.value = value
    }

    fun updateWifiName(value: String) {
        _wifiName.value = value
    }

    fun updateDraft(value: String) {
        _draftText.value = value
        draftSaveJob?.cancel()
        draftSaveJob = viewModelScope.launch {
            preferencesRepository.saveDraftText(value)
        }
    }

    fun connectWifi() {
        android.util.Log.i("BlueTypeUI", "connectWifi clicked, host field value: '${_wifiHost.value}', name: '${_wifiName.value}'")
        val host = normalizeWifiHost(_wifiHost.value)
        if (host.isEmpty()) {
            android.util.Log.w("BlueTypeUI", "normalizeWifiHost returned empty string for '${_wifiHost.value}'")
            connectionController.markError("Enter a Windows host or IP first.")
            return
        }

        val existing = _recentDevices.value.firstOrNull {
            it.type == DeviceType.WIFI && it.host == host
        }
        val displayName = _wifiName.value.trim().ifBlank {
            existing?.name?.takeIf { it.isNotBlank() } ?: host
        }
        val computerId = existing?.id?.takeIf { it.isNotBlank() }
            ?: java.util.UUID.randomUUID().toString()

        viewModelScope.launch {
            android.util.Log.i("BlueTypeUI", "Initiating connection to host=$host port=24862 name=$displayName id=$computerId")
            connectionController.connect(
                com.bluetype.android.bluetooth.ComputerConnectionProfile(
                    computerId = computerId,
                    displayName = displayName,
                    target = ConnectionTarget.Wifi(host = host, port = 24862),
                )
            )
        }
    }

    private fun normalizeWifiHost(value: String): String {
        val trimmed = value.trim()
        val ipv4 = Regex("""\b(?:\d{1,3}\.){3}\d{1,3}\b""").find(trimmed)?.value
        return ipv4 ?: trimmed.substringBefore(':').trim()
    }

    fun connectDevice(device: StoredDevice) {
        val target = when (device.type) {
            DeviceType.WIFI -> ConnectionTarget.Wifi(host = device.host.orEmpty(), port = device.port ?: 24862)
            DeviceType.BLUETOOTH -> ConnectionTarget.Bluetooth(name = device.name, address = device.address.orEmpty())
        }
        viewModelScope.launch {
            connectionController.connect(
                com.bluetype.android.bluetooth.ComputerConnectionProfile(
                    computerId = device.id,
                    displayName = device.name,
                    target = target,
                )
            )
        }
    }

    fun removeRecentDevice(device: StoredDevice) {
        viewModelScope.launch {
            connectionController.removeRecentDevice(device)
        }
    }

    fun connectBluetoothPlaceholder() {
        _pairedBluetoothDevices.value.firstOrNull()?.let(::connectBluetooth)
            ?: connectionController.markError("No paired Bluetooth devices found.")
    }

    fun connectBluetooth(device: ConnectionTarget.Bluetooth) {
        viewModelScope.launch {
            val existing = _recentDevices.value.firstOrNull {
                it.type == DeviceType.BLUETOOTH && it.address == device.address
            }
            val profile = if (existing != null) {
                com.bluetype.android.bluetooth.ComputerConnectionProfile(
                    computerId = existing.id,
                    displayName = existing.name,
                    target = device,
                )
            } else {
                com.bluetype.android.bluetooth.ComputerConnectionProfile(
                    computerId = java.util.UUID.randomUUID().toString(),
                    displayName = device.name,
                    target = device,
                )
            }
            connectionController.connect(profile)
        }
    }

    fun sendText() {
        viewModelScope.launch {
            sendCurrentTextAwaitAck()
        }
    }

    fun sendTextAndEnter() {
        viewModelScope.launch {
            if (_draftText.value.isBlank()) {
                connectionController.send(RemoteAction.KeyTap("ENTER"))
            } else if (sendCurrentTextAwaitAck()) {
                connectionController.send(RemoteAction.KeyTap("ENTER"))
            }
        }
    }

    fun sendKey(key: String) {
        viewModelScope.launch {
            connectionController.send(RemoteAction.KeyTap(key))
        }
    }

    fun sendKeyDown(key: String) {
        viewModelScope.launch {
            connectionController.send(RemoteAction.KeyDown(key))
        }
    }

    fun sendKeyUp(key: String) {
        viewModelScope.launch {
            connectionController.send(RemoteAction.KeyUp(key))
        }
    }

    fun sendCombo(keys: List<String>) {
        viewModelScope.launch {
            connectionController.send(RemoteAction.Combo(keys))
        }
    }

    fun sendMouseMove(dx: Int, dy: Int) {
        if (dx == 0 && dy == 0) return
        connectionController.sendImmediate(RemoteAction.MouseMove(dx, dy))
    }

    fun sendMouseButton(button: String, isDown: Boolean) {
        connectionController.sendImmediate(RemoteAction.MouseButton(button = button, isDown = isDown))
    }

    fun sendMouseClick(button: String, repeat: Int = 1) {
        connectionController.sendImmediate(RemoteAction.MouseClick(button = button, repeat = repeat))
    }

    fun sendMouseScroll(deltaY: Int) {
        if (deltaY == 0) return
        connectionController.sendImmediate(RemoteAction.MouseScroll(deltaY = deltaY))
    }

    fun pushClipboard() {
        val text = clipboardManager.primaryClip
            ?.takeIf { it.itemCount > 0 }
            ?.getItemAt(0)
            ?.coerceToText(getApplication())
            ?.toString()
            .orEmpty()
        if (text.isBlank()) {
            connectionController.markError("Local clipboard is empty.")
            return
        }

        viewModelScope.launch {
            connectionController.send(RemoteAction.ClipboardSet(text))
        }
    }

    fun pullClipboard() {
        viewModelScope.launch {
            connectionController.send(RemoteAction.ClipboardGet)
        }
    }

    fun navigateTo(route: UiRoute) {
        connectionController.updateUiRoute(route)
    }

    fun disconnect() {
        viewModelScope.launch {
            connectionController.disconnect()
        }
    }

    fun ensureForegroundSession() {
        viewModelScope.launch {
            connectionController.ensureForegroundSession()
        }
    }

    fun refreshBluetoothDevices() {
        if (!PermissionHelper.hasRequiredPermissions(getApplication())) {
            _pairedBluetoothDevices.value = emptyList()
            return
        }

        val bluetoothManager = getApplication<Application>()
            .getSystemService(Context.BLUETOOTH_SERVICE) as? BluetoothManager
        val adapter = bluetoothManager?.adapter
        if (adapter == null) {
            _pairedBluetoothDevices.value = emptyList()
            return
        }

        _pairedBluetoothDevices.value = adapter.bondedDevices
            .map { device ->
                ConnectionTarget.Bluetooth(
                    name = device.name ?: "Unknown device",
                    address = device.address ?: "",
                )
            }
            .sortedBy { it.name.lowercase() }
    }

    private suspend fun sendCurrentTextAwaitAck(): Boolean {
        val text = _draftText.value
        if (text.isBlank()) return false

        val sent = connectionController.sendAwaitAck(RemoteAction.TextInsert(text))
        if (sent && _draftText.value == text) {
            setDraftText("")
        }
        return sent
    }

    private suspend fun setDraftText(value: String) {
        draftSaveJob?.cancel()
        _draftText.value = value
        preferencesRepository.saveDraftText(value)
    }
}
