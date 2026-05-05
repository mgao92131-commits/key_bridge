package com.bluetype.android.ui.screens

import android.view.HapticFeedbackConstants
import androidx.compose.animation.core.*
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsPressedAsState
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.runtime.withFrameNanos
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.TextRange
import androidx.compose.ui.hapticfeedback.HapticFeedback
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.input.pointer.PointerId
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.platform.LocalSoftwareKeyboardController
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.text.input.TextFieldValue
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.unit.IntOffset
import androidx.compose.foundation.pager.HorizontalPager
import androidx.compose.foundation.pager.rememberPagerState
import com.bluetype.android.domain.*
import com.bluetype.android.ui.components.TriRailCommandCenter
import com.bluetype.android.ui.theme.BlueTypeRoundedTokens
import com.bluetype.android.ui.theme.BlueTypeTheme
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlin.math.PI
import kotlin.math.atan2
import kotlin.math.ceil
import kotlin.math.floor
import kotlin.math.max
import kotlin.math.sqrt

@Composable
fun RemoteScreen(
    state: ConnectionState,
    sessionTarget: ConnectionTarget?,
    isInputEnabled: Boolean,
    draftText: String,
    onDraftChange: (String) -> Unit,
    onSendText: () -> Unit,
    onSendTextAndEnter: () -> Unit,
    onSendKey: (String) -> Unit,
    onSendKeyDown: (String) -> Unit,
    onSendKeyUp: (String) -> Unit,
    onSendCombo: (List<String>) -> Unit,
    onMouseMove: (Int, Int) -> Unit,
    onMouseButton: (String, Boolean) -> Unit,
    onMouseClick: (String, Int) -> Unit,
    onMouseScroll: (Int) -> Unit,
    onDisconnect: () -> Unit,
    profile: ShortcutProfile,
    profileTitle: String?,
) {
    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(BlueTypeTheme.colors.backgroundGradient)
            .statusBarsPadding()
            .navigationBarsPadding(),
    ) {
        MainWorkspace(
            state = state,
            sessionTarget = sessionTarget,
            draftText = draftText,
            onDraftChange = onDraftChange,
            onSendText = onSendText,
            onFullSend = onSendTextAndEnter,
            onSendKey = onSendKey,
            onSendKeyDown = onSendKeyDown,
            onSendKeyUp = onSendKeyUp,
            onSendCombo = onSendCombo,
            onMouseMove = onMouseMove,
            onMouseButton = onMouseButton,
            onMouseClick = onMouseClick,
            onMouseScroll = onMouseScroll,
            onDisconnect = onDisconnect,
            profile = profile,
            profileTitle = profileTitle,
        )
    }
}

@Composable
private fun StatusDot(state: ConnectionState) {
    val infiniteTransition = rememberInfiniteTransition(label = "dot_blink")
    val alpha by infiniteTransition.animateFloat(
        initialValue = 0.3f,
        targetValue = 1f,
        animationSpec = infiniteRepeatable(
            animation = tween(800, easing = LinearEasing),
            repeatMode = RepeatMode.Reverse
        ),
        label = "dot_alpha"
    )

    val isConnecting = state is ConnectionState.Connecting ||
            state is ConnectionState.Reconnecting ||
            state is ConnectionState.AwaitingApproval

    val color = when (state) {
        is ConnectionState.Connected -> Color(0xFF4CAF50)
        is ConnectionState.Error -> MaterialTheme.colorScheme.error
        is ConnectionState.Idle -> MaterialTheme.colorScheme.outline.copy(alpha = 0.25f)
        else -> MaterialTheme.colorScheme.primary
    }

    Box(
        modifier = Modifier
            .size(8.dp)
            .clip(CircleShape)
            .background(color.copy(alpha = if (isConnecting) alpha else 1f))
            .shadow(if (state is ConnectionState.Connected) 4.dp else 0.dp, CircleShape)
    )
}

