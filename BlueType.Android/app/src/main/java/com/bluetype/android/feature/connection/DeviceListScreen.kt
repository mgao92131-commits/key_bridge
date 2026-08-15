package com.bluetype.android.feature.connection

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.Icon
import androidx.compose.material3.SwipeToDismissBox
import androidx.compose.material3.SwipeToDismissBoxValue
import androidx.compose.material3.rememberSwipeToDismissBoxState
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Delete
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.bluetype.android.domain.ConnectionState
import com.bluetype.android.domain.ConnectionTarget
import com.bluetype.android.data.StoredDevice
import com.bluetype.android.data.DeviceType
import com.bluetype.android.ui.theme.BlueTypeTheme
import com.bluetype.android.ui.theme.BlueTypeRoundedTokens

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun DeviceListScreen(
    state: ConnectionState,
    statusMessage: String?,
    connectingComputerId: String?,
    pairedBluetoothDevices: List<ConnectionTarget.Bluetooth>,
    recentDevices: List<StoredDevice>,
    wifiHost: String,
    wifiName: String,
    onWifiHostChange: (String) -> Unit,
    onWifiNameChange: (String) -> Unit,
    onConnectWifi: () -> Unit,
    onConnectRecentDevice: (StoredDevice) -> Unit,
    onRemoveRecentDevice: (StoredDevice) -> Unit,
    onConnectBluetooth: (ConnectionTarget.Bluetooth) -> Unit,
    onRefreshBluetooth: () -> Unit,
) {
    var showBluetoothDialog by remember { mutableStateOf(false) }
    var showWifiDialog by remember { mutableStateOf(false) }

    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(BlueTypeTheme.colors.backgroundGradient)
            .statusBarsPadding()
            .navigationBarsPadding(),
    ) {
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(horizontal = 24.dp, vertical = 24.dp),
            verticalArrangement = Arrangement.spacedBy(40.dp),
        ) {
            // Header
            Row(
                modifier = Modifier.fillMaxWidth(),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(12.dp)
            ) {
                Text(
                    text = "BlueType",
                    style = MaterialTheme.typography.headlineSmall.copy(
                        color = MaterialTheme.colorScheme.primary,
                        fontWeight = FontWeight.Bold,
                        letterSpacing = 0.5.sp
                    )
                )
                StatusIndicator(state = state)
            }

            if (!statusMessage.isNullOrBlank() && state !is ConnectionState.Idle && state !is ConnectionState.Connected) {
                Text(
                    text = statusMessage,
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurface.copy(alpha = 0.7f),
                )
            }

            // Main Content: Recent
            Column(
                modifier = Modifier.weight(1f),
                verticalArrangement = Arrangement.spacedBy(24.dp)
            ) {
                Text(
                    text = "RECENT CONNECTIONS",
                    style = MaterialTheme.typography.labelLarge.copy(
                        color = MaterialTheme.colorScheme.onSurface.copy(alpha = 0.25f),
                        letterSpacing = 2.sp,
                        fontWeight = FontWeight.Bold
                    )
                )

                if (recentDevices.isEmpty()) {
                    Box(
                        modifier = Modifier
                            .fillMaxWidth()
                            .weight(1f),
                        contentAlignment = Alignment.Center
                    ) {
                        Text(
                            text = "Connect a device to see it here",
                            style = MaterialTheme.typography.bodyLarge,
                            color = MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.25f),
                        )
                    }
                } else {
                    LazyColumn(
                        modifier = Modifier.fillMaxWidth(),
                        verticalArrangement = Arrangement.spacedBy(16.dp),
                        contentPadding = PaddingValues(bottom = 120.dp)
                    ) {
                        items(recentDevices, key = { device -> device.id }) { device ->
                            val dismissState = rememberSwipeToDismissBoxState(
                                confirmValueChange = { value ->
                                    if (value == SwipeToDismissBoxValue.StartToEnd) {
                                        onRemoveRecentDevice(device)
                                        true
                                    } else {
                                        false
                                    }
                                }
                            )

                            SwipeToDismissBox(
                                state = dismissState,
                                enableDismissFromEndToStart = false,
                                backgroundContent = {
                                    val color = if (dismissState.dismissDirection == SwipeToDismissBoxValue.StartToEnd) {
                                        MaterialTheme.colorScheme.errorContainer
                                    } else {
                                        Color.Transparent
                                    }

                                    Box(
                                        Modifier
                                            .fillMaxSize()
                                            .clip(BlueTypeRoundedTokens.cornerLarge)
                                            .background(color)
                                            .padding(horizontal = 24.dp),
                                        contentAlignment = Alignment.CenterStart
                                    ) {
                                        Icon(
                                            Icons.Default.Delete,
                                            contentDescription = "Delete",
                                            tint = MaterialTheme.colorScheme.onErrorContainer
                                        )
                                    }
                                },
                                content = {
                                    EnhancedRecentItem(
                                        device = device,
                                        isConnecting = connectingComputerId == device.id,
                                        onClick = { onConnectRecentDevice(device) },
                                    )
                                }
                            )
                        }
                    }
                }
            }
        }

        // Action Buttons
        Row(
            modifier = Modifier
                .align(Alignment.BottomCenter)
                .padding(bottom = 40.dp),
            horizontalArrangement = Arrangement.spacedBy(16.dp)
        ) {
            WeakTextActionButton(
                text = "BLUETOOTH",
                onClick = { 
                    onRefreshBluetooth()
                    showBluetoothDialog = true 
                }
            )
            WeakTextActionButton(
                text = "WI-FI",
                onClick = { showWifiDialog = true }
            )
        }

        // Dialogs...
        if (showBluetoothDialog) {
            BluetoothDiscoveryDialog(
                devices = pairedBluetoothDevices,
                onRefresh = onRefreshBluetooth,
                onConnect = {
                    onConnectBluetooth(it)
                    showBluetoothDialog = false
                },
                onDismiss = { showBluetoothDialog = false }
            )
        }

        if (showWifiDialog) {
            WifiManualConnectDialog(
                name = wifiName,
                onNameChange = onWifiNameChange,
                host = wifiHost,
                onHostChange = onWifiHostChange,
                onConnect = {
                    onConnectWifi()
                    showWifiDialog = false
                },
                onDismiss = { showWifiDialog = false }
            )
        }
    }
}

