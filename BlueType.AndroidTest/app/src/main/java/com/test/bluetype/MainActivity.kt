package com.test.bluetype

import android.os.Bundle
import android.widget.Button
import android.widget.EditText
import android.widget.TextView
import androidx.appcompat.app.AppCompatActivity
import kotlinx.coroutines.*
import java.net.InetSocketAddress
import java.net.Socket
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.util.UUID

class MainActivity : AppCompatActivity() {
    private lateinit var logOutput: TextView
    private val scope = CoroutineScope(Dispatchers.Main + Job())

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_main)

        val ipInput = findViewById<EditText>(R.id.ipInput)
        val connectBtn = findViewById<Button>(R.id.connectBtn)
        logOutput = findViewById<TextView>(R.id.logOutput)

        connectBtn.setOnClickListener {
            val ip = ipInput.text.toString()
            startTest(ip)
        }
    }

    private fun log(msg: String) {
        scope.launch {
            logOutput.append("\n> $msg")
        }
    }

    private fun startTest(host: String) {
        log("Testing connection to $host:24862...")
        scope.launch(Dispatchers.IO) {
            try {
                val socket = Socket()
                socket.connect(InetSocketAddress(host, 24862), 5000)
                log("Connected!")

                val payload = """
                {
                    "v": 1,
                    "id": "${UUID.randomUUID()}",
                    "type": "hello",
                    "payload": {
                        "deviceId": "android-tester",
                        "deviceName": "Android TCP Tester",
                        "appVersion": "1.0"
                    }
                }
                """.trimIndent()

                val data = payload.toByteArray(Charsets.UTF_8)
                val length = data.size
                
                // BigEndian 4-byte length
                val buffer = ByteBuffer.allocate(4 + length)
                buffer.order(ByteOrder.BIG_ENDIAN)
                buffer.putInt(length)
                buffer.put(data)

                socket.getOutputStream().write(buffer.array())
                socket.getOutputStream().flush()
                log("Sent Hello Frame ($length bytes)")

                // Read response length
                val lenBuf = ByteArray(4)
                val readLen = socket.getInputStream().read(lenBuf)
                if (readLen == 4) {
                    val respLen = ByteBuffer.wrap(lenBuf).order(ByteOrder.BIG_ENDIAN).int
                    log("Server expects to send $respLen bytes")
                    
                    val respData = ByteArray(respLen)
                    var totalRead = 0
                    while (totalRead < respLen) {
                        val r = socket.getInputStream().read(respData, totalRead, respLen - totalRead)
                        if (r == -1) break
                        totalRead += r
                    }
                    log("Response: ${String(respData)}")
                } else {
                    log("Failed to read response header")
                }

                socket.close()
                log("Closed.")
            } catch (e: Exception) {
                log("Error: ${e.message}")
            }
        }
    }
}
