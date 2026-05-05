package com.bluetype.android.bluetooth

import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class SessionClientTest {
    @Test
    fun trySend_returnsFalse_whenWriterBufferIsFull() = runTest {
        val client = SessionClient(
            logTag = "TestSessionClient",
            parentScope = this,
            input = EmptyInputStream,
            output = NoOpOutputStream,
            initialToken = null,
            closeTransport = { },
            onEnvelope = { },
            onDisconnected = { },
            writerCapacity = 1,
        )

        val first = client.trySend(testEnvelope("1"))
        val second = client.trySend(testEnvelope("2"))

        assertTrue(first)
        assertFalse(second)

        client.close()
    }

    private fun testEnvelope(id: String): Envelope {
        return Envelope(
            id = id,
            type = MsgType.PING.wireName,
            token = null,
            payload = kotlinx.serialization.json.buildJsonObject { },
        )
    }

    private object EmptyInputStream : java.io.InputStream() {
        override fun read(): Int = -1
    }

    private object NoOpOutputStream : java.io.OutputStream() {
        override fun write(b: Int) {
        }
    }
}