@Composable
private fun MainWorkspace(
    state: ConnectionState,
    sessionTarget: ConnectionTarget?,
    draftText: String,
    onDraftChange: (String) -> Unit,
    onSendText: () -> Unit,
    onFullSend: () -> Unit,
    onSendKey: (String) -> Unit,
    onSendKeyDown: (String) -> Unit,
    onSendKeyUp: (String) -> Unit,
    onSendCombo: (List<String>) -> Unit,
    onMouseMove: (Int, Int) -> Unit,
    onMouseButton: (String, Boolean) -> Unit,
    onMouseClick: (String, Int) -> Unit,
    onMouseScroll: (Int) -> Unit,
    onDisconnect: () -> Unit,
    profile: ShortcutProfile,
    profileTitle: String?,
) {
    val focusRequester = remember { FocusRequester() }
    val keyboardController = LocalSoftwareKeyboardController.current
    val density = LocalDensity.current
    val haptic = LocalHapticFeedback.current
    val scope = rememberCoroutineScope()

    var isCtrlActive by remember { mutableStateOf(false) }
    var isAltActive by remember { mutableStateOf(false) }
    var isWinActive by remember { mutableStateOf(false) }
    var isShiftActive by remember { mutableStateOf(false) }
    var isFnActive by remember { mutableStateOf(false) }
    var editorValue by remember {
        mutableStateOf(TextFieldValue(draftText, selection = TextRange(draftText.length)))
    }

    LaunchedEffect(draftText) {
        if (draftText != editorValue.text) {
            editorValue = TextFieldValue(draftText, selection = TextRange(draftText.length))
        }
    }

    val autoClearModifiers = {
        if (isCtrlActive || isAltActive || isWinActive || isShiftActive || isFnActive) {
            isCtrlActive = false
            isAltActive = false
            isWinActive = false
            isShiftActive = false
            isFnActive = false
            haptic.performHapticFeedback(HapticFeedbackType.TextHandleMove)
        }
    }

    val anyModifierActive = isCtrlActive || isAltActive || isWinActive || isShiftActive || isFnActive

    fun updateEditor(value: TextFieldValue) {
        editorValue = value
        onDraftChange(value.text)
    }

    fun insertTextAtCursor(text: String) {
        if (text.isEmpty()) {
            return
        }

        val selection = editorValue.selection
        val start = minOf(selection.start, selection.end).coerceIn(0, editorValue.text.length)
        val end = maxOf(selection.start, selection.end).coerceIn(0, editorValue.text.length)
        val nextText = buildString {
            append(editorValue.text.substring(0, start))
            append(text)
            append(editorValue.text.substring(end))
        }
        val nextCursor = start + text.length
        updateEditor(TextFieldValue(nextText, selection = TextRange(nextCursor)))
    }

    val sendSmartKey = { key: String, shouldClear: Boolean ->
        val activeModifiers = mutableListOf<String>()
        if (isCtrlActive) activeModifiers.add("CTRL")
        if (isAltActive) activeModifiers.add("ALT")
        if (isWinActive) activeModifiers.add("WIN")
        if (isShiftActive) activeModifiers.add("SHIFT")

        if (activeModifiers.isEmpty()) {
            onSendKey(key)
        } else {
            activeModifiers.add(key)
            onSendCombo(activeModifiers)
            if (shouldClear) {
                autoClearModifiers()
            }
        }
    }

    val handleEsc = {
        if (anyModifierActive) {
            autoClearModifiers()
        } else {
            onSendKey("ESC")
        }
    }

    suspend fun executeAction(action: ShortcutAction) {
        when (action) {
            is ShortcutAction.KeyTap -> onSendKey(action.key)
            is ShortcutAction.Combo -> onSendCombo(action.keys)
            is ShortcutAction.TextInsert -> insertTextAtCursor(action.text)
            is ShortcutAction.Macro -> {
                action.sequence.forEach { step ->
                    executeAction(step)
                }
            }
            is ShortcutAction.Delay -> {
                delay(action.ms)
            }
        }
    }

    LaunchedEffect(Unit) {
        delay(300)
        focusRequester.requestFocus()
        keyboardController?.show()
    }

    BoxWithConstraints(modifier = Modifier.fillMaxSize()) {
        val parentHeight = maxHeight
        var reservedHeight by remember { mutableStateOf(parentHeight * 0.38f) }
        val keyboardHeight = with(density) { WindowInsets.ime.getBottom(density).toDp() }
        if (keyboardHeight > reservedHeight) {
            reservedHeight = keyboardHeight
        }

        Column(modifier = Modifier.fillMaxSize()) {
            Column(
                modifier = Modifier
                    .weight(1f)
                    .padding(horizontal = 12.dp)
                    .padding(top = 10.dp),
            ) {
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(bottom = 4.dp),
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    // 1. Device Name (Truncated)
                    Text(
                        text = profileTitle ?: deviceLabel(state, sessionTarget),
                        style = MaterialTheme.typography.titleSmall,
                        color = MaterialTheme.colorScheme.onBackground,
                        maxLines = 1,
                        overflow = androidx.compose.ui.text.style.TextOverflow.Ellipsis,
                        modifier = Modifier.weight(1f)
                    )

                    // 2. Fixed-Width Status Dot
                    Box(
                        modifier = Modifier.width(32.dp),
                        contentAlignment = Alignment.Center
                    ) {
                        StatusDot(state = state)
                    }

                    IconButton(
                        onClick = onDisconnect,
                        modifier = Modifier.size(32.dp),
                    ) {
                        Text(
                            "OUT",
                            fontSize = 10.sp,
                            color = MaterialTheme.colorScheme.onBackground.copy(alpha = 0.55f),
                        )
                    }
                }

                Spacer(modifier = Modifier.height(8.dp))

                TriRailCommandCenter(
                    value = editorValue,
                    onValueChange = { newValue ->
                        if (newValue.text.length > editorValue.text.length && (isCtrlActive || isAltActive || isWinActive)) {
                            val inserted = newValue.text.removePrefix(editorValue.text)
                            val char = inserted.lastOrNull()?.toString()?.uppercase().orEmpty()
                            if (char.isNotEmpty()) {
                                val combo = mutableListOf<String>()
                                if (isCtrlActive) combo.add("CTRL")
                                if (isAltActive) combo.add("ALT")
                                if (isWinActive) combo.add("WIN")
                                if (isShiftActive) combo.add("SHIFT")
                                combo.add(char)
                                onSendCombo(combo)
                                autoClearModifiers()
                            } else {
                                updateEditor(newValue)
                            }
                        } else {
                            updateEditor(newValue)
                        }
                    },
                    onInsertText = { text -> insertTextAtCursor(text) },
                    onSendText = onSendText,
                    onSendKey = onSendKey,
                    onSendKeyDown = onSendKeyDown,
                    onSendKeyUp = onSendKeyUp,
                    onSendCombo = onSendCombo,
                    profile = profile,
                    modifier = Modifier
                        .weight(1f)
                        .fillMaxWidth()
                        .offset(x = 0.dp),
                    focusRequester = focusRequester,
                )

                Spacer(modifier = Modifier.height(6.dp))

                val pagerState = rememberPagerState(pageCount = { 2 })

                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .height(115.dp),
                    horizontalArrangement = Arrangement.spacedBy(10.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    // Left Column: Indicator + Pager for buttons
                    Column(
                        modifier = Modifier.weight(1.3f),
                        verticalArrangement = Arrangement.spacedBy(4.dp),
                        horizontalAlignment = Alignment.CenterHorizontally
                    ) {
                        // Page Indicator ONLY above the buttons
                        Row(
                            Modifier.height(8.dp),
                            horizontalArrangement = Arrangement.Center
                        ) {
                            repeat(2) { iteration ->
                                val color = if (pagerState.currentPage == iteration)
                                    Color.White.copy(alpha = 0.6f)
                                else
                                    Color.White.copy(alpha = 0.15f)
                                Box(
                                    modifier = Modifier
                                        .padding(2.dp)
                                        .clip(CircleShape)
                                        .background(color)
                                        .size(4.dp)
                                )
                            }
                        }

                        HorizontalPager(
                            state = pagerState,
                            modifier = Modifier.fillMaxSize(),
                            verticalAlignment = Alignment.CenterVertically,
                        ) { page ->
                            if (page == 0) {
                                DefaultModifierGroup(
                                    isFnActive = isFnActive,
                                    isCtrlActive = isCtrlActive,
                                    isAltActive = isAltActive,
                                    isWinActive = isWinActive,
                                    isShiftActive = isShiftActive,
                                    onEsc = { handleEsc() },
                                    onTab = { sendSmartKey("TAB", false) },
                                    onFnToggle = { isFnActive = !isFnActive },
                                    onDel = { sendSmartKey("BACKSPACE", false) },
                                    onCtrlToggle = { isCtrlActive = !isCtrlActive },
                                    onAltToggle = { isAltActive = !isAltActive },
                                    onWinToggle = { isWinActive = !isWinActive },
                                    onShiftToggle = { isShiftActive = !isShiftActive },
                                    autoClearModifiers = autoClearModifiers
                                )
                            } else {
                                CustomShortcutGroup(
                                    buttons = profile.customButtons,
                                    onAction = { action ->
                                        scope.launch { executeAction(action) }
                                    }
                                )
                            }
                        }
                    }

                    Box(
                        modifier = Modifier
                            .width(0.5.dp)
                            .height(32.dp)
                            .background(Color.White.copy(alpha = 0.1f)),
                    )

                    Box(modifier = Modifier.weight(0.7f), contentAlignment = Alignment.Center) {
                        VirtualRedPoint(
                            onDirection = { direction ->
                                val key = when (direction) {
                                    "N" -> "UP"
                                    "S" -> "DOWN"
                                    "W" -> "LEFT"
                                    "E" -> "RIGHT"
                                    else -> ""
                                }
                                if (key.isNotEmpty()) {
                                    sendSmartKey(key, false)
                                }
                            },
                            onSend = onFullSend,
                            onRelease = autoClearModifiers,
                            modifier = Modifier.size(80.dp),
                        )
                    }
                }

                Spacer(modifier = Modifier.height(4.dp))
            }

            Box(
                modifier = Modifier
                    .fillMaxWidth()
                    .height(reservedHeight)
                    .background(MaterialTheme.colorScheme.surface.copy(alpha = 0.05f)),
            ) {
                TrackpadSection(
                    onMouseMove = onMouseMove,
                    onMouseButton = onMouseButton,
                    onMouseClick = onMouseClick,
                    onMouseScroll = onMouseScroll,
                    onRelease = autoClearModifiers,
                    modifier = Modifier
                        .fillMaxSize()
                        .padding(8.dp),
                )
            }
        }
    }
}

