package com.bluetype.android.bluetooth

import com.bluetype.android.domain.CommandFeedback
import com.bluetype.android.domain.CommandFeedbackState
import com.bluetype.android.domain.ConnectionState
import com.bluetype.android.domain.ConnectionTarget
import com.bluetype.android.domain.RemoteAction
import com.bluetype.android.transport.SessionClient
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.async
import kotlinx.coroutines.test.advanceTimeBy
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

@OptIn(ExperimentalCoroutinesApi::class)
class ConnectionCommandDispatcherTest {
    private val target = ConnectionTarget.Wifi(host = "192.168.1.10", port = 24862)

    @Test
    fun sendAwaitAck_returnsTrue_whenAckCompletesRequest() = runTest {
        val fixture = createFixture(ConnectionState.Connected(target))
        val result = async {
            fixture.dispatcher.sendAwaitAck(textCommand(), timeoutMs = 1_000L)
        }

        runCurrent()
        completePending(fixture.connection, succeeded = true)

        assertTrue(result.await())
        assertEquals(CommandFeedbackState.QUEUED, fixture.feedback.single().state)
        fixture.client.close()
    }

    @Test
    fun sendAwaitAck_returnsFalse_whenErrorCompletesRequest() = runTest {
        val fixture = createFixture(ConnectionState.Connected(target))
        val result = async {
            fixture.dispatcher.sendAwaitAck(textCommand(), timeoutMs = 1_000L)
        }

        runCurrent()
        completePending(fixture.connection, succeeded = false)

        assertFalse(result.await())
        fixture.client.close()
    }

    @Test
    fun sendAwaitAck_returnsFalseAndRemovesPendingRequest_whenAckTimesOut() = runTest {
        val fixture = createFixture(ConnectionState.Connected(target))
        val result = async {
            fixture.dispatcher.sendAwaitAck(textCommand(), timeoutMs = 100L)
        }

        runCurrent()
        assertEquals(1, fixture.connection.pendingRequests.size)

        advanceTimeBy(101L)
        runCurrent()

        assertFalse(result.await())
        assertTrue(fixture.connection.pendingRequests.isEmpty())
        assertEquals(CommandFeedbackState.FAILED, fixture.feedback.last().state)
        fixture.client.close()
    }

    @Test
    fun sendAwaitAck_returnsFalse_whenNoActiveConnection() = runTest {
        val errors = mutableListOf<String>()
        val dispatcher = ConnectionCommandDispatcher(
            stateProvider = { ConnectionState.Idle },
            connectionProvider = { null },
            onError = errors::add,
            onQueuedFeedback = { },
        )

        val result = dispatcher.sendAwaitAck(textCommand(), timeoutMs = 1_000L)

        assertFalse(result)
        assertEquals(listOf("No active connection for TEXT_INSERT"), errors)
    }

    private fun createFixture(state: ConnectionState): DispatcherFixture {
        val client = SessionClient(
            logTag = "TestSessionClient",
            parentScope = kotlinx.coroutines.CoroutineScope(kotlinx.coroutines.Dispatchers.Unconfined),
            input = EmptyInputStream,
            output = NoOpOutputStream,
            initialToken = null,
            closeTransport = { },
            onEnvelope = { },
            onDisconnected = { },
        )
        val connection = ActiveConnection(
            profile = ComputerConnectionProfile(
                computerId = "test-comp-id",
                displayName = "test-comp-name",
                target = target,
                persistenceIntent = ProfilePersistenceIntent.EXISTING_SAVED_COMPUTER,
            ),
            attemptId = 1L,
            helloId = "hello",
            session = client,
        )
        val feedback = mutableListOf<CommandFeedback>()
        val dispatcher = ConnectionCommandDispatcher(
            stateProvider = { state },
            connectionProvider = { connection },
            onError = { },
            onQueuedFeedback = feedback::add,
        )
        return DispatcherFixture(client, connection, dispatcher, feedback)
    }

    private fun completePending(connection: ActiveConnection, succeeded: Boolean) {
        val request = connection.pendingRequests.entries.single()
        connection.pendingRequests.remove(request.key)?.ackCompletion?.complete(succeeded)
    }

    private fun textCommand(): EncodedRemoteCommand {
        return RemoteActionEncoder.encode(RemoteAction.TextInsert("hello"))!!
    }

    private data class DispatcherFixture(
        val client: SessionClient,
        val connection: ActiveConnection,
        val dispatcher: ConnectionCommandDispatcher,
        val feedback: MutableList<CommandFeedback>,
    )

    private object EmptyInputStream : java.io.InputStream() {
        override fun read(): Int = -1
    }

    private object NoOpOutputStream : java.io.OutputStream() {
        override fun write(b: Int) {
        }
    }
}
