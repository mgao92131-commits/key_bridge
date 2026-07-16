package com.bluetype.android.ui.components

import android.view.HapticFeedbackConstants
import androidx.compose.animation.*
import androidx.compose.animation.core.*
import androidx.compose.foundation.*
import androidx.compose.foundation.gestures.*
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.draw.drawWithContent
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.*
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.input.pointer.*
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.TextFieldValue
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.bluetype.android.domain.RailConfig
import com.bluetype.android.domain.ShortcutAction
import com.bluetype.android.domain.ShortcutProfile
import com.bluetype.android.ui.theme.BlueTypeTheme
import com.bluetype.android.ui.theme.BlueTypeRoundedTokens
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun TriRailCommandCenter(
    value: TextFieldValue,
    onValueChange: (TextFieldValue) -> Unit,
    onInsertText: (String) -> Unit,
    onSendText: () -> Unit,
    onSendKey: (String) -> Unit,
    onSendKeyDown: (String) -> Unit,
    onSendKeyUp: (String) -> Unit,
    onSendCombo: (List<String>) -> Unit,
    profile: ShortcutProfile,
    modifier: Modifier = Modifier,
    focusRequester: FocusRequester = remember { FocusRequester() }
) {
    val haptic = LocalHapticFeedback.current
    val view = LocalView.current
    val scope = rememberCoroutineScope()

    var activeRail by remember { mutableStateOf(DragMode.None) }
    var isRailArmed by remember { mutableStateOf(false) }
    var stickyMode by remember { mutableStateOf<DragMode>(DragMode.None) }
    var releaseJob by remember { mutableStateOf<kotlinx.coroutines.Job?>(null) }

    fun getRailConfig(mode: DragMode): RailConfig? = when (mode) {
        DragMode.LeftRail -> profile.leftRail
        DragMode.RightRail -> profile.rightRail
        DragMode.BottomRail -> profile.bottomRail
        else -> null
    }

    fun releaseModifiers(mode: DragMode) {
        getRailConfig(mode)?.stickyModifiers?.forEach { onSendKeyUp(it) }
    }

    fun pressModifiers(mode: DragMode) {
        getRailConfig(mode)?.stickyModifiers?.forEach { onSendKeyDown(it) }
    }

    fun armRail(mode: DragMode) {
        releaseJob?.cancel()
        releaseJob = null

        when {
            stickyMode == mode -> Unit
            stickyMode != DragMode.None -> {
                releaseModifiers(stickyMode)
                pressModifiers(mode)
                stickyMode = mode
            }
            else -> {
                pressModifiers(mode)
                stickyMode = mode
            }
        }
    }

    fun scheduleStickyRelease(mode: DragMode) {
        val stickyDuration = getRailConfig(mode)?.stickyDurationMs ?: 600L

        if (mode != DragMode.None) {
            releaseJob?.cancel()
            releaseJob = scope.launch {
                delay(stickyDuration)
                if (stickyMode == mode) {
                    releaseModifiers(mode)
                    stickyMode = DragMode.None
                }
            }
        }
    }

    suspend fun executeAction(action: ShortcutAction) {
        when (action) {
            is ShortcutAction.KeyTap -> onSendKey(action.key)
            is ShortcutAction.Combo -> onSendCombo(action.keys)
            is ShortcutAction.TextInsert -> onInsertText(action.text)
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

    fun runRailAction(mode: DragMode, action: ShortcutAction?) {
        if (action == null) {
            return
        }

        armRail(mode)
        activeRail = mode
        isRailArmed = true

        scope.launch { executeAction(action) }

        haptic.performHapticFeedback(HapticFeedbackType.TextHandleMove)
        view.performHapticFeedback(HapticFeedbackConstants.CLOCK_TICK)
    }

    fun finishRailAction(mode: DragMode) {
        activeRail = DragMode.None
        isRailArmed = false
        scheduleStickyRelease(mode)
    }

    Box(
        modifier = modifier
            .clip(RoundedCornerShape(0.dp)) // Edge-to-edge, no corner rounding for the main container
            .background(Color(0xFF0A0A0A))
            .carbonFiberTexture()
            .draw3DInset()
    ) {
        RailActionButtons(
            profile = profile,
            activeRail = activeRail,
            isRailArmed = isRailArmed,
            stickyMode = stickyMode,
            onPress = { mode, action -> runRailAction(mode, action) },
            onRelease = { mode -> finishRailAction(mode) },
        )

        Box(
            modifier = Modifier
                .fillMaxSize()
                .padding(start = SideRailWidth, end = SideRailWidth, bottom = BottomRailHeight, top = 2.dp)
                .background(Color.Black.copy(alpha = 0.6f), RoundedCornerShape(bottomStart = 12.dp, bottomEnd = 12.dp))
                .border(1.dp, Color.White.copy(alpha = 0.08f), RoundedCornerShape(bottomStart = 12.dp, bottomEnd = 12.dp))
        ) {
            TextField(
                value = value,
                onValueChange = onValueChange,
                modifier = Modifier
                    .fillMaxSize()
                    .focusRequester(focusRequester),
                colors = TextFieldDefaults.colors(
                    focusedContainerColor = Color.Transparent,
                    unfocusedContainerColor = Color.Transparent,
                    focusedIndicatorColor = Color.Transparent,
                    unfocusedIndicatorColor = Color.Transparent,
                    cursorColor = Color(0xFFE62E2D),
                ),
                textStyle = MaterialTheme.typography.bodyMedium.copy(
                    fontFamily = FontFamily.Monospace,
                    color = Color.White.copy(alpha = 0.9f),
                    fontSize = 14.sp
                ),
                placeholder = { 
                    Text(
                        "READY...", 
                        fontSize = 12.sp, 
                        color = Color.White.copy(alpha = 0.15f),
                        fontFamily = FontFamily.Monospace
                    ) 
                },
                keyboardOptions = KeyboardOptions(
                    imeAction = ImeAction.Send,
                    autoCorrect = false,
                    keyboardType = KeyboardType.Ascii
                ),
                keyboardActions = KeyboardActions(onSend = { onSendText() })
            )
        }
    }
}

@Composable
private fun RailActionButtons(
    profile: ShortcutProfile,
    activeRail: DragMode,
    isRailArmed: Boolean,
    stickyMode: DragMode,
    onPress: (DragMode, ShortcutAction?) -> Unit,
    onRelease: (DragMode) -> Unit,
) {
    Box(modifier = Modifier.fillMaxSize()) {
        Column(
            modifier = Modifier
                .align(Alignment.CenterStart)
                .width(SideRailWidth)
                .fillMaxHeight(),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.Center,
        ) {
            RailActionButton(
                label = "L1",
                mode = DragMode.LeftRail,
                action = profile.leftRail.primaryAction,
                isActive = activeRail == DragMode.LeftRail && isRailArmed,
                isSticky = stickyMode == DragMode.LeftRail,
                onPress = onPress,
                onRelease = onRelease,
                modifier = Modifier.weight(1f),
            )
            RailButtonDivider(isVerticalGroup = true)
            RailActionButton(
                label = "L2",
                mode = DragMode.LeftRail,
                action = profile.leftRail.secondaryAction,
                isActive = activeRail == DragMode.LeftRail && isRailArmed,
                isSticky = stickyMode == DragMode.LeftRail,
                onPress = onPress,
                onRelease = onRelease,
                modifier = Modifier.weight(1f),
            )
        }

        Column(
            modifier = Modifier
                .align(Alignment.CenterEnd)
                .width(SideRailWidth)
                .fillMaxHeight(),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.Center,
        ) {
            RailActionButton(
                label = "R1",
                mode = DragMode.RightRail,
                action = profile.rightRail.primaryAction,
                isActive = activeRail == DragMode.RightRail && isRailArmed,
                isSticky = stickyMode == DragMode.RightRail,
                onPress = onPress,
                onRelease = onRelease,
                modifier = Modifier.weight(1f),
            )
            RailButtonDivider(isVerticalGroup = true)
            RailActionButton(
                label = "R2",
                mode = DragMode.RightRail,
                action = profile.rightRail.secondaryAction,
                isActive = activeRail == DragMode.RightRail && isRailArmed,
                isSticky = stickyMode == DragMode.RightRail,
                onPress = onPress,
                onRelease = onRelease,
                modifier = Modifier.weight(1f),
            )
        }

        Row(
            modifier = Modifier
                .align(Alignment.BottomCenter)
                .height(BottomRailHeight)
                .fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.Center,
        ) {
            Spacer(modifier = Modifier.width(SideRailWidth))
            RailActionButton(
                label = "B1",
                mode = DragMode.BottomRail,
                action = profile.bottomRail.primaryAction,
                isActive = activeRail == DragMode.BottomRail && isRailArmed,
                isSticky = stickyMode == DragMode.BottomRail,
                onPress = onPress,
                onRelease = onRelease,
                modifier = Modifier.weight(1f),
            )
            RailButtonDivider(isVerticalGroup = false)
            RailActionButton(
                label = "B2",
                mode = DragMode.BottomRail,
                action = profile.bottomRail.secondaryAction,
                isActive = activeRail == DragMode.BottomRail && isRailArmed,
                isSticky = stickyMode == DragMode.BottomRail,
                onPress = onPress,
                onRelease = onRelease,
                modifier = Modifier.weight(1f),
            )
            Spacer(modifier = Modifier.width(SideRailWidth))
        }
    }
}

@Composable
private fun RailActionButton(
    label: String,
    mode: DragMode,
    action: ShortcutAction?,
    isActive: Boolean,
    isSticky: Boolean,
    onPress: (DragMode, ShortcutAction?) -> Unit,
    onRelease: (DragMode) -> Unit,
    modifier: Modifier = Modifier,
) {
    val scope = rememberCoroutineScope()
    var repeatJob by remember { mutableStateOf<kotlinx.coroutines.Job?>(null) }
    var isPressed by remember { mutableStateOf(false) }

    DisposableEffect(Unit) {
        onDispose {
            repeatJob?.cancel()
        }
    }

    val textAlpha = when {
        isActive -> 0.78f
        isSticky -> 0.58f
        action != null -> 0.34f
        else -> 0.16f
    }

    Box(
        modifier = modifier
            .fillMaxSize()
            .pointerInput(action, mode) {
                awaitEachGesture {
                    val down = awaitFirstDown()
                    isPressed = true
                    onPress(mode, action)

                    repeatJob?.cancel()
                    repeatJob = scope.launch {
                        delay(RepeatInitialDelayMs)
                        while (true) {
                            onPress(mode, action)
                            delay(RepeatIntervalMs)
                        }
                    }

                    var isReleased = false
                    while (!isReleased) {
                        val event = awaitPointerEvent()
                        val change = event.changes.firstOrNull { it.id == down.id }
                        isReleased = change == null || !change.pressed
                        change?.consume()
                    }

                    repeatJob?.cancel()
                    repeatJob = null
                    isPressed = false
                    onRelease(mode)
                }
            }
            .padding(horizontal = 2.dp),
        contentAlignment = Alignment.Center,
    ) {
        Text(
            text = label,
            color = Color.White.copy(alpha = if (isPressed) 0.9f else textAlpha),
            fontSize = 10.sp,
            fontWeight = FontWeight.Bold,
            maxLines = 1,
        )
    }
}

@Composable
private fun RailButtonDivider(isVerticalGroup: Boolean) {
    Box(
        modifier = if (isVerticalGroup) {
            Modifier
                .fillMaxWidth()
                .padding(horizontal = 10.dp)
                .height(0.5.dp)
                .background(Color.White.copy(alpha = 0.12f))
        } else {
            Modifier
                .fillMaxHeight()
                .padding(vertical = 8.dp)
                .width(0.5.dp)
                .background(Color.White.copy(alpha = 0.12f))
        }
    )
}

private enum class DragMode { None, LeftRail, RightRail, BottomRail }

private val SideRailWidth = 42.dp
private val BottomRailHeight = 38.dp
private const val RepeatInitialDelayMs = 350L
private const val RepeatIntervalMs = 120L

// Modifiers for visuals

fun Modifier.carbonFiberTexture() = drawBehind {
    val tileSize = 10.dp.toPx()
    val columns = (size.width / tileSize).toInt() + 1
    val rows = (size.height / tileSize).toInt() + 1
    
    val darkColor = Color(0xFF121212)
    val lightColor = Color(0xFF1A1A1A)

    for (c in 0 until columns) {
        for (r in 0 until rows) {
            val offset = Offset(c * tileSize, r * tileSize)
            val isEven = (c + r) % 2 == 0
            drawRect(
                color = if (isEven) darkColor else lightColor,
                topLeft = offset,
                size = Size(tileSize, tileSize)
            )
            // Add some diagonal lines for more "fiber" look
            drawLine(
                color = Color.White.copy(alpha = 0.05f),
                start = offset,
                end = offset + Offset(tileSize, tileSize),
                strokeWidth = 1f
            )
        }
    }
}

fun Modifier.draw3DInset() = drawWithContent {
    drawContent()
    
    // Inset shadow (top/left dark, bottom/right light)
    val strokeWidth = 1.dp.toPx()
    
    // Inner bevel shadow
    drawRoundRect(
        color = Color.Black.copy(alpha = 0.6f),
        topLeft = Offset.Zero,
        size = size,
        cornerRadius = CornerRadius(12.dp.toPx()),
        style = Stroke(width = strokeWidth * 2)
    )
    
    // Top highlight edge
    drawLine(
        color = Color.White.copy(alpha = 0.1f),
        start = Offset(0f, 0f),
        end = Offset(size.width, 0f),
        strokeWidth = strokeWidth
    )
}

fun Modifier.drawLEDGlows(leftAlpha: Float, rightAlpha: Float, bottomAlpha: Float) = drawWithContent {
    drawContent()

    if (leftAlpha > 0f) {
        drawRect(
            brush = Brush.horizontalGradient(
                colors = listOf(Color(0xFF00BFFF).copy(alpha = leftAlpha), Color.Transparent),
                startX = 0f,
                endX = SideRailWidth.toPx()
            ),
            topLeft = Offset.Zero,
            size = Size(SideRailWidth.toPx(), size.height)
        )
    }

    if (rightAlpha > 0f) {
        drawRect(
            brush = Brush.horizontalGradient(
                colors = listOf(Color.Transparent, Color(0xFF00BFFF).copy(alpha = rightAlpha)),
                startX = size.width - SideRailWidth.toPx(),
                endX = size.width
            ),
            topLeft = Offset(size.width - SideRailWidth.toPx(), 0f),
            size = Size(SideRailWidth.toPx(), size.height)
        )
    }

    if (bottomAlpha > 0f) {
        drawRect(
            brush = Brush.verticalGradient(
                colors = listOf(Color.Transparent, Color(0xFFFFBF00).copy(alpha = bottomAlpha)),
                startY = size.height - BottomRailHeight.toPx(),
                endY = size.height
            ),
            topLeft = Offset(0f, size.height - BottomRailHeight.toPx()),
            size = Size(size.width, BottomRailHeight.toPx())
        )
    }
}
