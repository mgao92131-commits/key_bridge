package com.bluetype.android.ui.theme

import android.app.Activity
import androidx.compose.material3.ColorScheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Shapes
import androidx.compose.runtime.Composable
import androidx.compose.runtime.SideEffect
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.toArgb
import androidx.compose.ui.platform.LocalView
import androidx.compose.runtime.CompositionLocalProvider
import androidx.core.view.WindowCompat

private val BlueTypeDarkColors = editorialDarkColorScheme()

object BlueTypeTheme {
    val colors: BlueTypeColors
        @Composable
        get() = LocalBlueTypeColors.current
}

@Composable
fun BlueTypeTheme(
    // 强制使用深色模式
    content: @Composable () -> Unit,
) {
    val colorScheme = BlueTypeDarkColors
    val customColors = darkBlueTypeColors()
    val view = LocalView.current

    if (!view.isInEditMode) {
        SideEffect {
            val window = (view.context as? Activity)?.window ?: return@SideEffect
            // 状态栏透明，导航栏使用背景色
            window.statusBarColor = Color.Transparent.toArgb()
            window.navigationBarColor = colorScheme.background.toArgb()
            
            val controller = WindowCompat.getInsetsController(window, view)
            // 强制状态栏和导航栏图标为浅色 (false 表示图标不使用深色)
            controller.isAppearanceLightStatusBars = false
            controller.isAppearanceLightNavigationBars = false
        }
    }

    CompositionLocalProvider(
        LocalBlueTypeColors provides customColors
    ) {
        MaterialTheme(
            colorScheme = colorScheme,
            typography = BlueTypeTypography,
            shapes = BlueTypeShapes,
            content = content,
        )
    }
}

fun editorialDarkColorScheme(): ColorScheme {
    return androidx.compose.material3.darkColorScheme(
        primary = Color(0xFFC7BDF0),
        onPrimary = Color(0xFF312B58),
        primaryContainer = Color(0xFF48416E),
        onPrimaryContainer = Color(0xFFE4DFFF),
        secondary = Color(0xFFC9C1E9),
        onSecondary = Color(0xFF312C4C),
        secondaryContainer = Color(0xFF474264),
        onSecondaryContainer = Color(0xFFE5DFFF),
        tertiary = Color(0xFFCDC3E6),
        onTertiary = Color(0xFF342E49),
        tertiaryContainer = Color(0xFF4B4461),
        onTertiaryContainer = Color(0xFFE9DFFC),
        error = Color(0xFFFFB2C1),
        onError = Color(0xFF670021),
        errorContainer = Color(0xFF913349),
        onErrorContainer = Color(0xFFFFD9E1),
        background = Color(0xFF1B1B1F),
        onBackground = Color(0xFFE4E1E6),
        surface = Color(0xFF1B1B1F),
        onSurface = Color(0xFFE4E1E6),
        surfaceVariant = Color(0xFF48454F),
        onSurfaceVariant = Color(0xFFC9C4D0),
        outline = Color(0xFF938F99),
        outlineVariant = Color(0xFF48454F),
        inverseSurface = Color(0xFFE4E1E6),
        inverseOnSurface = Color(0xFF313033),
        inversePrimary = Color(0xFF60598A),
        surfaceDim = Color(0xFF131316),
        surfaceBright = Color(0xFF39393C),
        surfaceContainerLowest = Color(0xFF0E0E11),
        surfaceContainerLow = Color(0xFF1B1B1F),
        surfaceContainer = Color(0xFF1F1F23),
        surfaceContainerHigh = Color(0xFF29292D),
        surfaceContainerHighest = Color(0xFF343438),
        scrim = Color(0xFF000000),
    )
}

val BlueTypeShapes: Shapes = Shapes(
    extraSmall = BlueTypeRoundedTokens.cornerSmall,
    small = BlueTypeRoundedTokens.cornerMedium,
    medium = BlueTypeRoundedTokens.cornerLarge,
    large = BlueTypeRoundedTokens.cornerXL,
    extraLarge = BlueTypeRoundedTokens.cornerXXL,
)
