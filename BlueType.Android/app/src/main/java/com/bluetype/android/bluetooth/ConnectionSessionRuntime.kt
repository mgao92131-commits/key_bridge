package com.bluetype.android.bluetooth

import android.net.ConnectivityManager
import android.net.Network
import android.net.NetworkCapabilities
import android.os.Build
import android.util.Log
import com.bluetype.android.BuildConfig
import com.bluetype.android.data.DeviceIdentityRepository
import com.bluetype.android.data.PreferencesRepository
import com.bluetype.android.data.TokenRepository
import com.bluetype.android.domain.CommandFeedback
import com.bluetype.android.domain.CommandFeedbackState
import com.bluetype.android.domain.ConnectionState
import com.bluetype.android.domain.ConnectionTarget
import com.bluetype.android.domain.RemoteAction
import com.bluetype.android.domain.UiRoute
import java.io.InputStream
import java.io.OutputStream
import java.util.UUID
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineExceptionHandler
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.Job
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put

internal class ConnectionSessionRuntime(
    private val appContext: android.content.Context,
    private val preferencesRepository: PreferencesRepository,
) {
    private val logTag = "BlueTypeConn"
    private val runtimeScope = CoroutineScope(
        SupervisorJob() + Dispatchers.IO + CoroutineExceptionHandler { _, throwable ->
            Log.e(logTag, "Unhandled coroutine exception", throwable)
        },
    )
    private val sessionMutex = Mutex()
    private val commandQueue = Channel<RemoteAction>(
        capacity = COMMAND_BUFFER_CAPACITY,
        onBufferOverflow = kotlinx.coroutines.channels.BufferOverflow.SUSPEND,
    )
    private val inputBackpressureController = InputBackpressureController(
        scope = runtimeScope,
        postAction = { action -> commandQueue.send(action) },
    )
    private val tokenRepository: TokenRepository = preferencesRepository
    private val deviceIdentityRepository: DeviceIdentityRepository = preferencesRepository
    private val persistedSessionCoordinator = PersistedSessionCoordinator(preferencesRepository)
    private val connectionOrchestrator = ConnectionOrchestrator(appContext)
    private val commandDispatcher = ConnectionCommandDispatcher(
        stateProvider = { ConnectionUiStateStore.state.value },
        connectionProvider = { activeConnection },
        onError = ::setError,
        onQueuedFeedback = ConnectionUiStateStore::setFeedback,
    )
    private val stickyModifierManager = StickyModifierManager(
        scope = runtimeScope,
        sendKeyDown = { key -> commandDispatcher.sendKeyDown(key) },
        sendKeyUp = { key -> commandDispatcher.sendKeyUp(key) },
        sendKeyTap = { key -> commandDispatcher.sendKeyTap(key) },
        sendCombo = { keys -> commandDispatcher.sendCombo(keys) },
        postAction = { action -> commandQueue.send(action) },
        stickyComboDurationMs = STICKY_COMBO_DURATION_MS,
    )

    private var activeConnection: ActiveConnection? = null
    private var desiredConnection: ComputerConnectionProfile? = null
    private var manualDisconnect = false
    private var lastBluetoothDisconnectAtMs = 0L
    private var hydratedSnapshot = false
    private var reconnectJob: Job? = null

    init {
        runtimeScope.launch {
            for (action in commandQueue) {
                handleRemoteAction(action)
            }
        }
    }

    suspend fun connect(profile: ComputerConnectionProfile) {
        sessionMutex.withLock {
            cancelReconnectJobLocked()
            val error = connectInternal(profile = profile, reason = ConnectReason.Explicit, reconnectAttempt = 1)
            if (error != null) {
                setError(error, target = profile.target)
            }
        }
    }

    suspend fun disconnect() {
        requestManualDisconnect()
        preferencesRepository.clearPersistedSession()

        sessionMutex.withLock {
            disconnectInternal(updateState = false, clearSession = false)
        }
    }

    suspend fun ensureForegroundSession() {
        sessionMutex.withLock {
            hydrateFromPersistedSessionIfNeeded()
            val currentState = ConnectionUiStateStore.state.value

            if (hasReconnectJobLocked()) {
                Log.i(logTag, "Restarting background reconnect job for foreground focus")
                cancelReconnectJobLocked()
            }

            if (activeConnection != null && ConnectionUiStateStore.state.value is ConnectionState.Connected) {
                if (validateActiveConnectionLocked()) {
                    return@withLock
                }
            }

            val transientTarget = when (currentState) {
                is ConnectionState.Connecting -> currentState.target
                is ConnectionState.AwaitingApproval -> currentState.target
                is ConnectionState.Reconnecting -> currentState.target
                else -> null
            }

            if (transientTarget != null) {
                val profile = desiredConnection
                if (profile != null && profile.target == transientTarget) {
                    if (activeConnection != null && validateActiveConnectionLocked()) {
                        return@withLock
                    }

                    Log.w(logTag, "recovering stale foreground state=$currentState profile=$profile")
                    startReconnectLocked(profile, ReconnectSource.ForegroundRestore)
                    return@withLock
                }
            }

            val profile = persistedSessionCoordinator.resolveRestoreProfile(
                manualDisconnect = manualDisconnect,
                hasActiveConnection = activeConnection != null,
                hasReconnectJob = hasReconnectJobLocked(),
            ) ?: return

            desiredConnection = profile
            startReconnectLocked(profile, ReconnectSource.ForegroundRestore)
        }
    }

    suspend fun send(action: RemoteAction) {
        if (inputBackpressureController.submit(action)) {
            return
        }

        inputBackpressureController.flush()
        commandQueue.send(action)
    }

    suspend fun sendAwaitAck(action: RemoteAction): Boolean {
        inputBackpressureController.flush()
        if (action is RemoteAction.TextInsert || action is RemoteAction.KeyTap) {
            stickyModifierManager.flush()
        }
        val command = RemoteActionEncoder.encode(action) ?: return false
        return commandDispatcher.sendAwaitAck(command)
    }

    fun trySend(action: RemoteAction): Boolean {
        if (inputBackpressureController.trySubmit(action)) {
            return true
        }

        return commandQueue.trySend(action).isSuccess
    }

    private suspend fun connectInternal(
        profile: ComputerConnectionProfile,
        reason: ConnectReason,
        reconnectAttempt: Int,
    ): String? {
        Log.d(logTag, "connect profile=$profile reason=$reason")
        manualDisconnect = false
        desiredConnection = profile
        disconnectInternal(updateState = false, clearSession = false)
        ConnectionUiStateStore.setFeedback(null)
        applyTransition(
            SessionStateReducer.reduce(
                SessionStateReducer.Event.ConnectRequested(
                    target = profile.target,
                    restoreAttempt = reason.isRestoreAttempt,
                    attempt = reconnectAttempt,
                ),
            ),
        )

        try {
            val transport = kotlinx.coroutines.withTimeout(connectTimeoutMs(profile.target)) {
                connectionOrchestrator.openTransport(
                    target = profile.target,
                    isReconnectAttempt = reason.isRestoreAttempt,
                    lastBluetoothDisconnectAtMs = lastBluetoothDisconnectAtMs,
                    preferredLanNetworkProvider = ::findPreferredLanNetwork,
                )
            }
            if (shouldAbortConnection(profile)) {
                Log.i(logTag, "connect aborted by manual disconnect profile=$profile")
                runCatching { transport.close() }
                return null
            }
            attachConnectedTransport(
                profile = profile,
                token = tokenRepository.getAndMigrateToken(profile.computerId, profile.target),
                input = transport.input,
                output = transport.output,
                close = transport.close,
            )
            Log.d(logTag, "connect attachConnectedTransport completed profile=$profile")
            return null
        } catch (error: Exception) {
            if (shouldAbortConnection(profile)) {
                Log.i(logTag, "connect failed after manual disconnect profile=$profile: ${error.message}")
                return null
            }
            Log.e(logTag, "connect failed", error)
            disconnectInternal(updateState = false, clearSession = false)
            return "Failed to connect to ${profile.displayName}: ${error.message ?: "unknown error"}"
        }
    }

    private fun connectTimeoutMs(target: ConnectionTarget): Long {
        return when (target) {
            is ConnectionTarget.Bluetooth -> BLUETOOTH_CONNECT_TIMEOUT_MS
            is ConnectionTarget.Wifi -> WIFI_CONNECT_TIMEOUT_MS
        }
    }

    private suspend fun attachConnectedTransport(
        profile: ComputerConnectionProfile,
        token: String?,
        input: InputStream,
        output: OutputStream,
        close: () -> Unit,
    ) {
        Log.d(logTag, "attachConnectedTransport start profile=$profile tokenPresent=${!token.isNullOrBlank()}")
        val helloId = UUID.randomUUID().toString()
        var connectionRef: ActiveConnection? = null
        val session = SessionClient(
            logTag = logTag,
            parentScope = runtimeScope,
            input = input,
            output = output,
            initialToken = token,
            closeTransport = close,
            onEnvelope = { envelope ->
                connectionRef?.let { handleIncoming(it, envelope) }
            },
            onDisconnected = { message ->
                connectionRef?.let { connection ->
                    if (activeConnection === connection) {
                        handleUnexpectedDisconnect(connection, message)
                    }
                }
            },
        )
        val connection = ActiveConnection(
            computerId = profile.computerId,
            displayName = profile.displayName,
            target = profile.target,
            helloId = helloId,
            session = session,
        )
        connectionRef = connection
        activeConnection = connection

        session.start()
        session.send(buildHelloEnvelope(helloId, token))
    }

    private suspend fun handleRemoteAction(action: RemoteAction) {
        when (action) {
            is RemoteAction.TextInsert -> {
                stickyModifierManager.flush()
                RemoteActionEncoder.encode(action)?.let { commandDispatcher.send(it) }
            }

            is RemoteAction.KeyTap -> {
                stickyModifierManager.flush()
                RemoteActionEncoder.encode(action)?.let { commandDispatcher.send(it) }
            }

            is RemoteAction.KeyDown -> stickyModifierManager.handleKeyDown(action.key)

            is RemoteAction.KeyUp -> stickyModifierManager.handleKeyUp(action.key)

            is RemoteAction.Combo -> stickyModifierManager.handleCombo(action.keys)

            is RemoteAction.StickyRelease -> stickyModifierManager.handleStickyRelease(action.generation)

            is RemoteAction.MouseMove,
            is RemoteAction.MouseButton,
            is RemoteAction.MouseClick,
            is RemoteAction.MouseScroll,
            is RemoteAction.ClipboardSet,
            RemoteAction.ClipboardGet -> RemoteActionEncoder.encode(action)?.let { commandDispatcher.send(it) }
        }
    }

    private suspend fun handleIncoming(connection: ActiveConnection, envelope: Envelope) {
        if (shouldAbortConnection(connection)) {
            return
        }

        when (MsgType.fromWire(envelope.type)) {
            MsgType.AUTH_PENDING -> {
                applyTransition(AuthResponseHandler.pendingApproval(connection.target, envelope))
            }

            MsgType.AUTH_RESULT -> {
                val result = AuthResponseHandler.authResult(envelope)
                if (result.token != null) {
                    connection.token = result.token
                    if (result.persistToken) {
                        tokenRepository.saveToken(connection.computerId, result.token)
                    }
                }
                markConnected(connection)
            }

            MsgType.ACK -> {
                if (envelope.id == connection.helloId) {
                    markConnected(connection)
                } else {
                    val request = connection.pendingRequests.remove(envelope.id)
                    request?.ackCompletion?.complete(true)
                    if (request?.trackFeedback == true) {
                        ConnectionUiStateStore.setFeedback(
                            CommandFeedback(
                                requestId = envelope.id,
                                action = request.action,
                                state = CommandFeedbackState.SUCCEEDED,
                                message = "${request.action} completed.",
                            ),
                        )
                    }
                }
            }

            MsgType.ERROR -> {
                val payload = ProtocolJson.decodeFromJsonElement(ErrorPayload.serializer(), envelope.payload)
                handleError(connection, envelope.id, payload)
            }

            MsgType.CLIPBOARD_VALUE -> {
                val payload = ProtocolJson.decodeFromJsonElement(ClipboardValuePayload.serializer(), envelope.payload)
                ConnectionUiStateStore.setRemoteClipboardText(payload.text)
                val request = connection.pendingRequests.remove(envelope.id)
                request?.ackCompletion?.complete(true)
                val action = request?.action ?: "CLIPBOARD_GET"
                ConnectionUiStateStore.setFeedback(
                    CommandFeedback(
                        requestId = envelope.id,
                        action = action,
                        state = CommandFeedbackState.SUCCEEDED,
                        message = "Clipboard received (${payload.text.length} chars).",
                    ),
                )
                ConnectionUiStateStore.setStatus("Clipboard received (${payload.text.length} chars).")
            }

            MsgType.SHORTCUT_PROFILE -> {
                val profile = runCatching {
                    ShortcutProfileWireParser.parsePayload(envelope.payload)
                }.getOrElse { error ->
                    Log.w(logTag, "Ignoring invalid shortcut_profile payload", error)
                    null
                }
                ConnectionUiStateStore.setRemoteShortcutProfile(profile)
            }

            MsgType.PING -> {
                connection.session.trySend(
                    Envelope(
                        id = UUID.randomUUID().toString(),
                        type = MsgType.PONG.wireName,
                        token = connection.token,
                        payload = buildJsonObject { },
                    ),
                )
            }

            else -> Unit
        }
    }

    private suspend fun handleError(connection: ActiveConnection, requestId: String, payload: ErrorPayload) {
        if (requestId == connection.helloId) {
            val action = AuthResponseHandler.helloError(payload)
            if (action.clearToken) {
                tokenRepository.clearToken(connection.computerId)
                tokenRepository.clearOldGlobalToken()
            }
            if (action.clearPersistedSession) {
                preferencesRepository.clearPersistedSession()
            }
            if (action.clearDesiredTarget) {
                desiredConnection = null
            }
            disconnectInternal(updateState = false, clearSession = false)
            setError(action.message, target = connection.target)
            return
        }

        val authAction = AuthResponseHandler.commandAuthorizationError(payload)
        if (authAction != null) {
            tokenRepository.clearToken(connection.computerId)
            tokenRepository.clearOldGlobalToken()
            preferencesRepository.clearPersistedSession()
            desiredConnection = null
            disconnectInternal(updateState = false, clearSession = false)
            setError(message = authAction.message, target = connection.target)
            return
        }

        val message = payload.message.ifBlank { AuthResponseHandler.defaultErrorMessage(payload.code) }
        val request = connection.pendingRequests.remove(requestId)
        request?.ackCompletion?.complete(false)
        if (request?.trackFeedback == true) {
            ConnectionUiStateStore.setFeedback(
                CommandFeedback(
                    requestId = requestId,
                    action = request.action,
                    state = CommandFeedbackState.FAILED,
                    message = message,
                ),
            )
            ConnectionUiStateStore.setStatus(message)
        }
    }

    private suspend fun buildHelloEnvelope(helloId: String, token: String?): Envelope {
        val deviceId = deviceIdentityRepository.getOrCreateDeviceId()
        return Envelope(
            id = helloId,
            type = MsgType.HELLO.wireName,
            token = token,
            payload = buildJsonObject {
                put("deviceId", deviceId)
                put("deviceName", "${Build.MANUFACTURER} ${Build.MODEL}")
                put("appVersion", BuildConfig.VERSION_NAME)
            },
        )
    }

    private fun markConnected(connection: ActiveConnection) {
        if (shouldAbortConnection(connection)) {
            return
        }

        reconnectJob = null
        applyTransition(SessionStateReducer.reduce(SessionStateReducer.Event.AuthSucceeded(connection.target)))
        runtimeScope.launch {
            val device = com.bluetype.android.data.StoredDevice(
                id = connection.computerId,
                name = connection.displayName,
                type = if (connection.target is ConnectionTarget.Wifi) com.bluetype.android.data.DeviceType.WIFI else com.bluetype.android.data.DeviceType.BLUETOOTH,
                host = (connection.target as? ConnectionTarget.Wifi)?.host,
                port = (connection.target as? ConnectionTarget.Wifi)?.port,
                address = (connection.target as? ConnectionTarget.Bluetooth)?.address,
                lastConnectedAt = System.currentTimeMillis()
            )
            preferencesRepository.saveRecentDevice(device)
            persistedSessionCoordinator.persistSession(device = device)
        }
    }

    private fun setError(message: String, target: ConnectionTarget? = ConnectionUiStateStore.sessionTarget.value) {
        if (target != null) {
            ConnectionUiStateStore.setSessionTarget(target)
            ConnectionUiStateStore.setUiRoute(UiRoute.REMOTE_SESSION)
            runtimeScope.launch {
                persistedSessionCoordinator.persistLastError(message)
            }
        }
        applyTransition(SessionStateReducer.reduce(SessionStateReducer.Event.AuthFailed(message, target)))
        ConnectionUiStateStore.setFeedback(
            CommandFeedback(
                requestId = "runtime",
                action = "runtime",
                state = CommandFeedbackState.FAILED,
                message = message,
            ),
        )
    }

    private fun disconnectInternal(updateState: Boolean, clearSession: Boolean) {
        stickyModifierManager.reset()
        ConnectionUiStateStore.setRemoteShortcutProfile(null)
        activeConnection?.let { connection ->
            activeConnection = null
            val pending = connection.pendingRequests.toMap()
            connection.pendingRequests.clear()
            connection.session.close()

            pending.forEach { (requestId, request) ->
                request.ackCompletion?.complete(false)
                if (request.trackFeedback) {
                    ConnectionUiStateStore.setFeedback(
                        CommandFeedback(
                            requestId = requestId,
                            action = request.action,
                            state = CommandFeedbackState.FAILED,
                            message = "${request.action} cancelled because the connection closed.",
                        ),
                    )
                }
            }
        }

        if (updateState) {
            if (clearSession) {
                ConnectionUiStateStore.clearForManualDisconnect()
            } else {
                ConnectionUiStateStore.setState(ConnectionState.Idle)
                ConnectionUiStateStore.setStatus(null)
            }
        }
    }

    private fun requestManualDisconnect() {
        manualDisconnect = true
        desiredConnection = null
        reconnectJob?.cancel()
        reconnectJob = null
        ConnectionUiStateStore.clearForManualDisconnect()
    }

    private fun shouldAbortConnection(profile: ComputerConnectionProfile): Boolean {
        return manualDisconnect || desiredConnection?.computerId != profile.computerId || !runtimeScope.coroutineContext.isActive
    }

    private fun shouldAbortConnection(connection: ActiveConnection): Boolean {
        return manualDisconnect || desiredConnection?.computerId != connection.computerId || !runtimeScope.coroutineContext.isActive
    }

    private fun handleUnexpectedDisconnect(connection: ActiveConnection, message: String) {
        runtimeScope.launch {
            sessionMutex.withLock {
                if (activeConnection !== connection) {
                    return@withLock
                }

                if (connection.target is ConnectionTarget.Bluetooth) {
                    lastBluetoothDisconnectAtMs = System.currentTimeMillis()
                }

                disconnectInternal(updateState = false, clearSession = false)
                if (!manualDisconnect) {
                    Log.w(logTag, "unexpected disconnect; starting state recovery connection=$connection: $message")
                    val profile = ComputerConnectionProfile(
                        computerId = connection.computerId,
                        displayName = connection.displayName,
                        target = connection.target,
                    )
                    desiredConnection = profile
                    startReconnectLocked(profile, ReconnectSource.StateRecovery)
                }
            }
        }
    }

    private suspend fun validateActiveConnectionLocked(reportError: Boolean = true): Boolean {
        val connection = activeConnection ?: return false
        try {
            val sent = connection.session.trySend(
                Envelope(
                    id = UUID.randomUUID().toString(),
                    type = MsgType.PING.wireName,
                    token = connection.token,
                    payload = buildJsonObject { },
                ),
            )
            if (sent) {
                return true
            }

            disconnectInternal(updateState = false, clearSession = false)
            if (reportError) {
                setError("Connection lost: write failed.", target = connection.target)
            }
            return false
        } catch (_: Exception) {
            disconnectInternal(updateState = false, clearSession = false)
            if (reportError) {
                setError("Connection lost: write failed.", target = connection.target)
            }
            return false
        }
    }

    private fun updateStatus(text: String) {
        ConnectionUiStateStore.setStatus(text)
    }

    private fun applyTransition(transition: SessionStateReducer.Transition) {
        val target = transition.sessionTarget
        if (transition.showRemoteSession && target != null) {
            ConnectionUiStateStore.showRemoteSession(target)
        }
        ConnectionUiStateStore.setState(transition.state)
        ConnectionUiStateStore.setStatus(transition.statusMessage)
    }

    private fun findPreferredLanNetwork(): Network? {
        val connectivityManager = appContext.getSystemService(ConnectivityManager::class.java) ?: return null
        return connectivityManager.allNetworks.firstOrNull { network ->
            val caps = connectivityManager.getNetworkCapabilities(network) ?: return@firstOrNull false
            caps.hasTransport(NetworkCapabilities.TRANSPORT_WIFI) &&
                !caps.hasTransport(NetworkCapabilities.TRANSPORT_VPN)
        }
    }

    private suspend fun hydrateFromPersistedSessionIfNeeded() {
        if (hydratedSnapshot) {
            return
        }

        hydratedSnapshot = true
        val snapshot = persistedSessionCoordinator.hydrateSnapshot() ?: return
        val profile = ComputerConnectionProfile(
            computerId = snapshot.computer.id,
            displayName = snapshot.computer.name,
            target = snapshot.target,
        )
        desiredConnection = profile
        ConnectionUiStateStore.setSessionTarget(snapshot.target)
        ConnectionUiStateStore.setUiRoute(snapshot.uiRoute)
        snapshot.lastError?.let {
            ConnectionUiStateStore.setState(ConnectionState.Error(it))
            ConnectionUiStateStore.setStatus(it)
        }
    }

    private fun startReconnectLocked(profile: ComputerConnectionProfile, source: ReconnectSource) {
        cancelReconnectJobLocked()
        reconnectJob = runtimeScope.launch {
            // Give 0.5s for OS to clean up socket resources
            delay(500)

            val result = sessionMutex.withLock {
                if (shouldAbortReconnectLocked(profile)) {
                    reconnectJob = null
                    return@launch
                }

                if (activeConnection != null && ConnectionUiStateStore.state.value is ConnectionState.Connected) {
                    if (validateActiveConnectionLocked(reportError = false)) {
                        reconnectJob = null
                        return@launch
                    }
                }

                applyTransition(
                    SessionStateReducer.reduce(
                        SessionStateReducer.Event.ReconnectStarted(profile.target, 1),
                    ),
                )

                connectInternal(profile = profile, reason = source.connectReason, reconnectAttempt = 1)
            }

            if (result == null) {
                reconnectJob = null
                return@launch
            }

            val fallbackResult = sessionMutex.withLock {
                if (shouldAbortReconnectLocked(profile) || activeConnection != null) {
                    reconnectJob = null
                    return@launch
                }

                Log.i(logTag, "${source.logName} restore failed; retrying as explicit connect profile=$profile")
                connectInternal(profile = profile, reason = ConnectReason.Explicit, reconnectAttempt = 1)
            }

            if (fallbackResult == null) {
                reconnectJob = null
                return@launch
            }

            sessionMutex.withLock {
                if (!shouldAbortReconnectLocked(profile) && activeConnection == null) {
                    setError(fallbackResult, target = profile.target)
                }
                reconnectJob = null
            }
        }
    }

    private fun hasReconnectJobLocked(): Boolean {
        return reconnectJob?.isActive == true
    }

    private fun cancelReconnectJobLocked() {
        reconnectJob?.cancel()
        reconnectJob = null
    }

    private fun shouldAbortReconnectLocked(profile: ComputerConnectionProfile): Boolean {
        return manualDisconnect || desiredConnection?.computerId != profile.computerId || !runtimeScope.coroutineContext.isActive
    }

    private companion object {
        private const val COMMAND_BUFFER_CAPACITY = 256
        private const val STICKY_COMBO_DURATION_MS = 300L
        private const val WIFI_CONNECT_TIMEOUT_MS = 15_000L
        private const val BLUETOOTH_CONNECT_TIMEOUT_MS = 45_000L
    }

    private enum class ReconnectSource(val logName: String) {
        ForegroundRestore("foreground"),
        StateRecovery("state recovery"),
    }

    private enum class ConnectReason(val isRestoreAttempt: Boolean) {
        Explicit(false),
        ForegroundRestore(true),
        StateRecovery(true),
    }

    private val ReconnectSource.connectReason: ConnectReason
        get() = when (this) {
            ReconnectSource.ForegroundRestore -> ConnectReason.ForegroundRestore
            ReconnectSource.StateRecovery -> ConnectReason.StateRecovery
        }
}
