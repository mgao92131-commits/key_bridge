package com.bluetype.android.bluetooth

import java.io.ByteArrayInputStream
import java.nio.file.Files
import java.nio.file.Path
import kotlin.io.path.isDirectory
import kotlin.io.path.name
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Test

class ProtocolExamplesTest {
    @Test
    fun protocolExamples_decodeAndRoundTripThroughFrameCodec() {
        val examples = Files.list(findExamplesDirectory()).use { stream ->
            stream
                .filter { it.name.endsWith(".json") }
                .sorted()
                .toList()
        }

        assertFalse("Expected protocol examples.", examples.isEmpty())

        examples.forEach { path ->
            val json = Files.readAllBytes(path).decodeToString()
            val envelope = ProtocolJson.decodeFromString(Envelope.serializer(), json)

            assertEquals("Unexpected protocol version in ${path.name}.", 1, envelope.v)
            assertTrue("Missing id in ${path.name}.", envelope.id.isNotBlank())
            assertTrue("Unknown message type in ${path.name}: ${envelope.type}", knownTypes.contains(envelope.type))

            if (envelope.type == MsgType.ERROR.wireName) {
                val code = envelope.payload["code"]?.jsonPrimitive?.content
                assertNotNull("Missing error code in ${path.name}.", code)
                assertTrue("Unknown error code in ${path.name}: $code", knownErrorCodes.contains(code))
            }

            val roundTripped = FrameCodec.read(ByteArrayInputStream(FrameCodec.encode(envelope)))
            assertEquals(envelope, roundTripped)
        }
    }

    @Test
    fun shortcutProfileExample_isAcceptedByWireParser() {
        val path = findExamplesDirectory().resolve("shortcut_profile.json")
        val envelope = ProtocolJson.decodeFromString(Envelope.serializer(), Files.readAllBytes(path).decodeToString())

        val parsed = ShortcutProfileWireParser.parsePayload(envelope.payload.jsonObject)

        assertNotNull(parsed)
        assertEquals("Terminal", parsed?.name)
        assertNotNull(parsed?.profile)
    }

    private fun findExamplesDirectory(): Path {
        var current = Path.of(System.getProperty("user.dir")).toAbsolutePath()
        while (true) {
            val candidate = current.resolve("protocol").resolve("spec").resolve("examples")
            if (candidate.isDirectory()) {
                return candidate
            }

            val parent = current.parent ?: break
            current = parent
        }

        throw AssertionError("Could not find protocol/spec/examples from ${System.getProperty("user.dir")}.")
    }

    private companion object {
        val knownTypes = MsgType.entries.map { it.wireName }.toSet()

        val knownErrorCodes = setOf(
            "BUSY",
            "NOT_AUTHORIZED",
            "AUTH_TIMEOUT",
            "AUTH_UI_UNAVAILABLE",
            "INVALID_PAYLOAD",
            "SERVER_ERROR",
            "SESSION_REPLACED",
            "INPUT_BLOCKED",
            "CLIPBOARD_FAILED",
        )
    }
}