@Composable
private fun DefaultModifierGroup(
    isFnActive: Boolean,
    isCtrlActive: Boolean,
    isAltActive: Boolean,
    isWinActive: Boolean,
    isShiftActive: Boolean,
    onEsc: () -> Unit,
    onTab: () -> Unit,
    onFnToggle: () -> Unit,
    onDel: () -> Unit,
    onCtrlToggle: () -> Unit,
    onAltToggle: () -> Unit,
    onWinToggle: () -> Unit,
    onShiftToggle: () -> Unit,
    autoClearModifiers: () -> Unit
) {
    Column(
        modifier = Modifier.fillMaxSize(),
        verticalArrangement = Arrangement.spacedBy(6.dp),
    ) {
        Row(
            modifier = Modifier.weight(1f),
            horizontalArrangement = Arrangement.spacedBy(6.dp),
        ) {
            ModifierKey("ESC", false, onEsc, Modifier.weight(1f))
            ModifierKey("TAB", false, onTab, Modifier.weight(1f), onRelease = autoClearModifiers)
            ModifierKey("FN", isFnActive, onFnToggle, Modifier.weight(1f))
            ModifierKey("DEL", false, onDel, Modifier.weight(1f), onRelease = autoClearModifiers)
        }
        Row(
            modifier = Modifier.weight(1f),
            horizontalArrangement = Arrangement.spacedBy(6.dp),
        ) {
            ModifierKey("CTRL", isCtrlActive, onCtrlToggle, Modifier.weight(1f))
            ModifierKey("ALT", isAltActive, onAltToggle, Modifier.weight(1f))
            ModifierKey("WIN", isWinActive, onWinToggle, Modifier.weight(1f))
            ModifierKey("SHIFT", isShiftActive, onShiftToggle, Modifier.weight(1f))
        }
    }
}

