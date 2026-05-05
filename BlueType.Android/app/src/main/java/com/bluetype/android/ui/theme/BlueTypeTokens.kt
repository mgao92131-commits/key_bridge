package com.bluetype.android.ui.theme

import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp

data class BlueTypeColors(
    val backgroundGradient: Brush,
    val sendButtonGradient: Brush,
    val ambientShadow: Color,
    val ghostStroke: Color,
    val trackpadOrb: Color,
    val trackpadDot: Color,
)

fun darkBlueTypeColors() = BlueTypeColors(
    backgroundGradient = Brush.verticalGradient(
        colors = listOf(
            Color(0xFF1B1B1F),
            Color(0xFF1F1F23),
            Color(0xFF1B1B1F),
        ),
    ),
    sendButtonGradient = Brush.linearGradient(
        colors = listOf(
            Color(0xFFC7BDF0),
            Color(0xFFA49BCB),
        ),
    ),
    ambientShadow = Color(0x33000000),
    ghostStroke = Color(0x26FFFFFF),
    trackpadOrb = Color(0xFF343438),
    trackpadDot = Color(0xFF48454F),
)

// 默认提供深色配色
val LocalBlueTypeColors = staticCompositionLocalOf { darkBlueTypeColors() }

object BlueTypeRoundedTokens {
    val cornerSmall = RoundedCornerShape(12.dp)
    val cornerMedium = RoundedCornerShape(18.dp)
    val cornerLarge = RoundedCornerShape(24.dp)
    val cornerXL = RoundedCornerShape(30.dp)
    val cornerXXL = RoundedCornerShape(36.dp)
    val pill = RoundedCornerShape(50)
}
