package com.bluetype.android.bluetooth

import com.bluetype.android.transport.FrameCodec
import com.bluetype.android.feature.shortcuts.*
import com.bluetype.android.protocol.*
import java.io.ByteArrayInputStream
import java.io.EOFException
import java.nio.file.Files
import java.nio.file.Path
import kotlin.io.path.isDirectory
import kotlin.io.path.name
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.intOrNull
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Assert.fail
import org.junit.Test

class ProtocolExamplesTest {
    @Test
    fun protocolExamples_decodeAndRoundTripThroughFrameCodec() {
        val manifest = readManifest()
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

            assertEquals("Unexpected protocol version in ${path.name}.", manifest.version, envelope.v)
            assertTrue("Missing id in ${path.name}.", envelope.id.isNotBlank())
            assertTrue(
                "Unknown message type in ${path.name}: ${envelope.type}",
                (manifest.commands + manifest.responses).toSet().contains(envelope.type),
            )

            if (envelope.type == MsgType.ERROR.wireName) {
                val code = envelope.payload["code"]?.jsonPrimitive?.content
                assertNotNull("Missing error code in ${path.name}.", code)
                assertTrue("Unknown error code in ${path.name}: $code", manifest.errorCodes.contains(code))
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

    @Test
    fun protocolConstants_matchSharedManifest() {
        val manifest = readManifest()
        val expectedTypes = (manifest.commands + manifest.responses).toSet()
        val actualTypes = MsgType.entries.map { it.wireName }.toSet()

        assertEquals(expectedTypes, actualTypes)
    }

    @Test
    fun invalidProtocolExamples_areRejectedByV1Contract() {
        val manifest = readManifest()
        val invalidExamples = Files.list(findInvalidDirectory()).use { stream ->
            stream
                .filter { it.name.endsWith(".json") }
                .sorted()
                .toList()
        }

        assertFalse("Expected invalid protocol examples.", invalidExamples.isEmpty())

        invalidExamples.forEach { path ->
            val root = ProtocolJson.parseToJsonElement(Files.readAllBytes(path).decodeToString()).jsonObject
            assertFalse(path.name, isValidEnvelope(root, manifest))
        }
    }

    @Test
    fun frameFixtures_encodeAndDecodeWithCanonicalBytes() {
        val frameFixtures = Files.list(findFramesDirectory()).use { stream ->
            stream
                .filter { it.name.endsWith(".json") }
                .sorted()
                .toList()
        }

        assertFalse("Expected framing fixtures.", frameFixtures.isEmpty())

        frameFixtures.forEach { path ->
            val fixture = ProtocolJson.decodeFromString(
                FrameFixture.serializer(),
                Files.readAllBytes(path).decodeToString(),
            )
            val expected = ProtocolJson.decodeFromString(Envelope.serializer(), fixture.json)
            val expectedBytes = hexToBytes(fixture.frameHex)
            val decoded = FrameCodec.read(ByteArrayInputStream(expectedBytes))

            assertEquals(expected, decoded)
            assertEquals(expectedBytes.size - 4, frameLength(expectedBytes))

            val encoded = FrameCodec.encode(expected)
            assertEquals(encoded.size - 4, frameLength(encoded))
            assertEquals(expected, FrameCodec.read(ByteArrayInputStream(encoded)))
        }
    }

    @Test
    fun framing_rejectsOversizedAndTruncatedPayloads() {
        val oversized = Envelope(
            id = "oversized-frame",
            type = MsgType.TEXT_INSERT.wireName,
            payload = buildJsonObject {
                put("text", JsonPrimitive("x".repeat(FrameCodec.MAX_FRAME_SIZE)))
            },
        )

        try {
            FrameCodec.encode(oversized)
            fail("Expected oversized frame rejection.")
        } catch (_: IllegalArgumentException) {
        }

        try {
            FrameCodec.read(ByteArrayInputStream(byteArrayOf(0, 0, 0, 4, 1, 2)))
            fail("Expected truncated frame rejection.")
        } catch (_: EOFException) {
        }
    }

    private fun readManifest(): ProtocolManifest {
        return ProtocolJson.decodeFromString(
            ProtocolManifest.serializer(),
            Files.readAllBytes(findSpecDirectory().resolve("protocol-v1.json")).decodeToString(),
        )
    }

    private fun isValidEnvelope(root: JsonObject, manifest: ProtocolManifest): Boolean {
        val version = intField(root, "v") ?: return false
        val id = stringField(root, "id", nonEmpty = true) ?: return false
        val type = stringField(root, "type", nonEmpty = true) ?: return false
        val payload = root["payload"] as? JsonObject ?: return false
        if (version != manifest.version || id.isBlank() || type !in (manifest.commands + manifest.responses)) {
            return false
        }

        return when (type) {
            MsgType.HELLO.wireName -> stringField(payload, "deviceId", nonEmpty = true) != null &&
                stringField(payload, "deviceName", nonEmpty = true) != null
            MsgType.TEXT_INSERT.wireName -> stringField(payload, "text") != null
            MsgType.KEY_TAP.wireName,
            MsgType.KEY_DOWN.wireName,
            MsgType.KEY_UP.wireName -> stringField(payload, "key", nonEmpty = true) != null
            MsgType.COMBO.wireName -> stringArrayField(payload, "keys")
            MsgType.MOUSE_MOVE.wireName -> intField(payload, "dx") != null && intField(payload, "dy") != null
            MsgType.MOUSE_BUTTON.wireName -> stringField(payload, "button", nonEmpty = true) != null &&
                oneOf(payload, "action", "down", "up")
            MsgType.MOUSE_CLICK.wireName -> stringField(payload, "button", nonEmpty = true) != null &&
                optionalIntField(payload, "repeat")
            MsgType.MOUSE_SCROLL.wireName -> optionalIntField(payload, "deltaX") && optionalIntField(payload, "deltaY")
            MsgType.CLIPBOARD_SET.wireName -> stringField(payload, "text") != null
            MsgType.CLIPBOARD_GET.wireName,
            MsgType.PING.wireName,
            MsgType.PONG.wireName -> true
            MsgType.ACK.wireName -> booleanField(payload, "ok")
            MsgType.ERROR.wireName -> stringField(payload, "message") != null &&
                manifest.errorCodes.contains(stringField(payload, "code"))
            MsgType.AUTH_PENDING.wireName -> intField(payload, "timeoutSec") != null &&
                stringField(payload, "message") != null
            MsgType.AUTH_RESULT.wireName -> booleanField(payload, "ok") &&
                booleanField(payload, "persistToken") &&
                booleanField(payload, "trusted") &&
                optionalNullableStringField(payload, "token")
            MsgType.CLIPBOARD_VALUE.wireName -> stringField(payload, "text") != null
            MsgType.SHORTCUT_PROFILE.wireName -> nullableStringField(payload, "name") &&
                nullableObjectField(payload, "profile")
            else -> false
        }
    }

    private fun stringField(objectValue: JsonObject, name: String, nonEmpty: Boolean = false): String? {
        val primitive = objectValue[name] as? JsonPrimitive ?: return null
        if (!primitive.isString) return null
        return primitive.content.takeIf { !nonEmpty || it.isNotBlank() }
    }

    private fun intField(objectValue: JsonObject, name: String): Int? {
        val primitive = objectValue[name] as? JsonPrimitive ?: return null
        return primitive.takeUnless { it.isString }?.intOrNull
    }

    private fun optionalIntField(objectValue: JsonObject, name: String): Boolean {
        return !objectValue.containsKey(name) || intField(objectValue, name) != null
    }

    private fun booleanField(objectValue: JsonObject, name: String): Boolean {
        val primitive = objectValue[name] as? JsonPrimitive ?: return false
        return !primitive.isString && primitive.booleanOrNull != null
    }

    private fun stringArrayField(objectValue: JsonObject, name: String): Boolean {
        val array = objectValue[name] as? JsonArray ?: return false
        return array.all { (it as? JsonPrimitive)?.isString == true }
    }

    private fun nullableStringField(objectValue: JsonObject, name: String): Boolean {
        return objectValue[name] is JsonNull || stringField(objectValue, name) != null
    }

    private fun optionalNullableStringField(objectValue: JsonObject, name: String): Boolean {
        return !objectValue.containsKey(name) || nullableStringField(objectValue, name)
    }

    private fun nullableObjectField(objectValue: JsonObject, name: String): Boolean {
        return objectValue[name] is JsonNull || objectValue[name] is JsonObject
    }

    private fun oneOf(objectValue: JsonObject, name: String, vararg values: String): Boolean {
        return stringField(objectValue, name)?.let { it in values } == true
    }

    private fun findSpecDirectory(): Path {
        var current = Path.of(System.getProperty("user.dir")).toAbsolutePath()
        while (true) {
            val candidate = current.resolve("protocol").resolve("spec")
            if (candidate.isDirectory()) {
                return candidate
            }

            val parent = current.parent ?: break
            current = parent
        }

        throw AssertionError("Could not find protocol/spec from ${System.getProperty("user.dir")}.")
    }

    private fun findExamplesDirectory(): Path = findSpecDirectory().resolve("examples")

    private fun findInvalidDirectory(): Path = findSpecDirectory().resolve("invalid")

    private fun findFramesDirectory(): Path = findSpecDirectory().resolve("frames")

    private fun hexToBytes(value: String): ByteArray {
        require(value.length % 2 == 0) { "Frame hex must contain complete bytes." }
        return ByteArray(value.length / 2) { index ->
            value.substring(index * 2, index * 2 + 2).toInt(16).toByte()
        }
    }

    private fun frameLength(frame: ByteArray): Int {
        require(frame.size >= 4) { "Frame must contain a length prefix." }
        return ((frame[0].toInt() and 0xff) shl 24) or
            ((frame[1].toInt() and 0xff) shl 16) or
            ((frame[2].toInt() and 0xff) shl 8) or
            (frame[3].toInt() and 0xff)
    }

    @Serializable
    private data class ProtocolManifest(
        val version: Int,
        val commands: List<String>,
        val responses: List<String>,
        val errorCodes: List<String>,
    )

    @Serializable
    private data class FrameFixture(
        val json: String,
        val frameHex: String,
    )
}