@Composable
private fun CustomShortcutGroup(
    buttons: List<CustomShortcutBtn>,
    onAction: (ShortcutAction) -> Unit
) {
    Column(
        modifier = Modifier.fillMaxSize(),
        verticalArrangement = Arrangement.spacedBy(6.dp),
    ) {
        // Render 2 rows of up to 4 buttons each
        val rows = buttons.chunked(4).take(2)
        rows.forEach { rowButtons ->
            Row(
                modifier = Modifier.weight(1f),
                horizontalArrangement = Arrangement.spacedBy(6.dp),
            ) {
                rowButtons.forEach { btn ->
                    ModifierKey(
                        label = btn.label,
                        isActive = false,
                        onClick = { onAction(btn.action) },
                        modifier = Modifier.weight(1f)
                    )
                }
                // Fill remaining space if row is not full
                if (rowButtons.size < 4) {
                    repeat(4 - rowButtons.size) {
                        Spacer(modifier = Modifier.weight(1f))
                    }
                }
            }
        }
        // If less than 2 rows, add a spacer to maintain height consistency
        if (rows.size < 2) {
            Spacer(modifier = Modifier.weight(1f))
        }
    }
}

@Composable
private fun RemoteStatusMessage(
    lastFeedback: CommandFeedback?,
) {
    val feedback = lastFeedback
    val message = when {
        feedback?.state == CommandFeedbackState.FAILED -> feedback.message
        feedback?.state == CommandFeedbackState.SUCCEEDED -> feedback.message
        else -> null
    } ?: return

    val color = when (feedback?.state) {
        CommandFeedbackState.FAILED -> MaterialTheme.colorScheme.error
        CommandFeedbackState.SUCCEEDED -> Color(0xFF7ADFA0)
        else -> MaterialTheme.colorScheme.onBackground.copy(alpha = 0.58f)
    }

    Text(
        text = message,
        style = MaterialTheme.typography.labelSmall,
        color = color,
        maxLines = 2,
        overflow = androidx.compose.ui.text.style.TextOverflow.Ellipsis,
        modifier = Modifier
            .fillMaxWidth()
            .padding(top = 2.dp),
    )
}

