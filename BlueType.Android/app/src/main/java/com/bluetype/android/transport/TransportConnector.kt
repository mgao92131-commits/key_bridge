package com.bluetype.android.transport

import android.net.Network
import com.bluetype.android.domain.ConnectionTarget
import java.io.InputStream
import java.io.OutputStream

internal data class OpenedTransport(
    val input: InputStream,
    val output: OutputStream,
    val close: () -> Unit,
)

/**
 * Abstraction over Wi-Fi / Bluetooth transport open so connection orchestration
 * can be tested without real sockets.
 */
internal interface TransportConnector {
    suspend fun open(
        target: ConnectionTarget,
        isReconnectAttempt: Boolean,
        lastBluetoothDisconnectAtMs: Long,
        preferredLanNetworkProvider: () -> Network?,
    ): OpenedTransport
}

internal class OrchestratorTransportConnector(
    private val orchestrator: ConnectionOrchestrator,
) : TransportConnector {
    override suspend fun open(
        target: ConnectionTarget,
        isReconnectAttempt: Boolean,
        lastBluetoothDisconnectAtMs: Long,
        preferredLanNetworkProvider: () -> Network?,
    ): OpenedTransport {
        val opened = orchestrator.openTransport(
            target = target,
            isReconnectAttempt = isReconnectAttempt,
            lastBluetoothDisconnectAtMs = lastBluetoothDisconnectAtMs,
            preferredLanNetworkProvider = preferredLanNetworkProvider,
        )
        return OpenedTransport(
            input = opened.input,
            output = opened.output,
            close = opened.close,
        )
    }
}
