package com.bluetype.android

import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.Surface
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.Modifier
import com.bluetype.android.domain.ConnectionState
import com.bluetype.android.domain.UiRoute
import com.bluetype.android.feature.connection.DeviceListScreen
import com.bluetype.android.feature.connection.MainViewModel
import com.bluetype.android.feature.remote.RemoteScreen
import com.bluetype.android.ui.theme.BlueTypeTheme

@Composable
fun BlueTypeApp(viewModel: MainViewModel) {
    val state by viewModel.connectionState.collectAsState()
    val uiRoute by viewModel.uiRoute.collectAsState()
    val sessionTarget by viewModel.sessionTarget.collectAsState()
    val statusMessage by viewModel.statusMessage.collectAsState()
    val connectingComputerId by viewModel.connectingComputerId.collectAsState()
    val draftText by viewModel.draftText.collectAsState()
    val pairedBluetoothDevices by viewModel.pairedBluetoothDevices.collectAsState()
    val recentDevices by viewModel.recentDevices.collectAsState()
    val wifiHost by viewModel.wifiHost.collectAsState()
    val wifiName by viewModel.wifiName.collectAsState()
    val shortcutProfile by viewModel.shortcutProfile.collectAsState()
    val remoteShortcutProfileName by viewModel.remoteShortcutProfileName.collectAsState()

    BlueTypeTheme {
        Surface(modifier = Modifier.fillMaxSize()) {
            when (uiRoute) {
                UiRoute.REMOTE_SESSION -> RemoteScreen(
                    state = state,
                    sessionTarget = sessionTarget,
                    draftText = draftText,
                    onDraftChange = viewModel::updateDraft,
                    onSendText = viewModel::sendText,
                    onSendTextAndEnter = viewModel::sendTextAndEnter,
                    onSendKey = viewModel::sendKey,
                    onSendKeyDown = viewModel::sendKeyDown,
                    onSendKeyUp = viewModel::sendKeyUp,
                    onSendCombo = viewModel::sendCombo,
                    onMouseMove = viewModel::sendMouseMove,
                    onMouseButton = viewModel::sendMouseButton,
                    onMouseClick = viewModel::sendMouseClick,
                    onMouseScroll = viewModel::sendMouseScroll,
                    onDisconnect = viewModel::disconnect,
                    onCancelConnection = viewModel::cancelConnection,
                    onRetryConnection = viewModel::retryConnection,
                    onBackToDeviceList = viewModel::backToDeviceList,
                    profile = shortcutProfile,
                    profileTitle = remoteShortcutProfileName,
                )

                UiRoute.DEVICE_LIST -> DeviceListScreen(
                    state = state,
                    statusMessage = statusMessage,
                    connectingComputerId = connectingComputerId,
                    pairedBluetoothDevices = pairedBluetoothDevices,
                    recentDevices = recentDevices,
                    wifiHost = wifiHost,
                    wifiName = wifiName,
                    onWifiHostChange = viewModel::updateWifiHost,
                    onWifiNameChange = viewModel::updateWifiName,
                    onConnectWifi = viewModel::connectWifi,
                    onConnectRecentDevice = viewModel::connectDevice,
                    onRemoveRecentDevice = viewModel::removeRecentDevice,
                    onConnectBluetooth = viewModel::connectBluetooth,
                    onRefreshBluetooth = viewModel::refreshBluetoothDevices,
                )
            }
        }
    }
}