@Composable
private fun ModifierKey(
    label: String,
    isActive: Boolean,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    onRelease: (() -> Unit)? = null,
) {
    val haptic = LocalHapticFeedback.current
    val interactionSource = remember { MutableInteractionSource() }
    val isPressed by interactionSource.collectIsPressedAsState()
    val verticalOffset by animateDpAsState(if (isPressed) 2.dp else 0.dp, label = "sink")

    LaunchedEffect(isPressed) {
        if (!isPressed) {
            onRelease?.invoke()
        }
    }

    Box(
        modifier = modifier
            .fillMaxSize()
            .clickable(
                interactionSource = interactionSource,
                indication = null,
                onClick = {
                    haptic.performHapticFeedback(HapticFeedbackType.TextHandleMove)
                    onClick()
                },
            ),
    ) {
        Surface(
            modifier = Modifier
                .fillMaxSize()
                .padding(top = 2.dp),
            color = if (isActive) Color(0xFF9E1D1D) else Color.Black.copy(alpha = 0.5f),
            shape = RoundedCornerShape(8.dp),
        ) {}

        Surface(
            modifier = Modifier
                .fillMaxSize()
                .padding(bottom = 2.dp)
                .offset(y = verticalOffset),
            color = if (isActive) Color(0xFFE62E2D) else MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.15f),
            shape = RoundedCornerShape(8.dp),
            border = BorderStroke(0.5.dp, Color.White.copy(alpha = 0.05f)),
        ) {
            Box(contentAlignment = Alignment.Center) {
                if (isActive) {
                    Box(
                        modifier = Modifier
                            .align(Alignment.TopEnd)
                            .padding(4.dp)
                            .size(4.dp)
                            .background(Color.White, CircleShape),
                    )
                }
                Text(
                    text = label,
                    style = if (label.length == 1) {
                        MaterialTheme.typography.titleMedium.copy(fontSize = 22.sp)
                    } else {
                        MaterialTheme.typography.labelSmall
                    },
                    color = if (isActive) Color.White else Color.White.copy(alpha = 0.7f),
                )
            }
        }
    }
}

