package com.bluetype.android.bluetooth

import java.io.EOFException
import java.io.InputStream
import java.io.OutputStream
import java.nio.ByteBuffer
import java.nio.ByteOrder
import android.util.Log
import kotlin.math.min

object FrameCodec {
    fun encode(envelope: Envelope): ByteArray {
        val payload = ProtocolJson.encodeToString(Envelope.serializer(), envelope).toByteArray(Charsets.UTF_8)
        require(payload.size <= MAX_FRAME_SIZE) { "Frame exceeds maximum size." }

        return ByteBuffer.allocate(4 + payload.size)
            .order(ByteOrder.BIG_ENDIAN)
            .putInt(payload.size)
            .put(payload)
            .array()
    }

    fun write(output: OutputStream, envelope: Envelope) {
        val payloadStr = ProtocolJson.encodeToString(Envelope.serializer(), envelope)
        Log.d("BlueTypeCodec", "Writing envelope type=${envelope.type} id=${envelope.id}: $payloadStr")
        val bytes = encode(envelope)
        output.write(bytes)
        output.flush()
    }

    fun read(input: InputStream): Envelope {
        val sizeBytes = readExact(input, 4)
        val size = ByteBuffer.wrap(sizeBytes).order(ByteOrder.BIG_ENDIAN).int
        require(size in 1..MAX_FRAME_SIZE) { "Invalid frame size: $size" }

        val payload = readExact(input, size)
        return ProtocolJson.decodeFromString(Envelope.serializer(), payload.toString(Charsets.UTF_8))
    }

    private fun readExact(input: InputStream, length: Int): ByteArray {
        val buffer = ByteArray(length)
        var offset = 0
        while (offset < length) {
            val count = input.read(buffer, offset, min(4096, length - offset))
            if (count < 0) throw EOFException("Stream closed while reading frame.")
            offset += count
        }
        return buffer
    }

    const val MAX_FRAME_SIZE: Int = 64 * 1024
}
