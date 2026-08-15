package com.bluetype.android.transport

import android.bluetooth.BluetoothManager
import android.net.ConnectivityManager
import android.content.Context
import android.net.Network
import android.util.Log
import com.bluetype.android.domain.model.ConnectionTarget
import com.bluetype.android.transport.bluetooth.SocketSession
import com.bluetype.android.transport.tcp.TcpSocketSession
import java.net.Inet4Address
import java.net.InetAddress

internal class ConnectionOrchestrator(
    private val appContext: Context,
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
            "openWifiTransport host=${target.host} port=${target.port} preferredNetwork=${preferredNetwork != null}",
        )
        val tcpSession = TcpSocketSession(
            host = target.host,
            port = target.port,
            network = preferredNetwork,
            localBindAddress = preferredNetwork?.let(::wifiIpv4Address),
        )
        tcpSession.connect()
        return OpenedTransport(
            input = tcpSession.inputStream(),
            output = tcpSession.outputStream(),
            close = { tcpSession.close() },
        )
    }

    private fun wifiIpv4Address(network: Network): InetAddress? {
        val connectivityManager = appContext.getSystemService(ConnectivityManager::class.java) ?: return null
        return connectivityManager.getLinkProperties(network)
            ?.linkAddresses
            ?.asSequence()
            ?.map { it.address }
            ?.filterIsInstance<Inet4Address>()
            ?.firstOrNull { !it.isLoopbackAddress }
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
            ?: throw IllegalStateException("Paired device not found. Re-pair the desktop first.")

        val reconnectCooldownMs = if (isReconnectAttempt) {
            (BLUETOOTH_RECONNECT_MIN_DELAY_MS - (System.currentTimeMillis() - lastBluetoothDisconnectAtMs))
                .coerceAtLeast(0L)
        } else {
            0L
        }

        val socketSession = SocketSession(
            adapter = adapter,
            device = device,
            preConnectDelayMs = reconnectCooldownMs,
            candidatePauseMs = BLUETOOTH_CANDIDATE_PAUSE_MS,
        )
        socketSession.connect()
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
