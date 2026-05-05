package com.bluetype.android.bluetooth

import com.bluetype.android.domain.ConnectionState
import com.bluetype.android.domain.ConnectionTarget
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class AuthResponseHandlerTest {
    private val target = ConnectionTarget.Wifi(host = "192.168.1.10", port = 24862)

    @Test
    fun pendingApproval_decodesTimeoutIntoStateTransition() {
        val envelope = Envelope(
            id = "hello",
            type = MsgType.AUTH_PENDING.wireName,
            payload = buildJsonObject {
                put("timeoutSec", 60)
                put("message", "Please confirm")
            },
        )

        val transition = AuthResponseHandler.pendingApproval(target, envelope)

        assertEquals(ConnectionState.AwaitingApproval(target, 60), transition.state)
        assertEquals("Confirm this device on Windows within 60 seconds.", transition.statusMessage)
    }

    @Test
    fun authResult_persistsTrustedToken() {
        val envelope = Envelope(
            id = "hello",
            type = MsgType.AUTH_RESULT.wireName,
            payload = buildJsonObject {
                put("ok", true)
                put("token", "abc")
                put("persistToken", false)
                put("trusted", true)
            },
        )

        val result = AuthResponseHandler.authResult(envelope)

        assertEquals("abc", result.token)
        assertTrue(result.persistToken)
    }

    @Test
    fun authResult_allowOnceDoesNotPersistBlankToken() {
        val envelope = Envelope(
            id = "hello",
            type = MsgType.AUTH_RESULT.wireName,
            payload = buildJsonObject {
                put("ok", true)
                put("token", "")
                put("persistToken", true)
                put("trusted", true)
            },
        )

        val result = AuthResponseHandler.authResult(envelope)

        assertNull(result.token)
        assertFalse(result.persistToken)
    }

    @Test
    fun helloNotAuthorized_clearsTokenAndRestoreState() {
        val action = AuthResponseHandler.helloError(
            ErrorPayload(code = ErrorCodes.NotAuthorized, message = "Invalid token."),
        )

        assertEquals("Invalid token.", action.message)
        assertTrue(action.clearToken)
        assertTrue(action.clearPersistedSession)
        assertTrue(action.clearDesiredTarget)
    }

    @Test
    fun helloBusy_stopsRestoreWithoutClearingToken() {
        val action = AuthResponseHandler.helloError(
            ErrorPayload(code = ErrorCodes.Busy, message = ""),
        )

        assertEquals("Another device is already controlling this PC.", action.message)
        assertFalse(action.clearToken)
        assertTrue(action.clearPersistedSession)
        assertTrue(action.clearDesiredTarget)
    }

    @Test
    fun commandAuthorizationError_onlyHandlesNotAuthorized() {
        val handled = AuthResponseHandler.commandAuthorizationError(
            ErrorPayload(code = ErrorCodes.NotAuthorized, message = "no"),
        )
        val ignored = AuthResponseHandler.commandAuthorizationError(
            ErrorPayload(code = ErrorCodes.InvalidPayload, message = "bad"),
        )

        assertEquals("Authorization expired. Reconnect to approve this device again.", handled?.message)
        assertTrue(handled?.clearToken == true)
        assertNull(ignored)
    }
}