@Composable
private fun StatusIndicator(state: ConnectionState) {
    val color = when (state) {
        is ConnectionState.Connected -> Color(0xFF4CAF50)
        is ConnectionState.Error -> MaterialTheme.colorScheme.error
        is ConnectionState.Idle -> MaterialTheme.colorScheme.outline.copy(alpha = 0.25f)
        else -> MaterialTheme.colorScheme.primary
    }

    Row(
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(6.dp),
        modifier = Modifier
            .clip(CircleShape)
            .background(color.copy(alpha = 0.08f))
            .padding(horizontal = 10.dp, vertical = 4.dp)
    ) {
        Box(
            modifier = Modifier
                .size(6.dp)
                .clip(CircleShape)
                .background(color)
        )
        Text(
            text = stateLabel(state).uppercase(),
            style = MaterialTheme.typography.labelSmall.copy(
                fontWeight = FontWeight.Bold,
                fontSize = 9.sp,
                letterSpacing = 0.5.sp
            ),
            color = color.copy(alpha = 0.7f)
        )
    }
}

@Composable
private fun EnhancedRecentItem(
    device: StoredDevice,
    isConnecting: Boolean,
    onClick: () -> Unit,
) {
    Box(
        modifier = Modifier
            .fillMaxWidth()
            .clip(BlueTypeRoundedTokens.cornerLarge)
            .background(MaterialTheme.colorScheme.surface)
            .border(0.5.dp, Color.White.copy(alpha = 0.15f), BlueTypeRoundedTokens.cornerLarge)
            .clickable(enabled = !isConnecting, onClick = onClick)
            .padding(18.dp)
    ) {
        Row(
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(20.dp)
        ) {
            // Node Icon with Subtle Gradient
            Box(
                modifier = Modifier
                    .size(44.dp)
                    .clip(CircleShape)
                    .background(
                        Brush.linearGradient(
                            colors = listOf(
                                MaterialTheme.colorScheme.primary.copy(alpha = 0.12f),
                                MaterialTheme.colorScheme.primary.copy(alpha = 0.05f)
                            )
                        )
                    ),
                contentAlignment = Alignment.Center
            ) {
                Text(
                    text = if (device.type == DeviceType.WIFI) "W" else "B",
                    style = MaterialTheme.typography.titleMedium.copy(
                        fontWeight = FontWeight.Bold,
                        color = MaterialTheme.colorScheme.primary.copy(alpha = 0.65f),
                        letterSpacing = 0.5.sp
                    )
                )
            }
            
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = device.name,
                    style = MaterialTheme.typography.titleMedium.copy(
                        fontWeight = FontWeight.SemiBold,
                        fontSize = 17.sp
                    ),
                    color = MaterialTheme.colorScheme.onSurface.copy(alpha = 0.9f),
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis
                )
                Spacer(modifier = Modifier.height(2.dp))
                Text(
                    text = if (isConnecting) {
                        "Connecting…"
                    } else {
                        (if (device.type == DeviceType.WIFI) device.host else device.address) ?: ""
                    },
                    style = MaterialTheme.typography.bodySmall.copy(
                        color = if (isConnecting) {
                            MaterialTheme.colorScheme.primary
                        } else {
                            MaterialTheme.colorScheme.onSurface.copy(alpha = 0.45f)
                        },
                    ),
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
            }

            if (isConnecting) {
                androidx.compose.material3.CircularProgressIndicator(
                    modifier = Modifier.size(22.dp),
                    strokeWidth = 2.dp,
                )
            }
        }
    }
}

