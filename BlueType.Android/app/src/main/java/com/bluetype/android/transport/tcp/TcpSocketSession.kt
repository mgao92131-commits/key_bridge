package com.bluetype.android.transport.tcp

import java.io.InputStream
import java.io.OutputStream
import android.net.Network
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.Proxy
import java.net.Socket
import java.net.SocketException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

class TcpSocketSession(
    private val host: String,
    private val port: Int = 24862,
    private val network: Network? = null,
    private val localBindAddress: InetAddress? = null,
) {
    private var socket: Socket? = null

    suspend fun connect() = withContext(Dispatchers.IO) {
        val targetHost = host
        val targetPort = port
        android.util.Log.d("BlueTypeNet", "Connecting to $targetHost:$targetPort (boundNetwork=${network != null})")
        socket = createSocket().apply {
            connect(java.net.InetSocketAddress(targetHost, targetPort), CONNECT_TIMEOUT_MS)
            tcpNoDelay = true
        }
        android.util.Log.d("BlueTypeNet", "Connected to $targetHost:$targetPort")
    }

    fun inputStream(): InputStream = requireNotNull(socket).getInputStream()

    fun outputStream(): OutputStream = requireNotNull(socket).getOutputStream()

    fun close() {
        runCatching { socket?.close() }
        socket = null
    }

    private fun createSocket(): Socket {
        if (localBindAddress != null) {
            return Socket(Proxy.NO_PROXY).apply {
                bind(InetSocketAddress(localBindAddress, 0))
            }
        }

        if (network == null) {
            return Socket(Proxy.NO_PROXY)
        }

        return try {
            network.socketFactory.createSocket()
        } catch (error: SocketException) {
            android.util.Log.w("BlueTypeNet", "Failed to bind socket to preferred network; falling back to default network.", error)
            Socket(Proxy.NO_PROXY)
        } catch (error: SecurityException) {
            android.util.Log.w("BlueTypeNet", "No permission to bind socket to preferred network; falling back to default network.", error)
            Socket(Proxy.NO_PROXY)
        }
    }

    private companion object {
        private const val CONNECT_TIMEOUT_MS = 5_000
    }
}
