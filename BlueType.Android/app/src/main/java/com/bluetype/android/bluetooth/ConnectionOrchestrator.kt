package com.bluetype.android.bluetooth

import android.bluetooth.BluetoothManager
import android.content.Context
import android.net.Network
import android.util.Log
import com.bluetype.android.data.PreferencesRepository
import com.bluetype.android.domain.ConnectionTarget
import com.bluetype.android.network.TcpSocketSession

internal class ConnectionOrchestrator(
    private val appContext: Context,
    private val preferencesRepository: PreferencesRepository,
) {
    suspend fun openTransport(
        target: ConnectionTarget,
        isReconnectAttempt: Boolean,
        lastBluetoothDisconnectAtMs: Long,
        preferredLanNetworkProvider: () -> Network?,
    ): OpenedTransport {
        return when (target) {
            is ConnectionTarget.Wifi -> openWifiTransport(target, preferredLanNetworkProvider)
            is ConnectionTarget.Bluetooth -> openBluetoothTransport(target, isReconnectAttempt, lastBluetoothDisconnectAtMs)
        }
    }

    private suspend fun openWifiTransport(
        target: ConnectionTarget.Wifi,
        preferredLanNetworkProvider: () -> Network?,
    ): OpenedTransport {
        val preferredNetwork = preferredLanNetworkProvider()
        Log.d(
            LOG_TAG,
            "openWifiTransport host=${target.host} port=${target.port} preferredNetwork=${preferredNetwork != null}; using default socket",
        )
        val tcpSession = TcpSocketSession(target.host, target.port, network = preferredNetwork)
        tcpSession.connect()
        return OpenedTransport(
            input = tcpSession.inputStream(),
            output = tcpSession.outputStream(),
            close = { tcpSession.close() },
        )
    }

    private suspend fun openBluetoothTransport(
        target: ConnectionTarget.Bluetooth,
        isReconnectAttempt: Boolean,
        lastBluetoothDisconnectAtMs: Long,
    ): OpenedTransport {
        val bluetoothManager = appContext.getSystemService(BluetoothManager::class.java)
            ?: throw IllegalStateException("Bluetooth manager is unavailable.")
        val adapter = bluetoothManager.adapter
            ?: throw IllegalStateException("Bluetooth is not available on this device.")
        val device = adapter.bondedDevices.firstOrNull { it.address.equals(target.address, ignoreCase = true) }
            ?: throw IllegalStateException("Paired device not found. Re-pair the Windows PC first.")

        val reconnectCooldownMs = if (isReconnectAttempt) {
            (BLUETOOTH_RECONNECT_MIN_DELAY_MS - (System.currentTimeMillis() - lastBluetoothDisconnectAtMs))
                .coerceAtLeast(0L)
        } else {
            0L
        }

        val socketSession = SocketSession(
            adapter = adapter,
            device = device,
            preferredChannel = preferencesRepository.bluetoothRfcommChannel(target.address),
            preConnectDelayMs = reconnectCooldownMs,
            candidatePauseMs = BLUETOOTH_CANDIDATE_PAUSE_MS,
        )
        socketSession.connect()
        socketSession.connectedChannel()?.let { channel ->
            preferencesRepository.saveBluetoothRfcommChannel(target.address, channel)
            Log.d(LOG_TAG, "Saved Bluetooth RFCOMM channel $channel for ${target.address}")
        }
        return OpenedTransport(
            input = socketSession.inputStream(),
            output = socketSession.outputStream(),
            close = { socketSession.close() },
        )
    }

    internal data class OpenedTransport(
        val input: java.io.InputStream,
        val output: java.io.OutputStream,
        val close: () -> Unit,
    )

    private companion object {
        private const val LOG_TAG = "BlueTypeOrchestrator"
        private const val BLUETOOTH_RECONNECT_MIN_DELAY_MS = 500L
        private const val BLUETOOTH_CANDIDATE_PAUSE_MS = 350L
    }
}