@Composable
private fun WeakTextActionButton(
    text: String,
    onClick: () -> Unit
) {
    Box(
        modifier = Modifier
            .clip(CircleShape)
            .background(MaterialTheme.colorScheme.primary.copy(alpha = 0.06f))
            .clickable(onClick = onClick)
            .padding(horizontal = 22.dp, vertical = 12.dp),
        contentAlignment = Alignment.Center
    ) {
        Text(
            text = "+ $text",
            style = MaterialTheme.typography.labelLarge.copy(
                fontWeight = FontWeight.Bold,
                color = MaterialTheme.colorScheme.primary.copy(alpha = 0.45f),
                letterSpacing = 1.2.sp
            )
        )
    }
}

@Composable
private fun BluetoothDiscoveryDialog(
    devices: List<ConnectionTarget.Bluetooth>,
    onRefresh: () -> Unit,
    onConnect: (ConnectionTarget.Bluetooth) -> Unit,
    onDismiss: () -> Unit
) {
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { 
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically
            ) {
                Text("Bluetooth", style = MaterialTheme.typography.titleLarge)
                TextButton(onClick = onRefresh) { Text("Refresh") }
            }
        },
        text = {
            Column(modifier = Modifier.height(300.dp)) {
                if (devices.isEmpty()) {
                    Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                        Text(
                            "No paired devices",
                            style = MaterialTheme.typography.bodyMedium,
                            color = MaterialTheme.colorScheme.onSurface.copy(alpha = 0.35f)
                        )
                    }
                } else {
                    LazyColumn(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        items(devices) { device ->
                            Box(
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .clip(BlueTypeRoundedTokens.cornerMedium)
                                    .background(MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.5f))
                                    .clickable { onConnect(device) }
                                    .padding(14.dp)
                            ) {
                                Column {
                                    Text(device.name, style = MaterialTheme.typography.bodyLarge)
                                    Text(device.address, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurface.copy(alpha = 0.4f))
                                }
                            }
                        }
                    }
                }
            }
        },
        confirmButton = { TextButton(onClick = onDismiss) { Text("Close") } }
    )
}

@Composable
private fun WifiManualConnectDialog(
    name: String,
    onNameChange: (String) -> Unit,
    host: String,
    onHostChange: (String) -> Unit,
    onConnect: () -> Unit,
    onDismiss: () -> Unit
) {
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Wi-Fi Connect") },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(16.dp)) {
                OutlinedTextField(
                    value = name,
                    onValueChange = onNameChange,
                    modifier = Modifier.fillMaxWidth(),
                    placeholder = { Text("Computer Name (Optional)") },
                    label = { Text("Computer Name") },
                    singleLine = true,
                    shape = BlueTypeRoundedTokens.cornerMedium
                )
                OutlinedTextField(
                    value = host,
                    onValueChange = onHostChange,
                    modifier = Modifier.fillMaxWidth(),
                    placeholder = { Text("IP Address") },
                    label = { Text("Host") },
                    singleLine = true,
                    shape = BlueTypeRoundedTokens.cornerMedium
                )
            }
        },
        confirmButton = {
            Box(
                modifier = Modifier
                    .fillMaxWidth()
                    .height(50.dp)
                    .clip(BlueTypeRoundedTokens.pill)
                    .background(BlueTypeTheme.colors.sendButtonGradient)
                    .clickable(onClick = onConnect),
                contentAlignment = Alignment.Center
            ) {
                Text("Connect", color = Color.White, fontWeight = FontWeight.Bold)
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss, modifier = Modifier.fillMaxWidth()) {
                Text("Cancel")
            }
        }
    )
}

private fun stateLabel(state: ConnectionState): String {
    return when (state) {
        ConnectionState.Idle -> "Idle"
        is ConnectionState.Connecting -> "Wait"
        is ConnectionState.AwaitingApproval -> "Check"
        is ConnectionState.Connected -> "Live"
        is ConnectionState.Reconnecting -> "Retry"
        is ConnectionState.Error -> "Fail"
    }
}