@Composable
private fun VirtualRedPoint(
    onDirection: (String) -> Unit,
    onSend: () -> Unit,
    onRelease: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val haptic = LocalHapticFeedback.current
    var offset by remember { mutableStateOf(Offset.Zero) }
    val maxOffset = 50f
    val triggerThreshold = 15f
    var isPressed by remember { mutableStateOf(false) }
    var lastTriggeredDir by remember { mutableStateOf("") }

    Box(
        modifier = modifier.pointerInput(Unit) {
            awaitEachGesture {
                val down = awaitFirstDown()
                isPressed = true
                lastTriggeredDir = ""

                do {
                    val event = awaitPointerEvent()
                    val change = event.changes.firstOrNull { it.id == down.id } ?: break
                    if (change.pressed) {
                        val dx = change.position.x - change.previousPosition.x
                        val dy = change.position.y - change.previousPosition.y
                        offset = Offset(
                            x = (offset.x + dx).coerceIn(-maxOffset, maxOffset),
                            y = (offset.y + dy).coerceIn(-maxOffset, maxOffset),
                        )

                        val distance = sqrt(offset.x * offset.x + offset.y * offset.y)
                        if (distance > triggerThreshold) {
                            val angle = atan2(offset.y, offset.x) * (180 / PI)
                            val currentDir = when {
                                angle in -135.0..-45.0 -> "N"
                                angle in 45.0..135.0 -> "S"
                                angle > 135.0 || angle < -135.0 -> "W"
                                else -> "E"
                            }
                            if (currentDir != lastTriggeredDir) {
                                onDirection(currentDir)
                                lastTriggeredDir = currentDir
                                haptic.performHapticFeedback(HapticFeedbackType.TextHandleMove)
                            }
                        } else {
                            lastTriggeredDir = ""
                        }
                        change.consume()
                    }
                } while (event.changes.any { it.pressed })

                val totalDistance = sqrt(offset.x * offset.x + offset.y * offset.y)
                if (totalDistance < 10f) {
                    onSend()
                }

                isPressed = false
                offset = Offset.Zero
                onRelease()
            }
        },
        contentAlignment = Alignment.Center,
    ) {
        androidx.compose.foundation.Canvas(modifier = Modifier.size(100.dp)) {
            drawCircle(
                color = Color.Black.copy(alpha = 0.1f),
                radius = 45.dp.toPx(),
                center = Offset(size.width / 2, size.height / 2),
            )
        }
        Box(
            modifier = Modifier
                .size(64.dp)
                .graphicsLayer {
                    translationX = offset.x
                    translationY = offset.y
                    rotationX = -(offset.y / maxOffset) * 20f
                    rotationY = (offset.x / maxOffset) * 20f
                    scaleX = if (isPressed) 0.95f else 1f
                    scaleY = if (isPressed) 0.95f else 1f
                }
                .shadow(if (isPressed) 4.dp else 12.dp, CircleShape)
                .background(
                    brush = Brush.radialGradient(
                        colors = listOf(Color(0xFFFF5252), Color(0xFFE62E2D), Color(0xFFB71C1C)),
                    ),
                    shape = CircleShape,
                )
                .border(1.dp, Color.White.copy(alpha = 0.2f), CircleShape),
        )
    }
}

@Composable
private fun TrackpadSection(
    onMouseMove: (Int, Int) -> Unit,
    onMouseButton: (String, Boolean) -> Unit,
    onMouseClick: (String, Int) -> Unit,
    onMouseScroll: (Int) -> Unit,
    onRelease: () -> Unit,
    modifier: Modifier = Modifier,
) {
    Surface(
        color = MaterialTheme.colorScheme.surface.copy(alpha = 0.84f),
        shape = BlueTypeRoundedTokens.cornerXXL,
        modifier = modifier
            .fillMaxWidth()
            .shadow(18.dp, BlueTypeRoundedTokens.cornerXXL),
    ) {
        TrackpadSurface(
            onMouseMove = onMouseMove,
            onMouseButton = onMouseButton,
            onMouseClick = onMouseClick,
            onMouseScroll = onMouseScroll,
            onRelease = onRelease,
            modifier = Modifier.fillMaxSize(),
        )
    }
}

