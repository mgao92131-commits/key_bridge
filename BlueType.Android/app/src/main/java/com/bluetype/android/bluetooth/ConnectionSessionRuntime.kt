package com.bluetype.android.bluetooth

import android.net.ConnectivityManager
import android.net.Network
import android.net.NetworkCapabilities
import android.os.Build
import android.util.Log
import com.bluetype.android.BuildConfig
import com.bluetype.android.data.DeviceIdentityRepository
import com.bluetype.android.data.DeviceType
import com.bluetype.android.data.PersistedSession
import com.bluetype.android.data.StoredDevice
import com.bluetype.android.data.TokenCandidate
import com.bluetype.android.data.TokenRepository
import com.bluetype.android.data.TokenSource
import com.bluetype.android.data.preferences.PreferenceStores
import com.bluetype.android.domain.CommandFeedback
import com.bluetype.android.domain.CommandFeedbackState
import com.bluetype.android.domain.ConnectionPhase
import com.bluetype.android.domain.ConnectionState
import com.bluetype.android.domain.ConnectionTarget
import com.bluetype.android.domain.RemoteAction
import com.bluetype.android.domain.UiRoute
import com.bluetype.android.transport.ConnectionOrchestrator
import com.bluetype.android.transport.OrchestratorTransportConnector
import com.bluetype.android.transport.SessionClient
import com.bluetype.android.transport.TransportConnector
import java.io.InputStream
import java.io.OutputStream
import java.util.UUID
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineExceptionHandler
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withTimeout
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put

