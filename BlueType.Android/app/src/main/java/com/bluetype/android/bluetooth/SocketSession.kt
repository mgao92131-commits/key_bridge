package com.bluetype.android.bluetooth

import android.annotation.SuppressLint
import android.bluetooth.BluetoothAdapter
import android.bluetooth.BluetoothDevice
import android.bluetooth.BluetoothSocket
import com.bluetype.android.util.UuidConst
import android.util.Log
import java.io.InputStream
import java.io.IOException
import java.io.OutputStream
import java.util.concurrent.ExecutionException
import java.util.concurrent.Executors
import java.util.concurrent.ThreadFactory
import java.util.concurrent.TimeUnit
import java.util.concurrent.TimeoutException
import java.util.concurrent.atomic.AtomicReference
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.withContext

class SocketSession(
    private val adapter: BluetoothAdapter,
    private val device: BluetoothDevice,
    private val preConnectDelayMs: Long = 0L,
    private val candidatePauseMs: Long = 250L,
    private val candidateConnectTimeoutMs: Long = CANDIDATE_CONNECT_TIMEOUT_MS,
) {
    private var socket: BluetoothSocket? = null

    @SuppressLint("MissingPermission")
    suspend fun connect() = withContext(Dispatchers.IO) {
        adapter.cancelDiscovery()
        if (preConnectDelayMs > 0L) {
            delay(preConnectDelayMs)
        }

        var lastError: Throwable? = null
        var lastCandidateLabel = "unknown"
        for (candidate in socketCandidates()) {
            try {
                Log.d(LOG_TAG, "Trying Bluetooth socket candidate ${candidate.label}")
                val candidateSocket = connectCandidateWithTimeout(candidate)
                socket = candidateSocket
                Log.d(LOG_TAG, "Connected using Bluetooth socket candidate ${candidate.label}")
                return@withContext
            } catch (error: Throwable) {
                lastError = error
                lastCandidateLabel = candidate.label
                Log.w(LOG_TAG, "Bluetooth socket candidate ${candidate.label} failed: ${error.message}")
                if (candidatePauseMs > 0L) {
                    delay(candidatePauseMs)
                }
            }
        }

        throw IOException(
            "Bluetooth RFCOMM connect failed for ${device.name ?: device.address} after $lastCandidateLabel: ${lastError?.message ?: "unknown error"}",
            lastError,
        )
    }

    private fun connectCandidateWithTimeout(candidate: SocketCandidate): BluetoothSocket {
        val candidateSocket = AtomicReference<BluetoothSocket?>()
        val executor = Executors.newSingleThreadExecutor(BluetoothConnectThreadFactory(candidate.label))
        val future = executor.submit<BluetoothSocket> {
            val socket = candidate.create()
            candidateSocket.set(socket)
            socket.connect()
            socket
        }

        return try {
            future.get(candidateConnectTimeoutMs, TimeUnit.MILLISECONDS)
        } catch (error: TimeoutException) {
            runCatching { candidateSocket.get()?.close() }
            future.cancel(true)
            throw IOException(
                "timed out after ${candidateConnectTimeoutMs}ms",
                error,
            )
        } catch (error: ExecutionException) {
            runCatching { candidateSocket.get()?.close() }
            throw IOException(error.cause?.message ?: "connect failed", error.cause ?: error)
        } catch (error: InterruptedException) {
            Thread.currentThread().interrupt()
            runCatching { candidateSocket.get()?.close() }
            throw IOException("interrupted while connecting", error)
        } finally {
            executor.shutdownNow()
        }
    }

    fun inputStream(): InputStream = requireNotNull(socket).inputStream

    fun outputStream(): OutputStream = requireNotNull(socket).outputStream

    fun close() {
        runCatching { socket?.close() }
        socket = null
    }

    @SuppressLint("MissingPermission")
    private fun socketCandidates(): Sequence<SocketCandidate> = sequence {
        yield(
            SocketCandidate(label = "secure SDP BlueType ${UuidConst.SERVICE_UUID}") {
                device.createRfcommSocketToServiceRecord(UuidConst.SERVICE_UUID)
            },
        )
        yield(
            SocketCandidate(label = "insecure SDP BlueType ${UuidConst.SERVICE_UUID}") {
                device.createInsecureRfcommSocketToServiceRecord(UuidConst.SERVICE_UUID)
            },
        )
    }

    private data class SocketCandidate(
        val label: String,
        val create: () -> BluetoothSocket,
    )

    private class BluetoothConnectThreadFactory(
        private val candidateLabel: String,
    ) : ThreadFactory {
        override fun newThread(runnable: Runnable): Thread {
            return Thread(runnable, "BlueType-BluetoothConnect-$candidateLabel").apply {
                isDaemon = true
            }
        }
    }

    private companion object {
        private const val LOG_TAG = "BlueTypeSocket"
        private const val CANDIDATE_CONNECT_TIMEOUT_MS = 20_000L
    }
}