@Composable
private fun TrackpadSurface(
    onMouseMove: (Int, Int) -> Unit,
    onMouseButton: (String, Boolean) -> Unit,
    onMouseClick: (String, Int) -> Unit,
    onMouseScroll: (Int) -> Unit,
    onRelease: () -> Unit,
    modifier: Modifier = Modifier,
) {
    var pendingMove by remember { mutableStateOf(IntOffset.Zero) }
    var pendingScroll by remember { mutableIntStateOf(0) }
    var dragVisualActive by remember { mutableStateOf(false) }
    val haptic = LocalHapticFeedback.current
    val view = LocalView.current
    val touchSlop = androidx.compose.ui.platform.LocalViewConfiguration.current.touchSlop

    LaunchedEffect(onMouseMove, onMouseScroll) {
        while (true) {
            withFrameNanos { }
            if (pendingMove != IntOffset.Zero) {
                onMouseMove(pendingMove.x, pendingMove.y)
                pendingMove = IntOffset.Zero
            }
            if (pendingScroll != 0) {
                onMouseScroll(pendingScroll)
                pendingScroll = 0
            }
        }
    }

    Box(
        modifier = modifier
            .clip(BlueTypeRoundedTokens.cornerXL)
            .background(MaterialTheme.colorScheme.surfaceContainerLowest.copy(alpha = 0.72f))
            .border(
                width = 1.dp,
                color = if (dragVisualActive) {
                    MaterialTheme.colorScheme.primary.copy(alpha = 0.56f)
                } else {
                    BlueTypeTheme.colors.ghostStroke
                },
                shape = BlueTypeRoundedTokens.cornerXL,
            )
            .pointerInput(onMouseMove, onMouseButton, onMouseScroll, touchSlop) {
                awaitEachGesture {
                    val firstDown = awaitFirstDown(requireUnconsumed = false)
                    var gestureMode = TrackpadGestureMode.TapCandidate
                    var maximumPointers = 1
                    var carryMoveX = 0f
                    var carryMoveY = 0f
                    var carryScroll = 0f
                    var isDragging = false
                    var shouldTapOnRelease = true
                    var activePointerId: PointerId = firstDown.id
                    var twoFingerStart = firstDown.position

                    while (true) {
                        val event = awaitPointerEvent()
                        val pressed = event.changes.filter { it.pressed }
                        if (pressed.isEmpty()) {
                            break
                        }

                        maximumPointers = max(maximumPointers, max(pressed.size, event.changes.size))
                        val primaryChange = event.changes.firstOrNull { it.id == activePointerId } ?: pressed.first()
                        if (!primaryChange.pressed) {
                            if (gestureMode == TrackpadGestureMode.TapCandidate || gestureMode == TrackpadGestureMode.TwoFingerTapCandidate) {
                                activePointerId = pressed.first().id
                                continue
                            }
                            shouldTapOnRelease = false
                            break
                        }

                        when (gestureMode) {
                            TrackpadGestureMode.TapCandidate -> {
                                if (max(pressed.size, event.changes.size) >= 2) {
                                    gestureMode = TrackpadGestureMode.TwoFingerTapCandidate
                                    twoFingerStart = (event.changes[0].position + event.changes[1].position) / 2f
                                } else {
                                    val elapsed = primaryChange.uptimeMillis - firstDown.uptimeMillis
                                    val displacement = primaryChange.position - firstDown.position
                                    if (elapsed >= 600L && displacement.getDistance() < touchSlop) {
                                        gestureMode = TrackpadGestureMode.Dragging
                                        shouldTapOnRelease = false
                                        isDragging = true
                                        dragVisualActive = true
                                        onMouseButton("LEFT", true)
                                        performTrackpadHapticFeedback(haptic, view)
                                    } else if (displacement.getDistance() > touchSlop) {
                                        gestureMode = TrackpadGestureMode.MoveCursor
                                        shouldTapOnRelease = false
                                    }
                                }
                            }

                            TrackpadGestureMode.TwoFingerTapCandidate -> {
                                if (pressed.size >= 2) {
                                    val midpoint = (event.changes[0].position + event.changes[1].position) / 2f
                                    if ((midpoint - twoFingerStart).getDistance() > touchSlop) {
                                        gestureMode = TrackpadGestureMode.Scroll
                                        shouldTapOnRelease = false
                                    }
                                }
                            }

                            else -> Unit
                        }

                        when (gestureMode) {
                            TrackpadGestureMode.MoveCursor, TrackpadGestureMode.Dragging -> {
                                val dx = primaryChange.position.x - primaryChange.previousPosition.x
                                val dy = primaryChange.position.y - primaryChange.previousPosition.y
                                val scaledX = carryMoveX + (dx * 1.45f)
                                val scaledY = carryMoveY + (dy * 1.45f)
                                val wholeX = if (scaledX > 0f) floor(scaledX).toInt() else ceil(scaledX).toInt()
                                val wholeY = if (scaledY > 0f) floor(scaledY).toInt() else ceil(scaledY).toInt()
                                carryMoveX = scaledX - wholeX
                                carryMoveY = scaledY - wholeY
                                if (wholeX != 0 || wholeY != 0) {
                                    pendingMove = IntOffset(pendingMove.x + wholeX, pendingMove.y + wholeY)
                                }
                            }

                            TrackpadGestureMode.Scroll -> {
                                if (pressed.size >= 2) {
                                    val averageDy = pressed.map { it.position.y - it.previousPosition.y }.average().toFloat()
                                    val scaledScroll = carryScroll + (-averageDy * 0.1f)
                                    val wholeScroll = if (scaledScroll > 0f) floor(scaledScroll).toInt() else ceil(scaledScroll).toInt()
                                    carryScroll = scaledScroll - wholeScroll
                                    if (wholeScroll != 0) {
                                        pendingScroll += wholeScroll
                                    }
                                }
                            }

                            else -> Unit
                        }

                        if (primaryChange.pressed) {
                            primaryChange.consume()
                        }
                    }

                    if (isDragging) {
                        onMouseButton("LEFT", false)
                        dragVisualActive = false
                    }
                    if (shouldTapOnRelease) {
                        val button = if (maximumPointers >= 2) "RIGHT" else "LEFT"
                        onMouseClick(button, 1)
                    }
                    onRelease()
                }
            },
        contentAlignment = Alignment.Center,
    ) {
        Box(
            modifier = Modifier
                .size(132.dp)
                .border(
                    width = 1.dp,
                    color = if (dragVisualActive) {
                        MaterialTheme.colorScheme.primary.copy(alpha = 0.52f)
                    } else {
                        BlueTypeTheme.colors.ghostStroke
                    },
                    shape = CircleShape,
                ),
            contentAlignment = Alignment.Center,
        ) {
            Box(
                modifier = Modifier
                    .size(if (dragVisualActive) 18.dp else 8.dp)
                    .clip(CircleShape)
                    .background(if (dragVisualActive) MaterialTheme.colorScheme.primary else BlueTypeTheme.colors.trackpadDot),
            )
        }
    }
}

private fun performTrackpadHapticFeedback(haptic: HapticFeedback, view: android.view.View) {
    haptic.performHapticFeedback(HapticFeedbackType.LongPress)
    view.performHapticFeedback(HapticFeedbackConstants.LONG_PRESS)
}

private fun deviceLabel(state: ConnectionState, sessionTarget: ConnectionTarget?): String {
    val target = when (state) {
        is ConnectionState.Connected -> state.target
        is ConnectionState.Connecting -> state.target
        is ConnectionState.AwaitingApproval -> state.target
        is ConnectionState.Reconnecting -> state.target
        else -> sessionTarget
    }

    return target?.shortLabel() ?: "DISCONNECTED"
}

private fun ConnectionTarget.shortLabel(): String {
    return when (this) {
        is ConnectionTarget.Bluetooth -> name
            .replace(Regex("\\s*\\([^)]*\\)\\s*$"), "")
            .replace(Regex("\\s+"), " ")
            .trim()
            .ifBlank { "Bluetooth" }

        is ConnectionTarget.Wifi -> host
    }
}

private enum class TrackpadGestureMode {
    TapCandidate,
    TwoFingerTapCandidate,
    MoveCursor,
    Scroll,
    Dragging,
}