internal class ConnectionSessionRuntime(
    private val appContext: android.content.Context,
    private val preferenceStores: PreferenceStores,
    private val transportConnector: TransportConnector = OrchestratorTransportConnector(
        ConnectionOrchestrator(appContext),
    ),
    private val runtimeScope: CoroutineScope = CoroutineScope(
        SupervisorJob() + Dispatchers.IO + CoroutineExceptionHandler { _, throwable ->
            Log.e("BlueTypeConn", "Unhandled coroutine exception", throwable)
        },
    ),
) {
    private val logTag = "BlueTypeConn"
    private val sessionMutex = Mutex()
    private val commandQueue = Channel<RemoteAction>(
        capacity = COMMAND_BUFFER_CAPACITY,
        onBufferOverflow = kotlinx.coroutines.channels.BufferOverflow.SUSPEND,
    )
    private val inputBackpressureController = InputBackpressureController(
        scope = runtimeScope,
        postAction = { action -> commandQueue.send(action) },
    )
    private val tokenRepository: TokenRepository = preferenceStores.tokens
    private val deviceIdentityRepository: DeviceIdentityRepository = preferenceStores.devices
    private val persistedSessionCoordinator = PersistedSessionCoordinator(preferenceStores.sessions)
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
    private var connectJob: Job? = null
    private var openingTransportClose: (() -> Unit)? = null
    private val attemptTracker = ConnectionAttemptTracker()
    private var lastForegroundEnsureAtMs = 0L
    private val currentAttemptId: Long
        get() = attemptTracker.currentAttemptId

    init {
        runtimeScope.launch {
            for (action in commandQueue) {
                handleRemoteAction(action)
            }
        }
    }

    /**
     * Starts an explicit user-driven connect. Publishes Connecting UI immediately under a short
     * critical section, then opens transport outside the mutex.
     */
    suspend fun connect(profile: ComputerConnectionProfile) {
        val attemptId: Long
        sessionMutex.withLock {
            cancelReconnectJobLocked()
            attemptId = beginAttemptLocked(
                profile = profile,
                reason = ConnectReason.Explicit,
                reconnectAttempt = 1,
            )
            connectJob = runtimeScope.launch {
                runConnectAttempt(
                    attemptId = attemptId,
                    profile = profile,
                    reason = ConnectReason.Explicit,
                    reconnectAttempt = 1,
                )
            }
        }
    }

    suspend fun disconnect() {
        sessionMutex.withLock {
            cancelReconnectJobLocked()
            cancelConnectWorkLocked()
            attemptTracker.invalidate()
            requestManualDisconnectLocked()
            disconnectInternal(updateState = false, clearSession = false)
        }
        preferenceStores.sessions.clearPersistedSession()
    }

    suspend fun disconnectIfComputer(computerId: String) {
        val matches = sessionMutex.withLock {
            activeConnection?.computerId == computerId || desiredConnection?.computerId == computerId
        }
        if (matches) {
            disconnect()
        }
    }

    suspend fun ensureForegroundSession() {
        val now = System.currentTimeMillis()
        if (now - lastForegroundEnsureAtMs < FOREGROUND_ENSURE_DEBOUNCE_MS) {
            Log.d(logTag, "ensureForegroundSession debounced")
            return
        }
        lastForegroundEnsureAtMs = now

        var profileToRestore: ComputerConnectionProfile? = null
        sessionMutex.withLock {
            hydrateFromPersistedSessionIfNeeded()

            if (manualDisconnect) {
                return
            }

            // An in-flight connect/reconnect already owns the session — do not spawn another.
            if (connectJob?.isActive == true) {
                Log.d(logTag, "ensureForegroundSession skipped: connectJob active attemptId=$currentAttemptId")
                return
            }

            if (activeConnection != null && ConnectionUiStateStore.state.value is ConnectionState.Connected) {
                if (validateActiveConnectionLocked()) {
                    return
                }
            }

            val currentState = ConnectionUiStateStore.state.value
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
                        return
                    }
                    Log.w(logTag, "recovering stale foreground state=$currentState profile=$profile")
                    profileToRestore = profile
                } else {
                    return
                }
            } else {
                profileToRestore = persistedSessionCoordinator.resolveRestoreProfile(
                    manualDisconnect = manualDisconnect,
                    hasActiveConnection = activeConnection != null,
                    hasReconnectJob = hasReconnectJobLocked(),
                )
            }

            val profile = profileToRestore ?: return
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

    private fun beginAttemptLocked(
        profile: ComputerConnectionProfile,
        reason: ConnectReason,
        reconnectAttempt: Int,
    ): Long {
        val previousAttemptId = currentAttemptId
        // Cancel only the previous connect attempt — never cancel the caller reconnectJob here.
        cancelConnectWorkLocked()
        disconnectInternal(updateState = false, clearSession = false)

        manualDisconnect = false
        desiredConnection = profile
        val attemptId = attemptTracker.begin(profile.computerId)
        ConnectionUiStateStore.setFeedback(null)
        ConnectionUiStateStore.setConnectingComputerId(profile.computerId)

        val startedAt = System.currentTimeMillis()
        Log.i(
            logTag,
            "connect_attempt_started attemptId=$attemptId computerId=${profile.computerId} " +
                "reason=$reason targetType=${profile.target::class.simpleName} " +
                "previousAttemptId=$previousAttemptId startedAt=$startedAt",
        )

        applyTransition(
            SessionStateReducer.reduce(
                if (reason.isRestoreAttempt) {
                    SessionStateReducer.Event.ReconnectStarted(
                        target = profile.target,
                        displayName = profile.displayName,
                        computerId = profile.computerId,
                        attemptId = attemptId,
                        attempt = reconnectAttempt,
                    )
                } else {
                    SessionStateReducer.Event.ConnectRequested(
                        target = profile.target,
                        displayName = profile.displayName,
                        computerId = profile.computerId,
                        attemptId = attemptId,
                        restoreAttempt = false,
                        attempt = reconnectAttempt,
                        phase = ConnectionPhase.OPENING_TRANSPORT,
                    )
                },
            ),
        )
        return attemptId
    }

    private suspend fun runConnectAttempt(
        attemptId: Long,
        profile: ComputerConnectionProfile,
        reason: ConnectReason,
        reconnectAttempt: Int,
    ) {
        val startedAt = System.currentTimeMillis()
        try {
            Log.i(
                logTag,
                "connect_transport_open_start attemptId=$attemptId computerId=${profile.computerId} " +
                    "phase=${ConnectionPhase.OPENING_TRANSPORT}",
            )
            val transport = withTimeout(connectTimeoutMs(profile.target)) {
                transportConnector.open(
                    target = profile.target,
                    isReconnectAttempt = reason.isRestoreAttempt,
                    lastBluetoothDisconnectAtMs = lastBluetoothDisconnectAtMs,
                    preferredLanNetworkProvider = ::findPreferredLanNetwork,
                )
            }

            var shouldAttach = false
            sessionMutex.withLock {
                if (!isCurrentAttemptLocked(attemptId, profile)) {
                    Log.i(
                        logTag,
                        "connect_attempt_superseded attemptId=$attemptId currentAttemptId=$currentAttemptId " +
                            "computerId=${profile.computerId}",
                    )
                    runCatching { transport.close() }
                    return
                }
                openingTransportClose = transport.close
                applyTransition(
                    SessionStateReducer.reduce(
                        SessionStateReducer.Event.TransportPhaseChanged(
                            target = profile.target,
                            displayName = profile.displayName,
                            computerId = profile.computerId,
                            attemptId = attemptId,
                            phase = ConnectionPhase.AUTHENTICATING,
                        ),
                    ),
                )
                shouldAttach = true
            }

            if (!shouldAttach) {
                runCatching { transport.close() }
                return
            }

            Log.i(
                logTag,
                "connect_transport_open_success attemptId=$attemptId computerId=${profile.computerId} " +
                    "durationMs=${System.currentTimeMillis() - startedAt}",
            )

            val candidate = tokenRepository.resolveTokenCandidate(profile.computerId, profile.target)

            sessionMutex.withLock {
                if (!isCurrentAttemptLocked(attemptId, profile)) {
                    Log.i(logTag, "connect_attempt_superseded after open attemptId=$attemptId")
                    openingTransportClose = null
                    runCatching { transport.close() }
                    return
                }
                openingTransportClose = null
                attachConnectedTransport(
                    profile = profile,
                    attemptId = attemptId,
                    candidate = candidate,
                    input = transport.input,
                    output = transport.output,
                    close = transport.close,
                )
            }
        } catch (cancelled: CancellationException) {
            Log.i(logTag, "connect_attempt_cancelled attemptId=$attemptId computerId=${profile.computerId}")
            closeOpeningTransportQuietly()
            throw cancelled
        } catch (error: Exception) {
            closeOpeningTransportQuietly()
            sessionMutex.withLock {
                if (!isCurrentAttemptLocked(attemptId, profile)) {
                    Log.i(
                        logTag,
                        "connect_attempt_failed ignored (superseded) attemptId=$attemptId " +
                            "message=${error.message}",
                    )
                    return
                }
                Log.e(logTag, "connect_attempt_failed attemptId=$attemptId", error)
                disconnectInternal(updateState = false, clearSession = false)
                setError(
                    message = "Failed to connect to ${profile.displayName}: ${error.message ?: "unknown error"}",
                    target = profile.target,
                    displayName = profile.displayName,
                    computerId = profile.computerId,
                )
            }
        } finally {
            sessionMutex.withLock {
                if (currentAttemptId == attemptId) {
                    connectJob = null
                }
            }
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
        attemptId: Long,
        candidate: TokenCandidate?,
        input: InputStream,
        output: OutputStream,
        close: () -> Unit,
    ) {
        val token = candidate?.token
        Log.d(
            logTag,
            "attachConnectedTransport attemptId=$attemptId profile=$profile " +
                "tokenPresent=${!token.isNullOrBlank()} tokenSource=${candidate?.source?.let { it::class.simpleName }}",
        )
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
            profile = profile,
            attemptId = attemptId,
            helloId = helloId,
            session = session,
            tokenCandidate = candidate,
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
                Log.i(
                    logTag,
                    "connect_auth_pending attemptId=${connection.attemptId} computerId=${connection.computerId}",
                )
                applyTransition(
                    AuthResponseHandler.pendingApproval(
                        target = connection.target,
                        displayName = connection.displayName,
                        computerId = connection.computerId,
                        attemptId = connection.attemptId,
                        envelope = envelope,
                    ),
                )
            }

            MsgType.AUTH_RESULT -> {
                val result = AuthResponseHandler.authResult(envelope)
                if (result.token != null) {
                    connection.token = result.token
                }
                val outcome = AuthenticationOutcomeResolver.fromAuthResult(
                    token = result.token,
                    persistToken = result.persistToken,
                    candidate = connection.tokenCandidate,
                )
                finalizeAuthenticatedConnection(connection, outcome)
            }

            MsgType.ACK -> {
                if (envelope.id == connection.helloId) {
                    val outcome = AuthenticationOutcomeResolver.fromHelloAck(connection.tokenCandidate)
                    if (connection.tokenCandidate?.token != null) {
                        connection.token = connection.tokenCandidate.token
                    }
                    finalizeAuthenticatedConnection(connection, outcome)
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
        if (shouldAbortConnection(connection)) {
            return
        }

        if (requestId == connection.helloId) {
            val action = AuthResponseHandler.helloError(payload)
            if (action.clearRejectedCandidate) {
                tokenRepository.clearRejectedCandidate(connection.computerId, connection.tokenCandidate)
            }
            if (action.clearPersistedSession) {
                preferenceStores.sessions.clearPersistedSession()
            }
            sessionMutex.withLock {
                if (!isCurrentAttemptLocked(connection.attemptId, connection.profile)) {
                    return
                }
                if (action.clearDesiredTarget) {
                    desiredConnection = null
                    attemptTracker.clearDesired()
                }
                disconnectInternal(updateState = false, clearSession = false)
                setError(
                    message = action.message,
                    target = connection.target,
                    displayName = connection.displayName,
                    computerId = connection.computerId,
                )
            }
            return
        }

        val authAction = AuthResponseHandler.commandAuthorizationError(payload)
        if (authAction != null) {
            tokenRepository.clearToken(connection.computerId)
            preferenceStores.sessions.clearPersistedSession()
            sessionMutex.withLock {
                if (!isCurrentAttemptLocked(connection.attemptId, connection.profile)) {
                    return
                }
                desiredConnection = null
                attemptTracker.clearDesired()
                disconnectInternal(updateState = false, clearSession = false)
                setError(
                    message = authAction.message,
                    target = connection.target,
                    displayName = connection.displayName,
                    computerId = connection.computerId,
                )
            }
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

    private suspend fun finalizeAuthenticatedConnection(
        connection: ActiveConnection,
        authOutcome: AuthenticationOutcome,
    ) {
        if (shouldAbortConnection(connection)) {
            return
        }

        Log.i(
            logTag,
            "connect_authenticated attemptId=${connection.attemptId} computerId=${connection.computerId} " +
                "outcome=${authOutcome::class.simpleName}",
        )

        reconnectJob = null
        ConnectionUiStateStore.setConnectingComputerId(null)
        applyTransition(
            SessionStateReducer.reduce(
                SessionStateReducer.Event.AuthSucceeded(
                    target = connection.target,
                    displayName = connection.displayName,
                    computerId = connection.computerId,
                ),
            ),
        )

        val device = StoredDevice(
            id = connection.computerId,
            name = connection.displayName,
            type = if (connection.target is ConnectionTarget.Wifi) DeviceType.WIFI else DeviceType.BLUETOOTH,
            host = (connection.target as? ConnectionTarget.Wifi)?.host,
            port = (connection.target as? ConnectionTarget.Wifi)?.port,
            address = (connection.target as? ConnectionTarget.Bluetooth)?.address,
            lastConnectedAt = System.currentTimeMillis(),
        )

        when (authOutcome) {
            is AuthenticationOutcome.Temporary -> {
                if (connection.profile.persistenceIntent == ProfilePersistenceIntent.EXISTING_SAVED_COMPUTER) {
                    preferenceStores.sessions.clearPersistedSession()
                }
                Log.i(logTag, "temporary authorization computerId=${connection.computerId}")
            }

            is AuthenticationOutcome.Persistent -> {
                val session = PersistedSession(
                    target = device,
                    uiRoute = UiRoute.REMOTE_SESSION,
                    autoRestore = true,
                    manuallyDisconnected = false,
                )
                val saved = runCatching {
                    preferenceStores.authorizedComputers.persistAuthorizedComputer(
                        device = device,
                        token = authOutcome.token,
                        persistedSession = session,
                        migrationCandidate = null,
                    )
                }
                if (saved.isFailure) {
                    Log.e(logTag, "Failed to persist authorization", saved.exceptionOrNull())
                    ConnectionUiStateStore.setStatus(
                        "Connected, but failed to save authorization on this Android device. " +
                            "You may need to approve again next time.",
                    )
                }
            }

            is AuthenticationOutcome.ExistingCredential -> {
                val session = PersistedSession(
                    target = device,
                    uiRoute = UiRoute.REMOTE_SESSION,
                    autoRestore = true,
                    manuallyDisconnected = false,
                )
                val candidate = connection.tokenCandidate
                val migrationCandidate = candidate?.takeIf { it.source !is TokenSource.ComputerProfile }
                val saved = runCatching {
                    preferenceStores.authorizedComputers.persistAuthorizedComputer(
                        device = device,
                        token = null,
                        persistedSession = session,
                        migrationCandidate = migrationCandidate,
                    )
                }
                if (saved.isFailure) {
                    Log.e(logTag, "Failed to persist existing credential session", saved.exceptionOrNull())
                    ConnectionUiStateStore.setStatus(
                        "Connected, but failed to save authorization on this Android device. " +
                            "You may need to approve again next time.",
                    )
                } else if (migrationCandidate != null) {
                    Log.i(
                        logTag,
                        "committed token migration computerId=${connection.computerId} " +
                            "source=${migrationCandidate.source::class.simpleName}",
                    )
                }
            }
        }
    }

    private fun setError(
        message: String,
        target: ConnectionTarget? = ConnectionUiStateStore.sessionTarget.value,
        displayName: String? = desiredConnection?.displayName,
        computerId: String? = desiredConnection?.computerId,
    ) {
        ConnectionUiStateStore.setConnectingComputerId(null)
        if (target != null) {
            ConnectionUiStateStore.setSessionTarget(target)
            ConnectionUiStateStore.setUiRoute(UiRoute.REMOTE_SESSION)
            runtimeScope.launch {
                persistedSessionCoordinator.persistLastError(message)
            }
        }
        applyTransition(
            SessionStateReducer.reduce(
                SessionStateReducer.Event.AuthFailed(
                    message = message,
                    target = target,
                    displayName = displayName,
                    computerId = computerId,
                ),
            ),
        )
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
                ConnectionUiStateStore.setConnectingComputerId(null)
            }
        }
    }

    private fun requestManualDisconnectLocked() {
        manualDisconnect = true
        desiredConnection = null
        attemptTracker.clearDesired()
        ConnectionUiStateStore.clearForManualDisconnect()
    }

    private fun cancelConnectWorkLocked() {
        connectJob?.cancel()
        connectJob = null
        closeOpeningTransportQuietly()
    }

    private fun closeOpeningTransportQuietly() {
        val close = openingTransportClose
        openingTransportClose = null
        if (close != null) {
            runCatching { close() }
        }
    }

    private fun isCurrentAttemptLocked(attemptId: Long, profile: ComputerConnectionProfile): Boolean {
        return attemptTracker.isCurrent(
            attemptId = attemptId,
            computerId = profile.computerId,
            manualDisconnect = manualDisconnect,
        ) && runtimeScope.coroutineContext.isActive
    }

    private fun shouldAbortConnection(connection: ActiveConnection): Boolean {
        return !attemptTracker.isCurrent(
            attemptId = connection.attemptId,
            computerId = connection.computerId,
            manualDisconnect = manualDisconnect,
        ) || !runtimeScope.coroutineContext.isActive
    }

    private fun handleUnexpectedDisconnect(connection: ActiveConnection, message: String) {
        runtimeScope.launch {
            sessionMutex.withLock {
                if (activeConnection !== connection || currentAttemptId != connection.attemptId) {
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
                        persistenceIntent = ProfilePersistenceIntent.EXISTING_SAVED_COMPUTER,
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
                setError(
                    message = "Connection lost: write failed.",
                    target = connection.target,
                    displayName = connection.displayName,
                    computerId = connection.computerId,
                )
            }
            return false
        } catch (_: Exception) {
            disconnectInternal(updateState = false, clearSession = false)
            if (reportError) {
                setError(
                    message = "Connection lost: write failed.",
                    target = connection.target,
                    displayName = connection.displayName,
                    computerId = connection.computerId,
                )
            }
            return false
        }
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
            persistenceIntent = ProfilePersistenceIntent.EXISTING_SAVED_COMPUTER,
        )
        desiredConnection = profile
        ConnectionUiStateStore.setSessionTarget(snapshot.target)
        ConnectionUiStateStore.setUiRoute(snapshot.uiRoute)
        snapshot.lastError?.let {
            ConnectionUiStateStore.setState(
                ConnectionState.Error(
                    message = it,
                    target = snapshot.target,
                    displayName = snapshot.computer.name,
                    computerId = snapshot.computer.id,
                ),
            )
            ConnectionUiStateStore.setStatus(it)
        }
    }

    private fun startReconnectLocked(profile: ComputerConnectionProfile, source: ReconnectSource) {
        cancelReconnectJobLocked()
        reconnectJob = runtimeScope.launch {
            delay(500)

            val attemptId = sessionMutex.withLock {
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

                if (connectJob?.isActive == true) {
                    reconnectJob = null
                    return@launch
                }

                val id = beginAttemptLocked(
                    profile = profile,
                    reason = source.connectReason,
                    reconnectAttempt = 1,
                )
                connectJob = runtimeScope.launch {
                    runConnectAttempt(
                        attemptId = id,
                        profile = profile,
                        reason = source.connectReason,
                        reconnectAttempt = 1,
                    )
                }
                id
            }

            // Wait for the connect job started above; do not hold sessionMutex while opening transport.
            connectJob?.join()

            sessionMutex.withLock {
                if (shouldAbortReconnectLocked(profile)) {
                    reconnectJob = null
                    return@withLock
                }
                if (activeConnection != null && ConnectionUiStateStore.state.value is ConnectionState.Connected) {
                    reconnectJob = null
                    return@withLock
                }

                // Fallback: one explicit retry if restore still failed.
                Log.i(logTag, "${source.logName} restore failed; retrying as explicit connect profile=$profile")
                val fallbackId = beginAttemptLocked(
                    profile = profile,
                    reason = ConnectReason.Explicit,
                    reconnectAttempt = 1,
                )
                connectJob = runtimeScope.launch {
                    runConnectAttempt(
                        attemptId = fallbackId,
                        profile = profile,
                        reason = ConnectReason.Explicit,
                        reconnectAttempt = 1,
                    )
                }
            }

            connectJob?.join()
            sessionMutex.withLock {
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
        return manualDisconnect ||
            desiredConnection?.computerId != profile.computerId ||
            !runtimeScope.coroutineContext.isActive
    }

    private companion object {
        private const val COMMAND_BUFFER_CAPACITY = 256
        private const val STICKY_COMBO_DURATION_MS = 300L
        private const val WIFI_CONNECT_TIMEOUT_MS = 15_000L
        private const val BLUETOOTH_CONNECT_TIMEOUT_MS = 45_000L
        private const val FOREGROUND_ENSURE_DEBOUNCE_MS = 750L
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
