package com.bluetype.android

import android.os.Bundle
import androidx.activity.result.contract.ActivityResultContracts
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.viewModels
import com.bluetype.android.platform.permissions.PermissionHelper
import com.bluetype.android.feature.connection.MainViewModel

class MainActivity : ComponentActivity() {
    private val viewModel by viewModels<MainViewModel>()
    private val permissionLauncher =
        registerForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) {
            viewModel.refreshBluetoothDevices()
        }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val permissions = PermissionHelper.requiredPermissions()
        if (permissions.isNotEmpty() && !PermissionHelper.hasRequiredPermissions(this)) {
            permissionLauncher.launch(permissions)
        }
        // Foreground restore is handled in onResume only (plus internal debounce) to avoid
        // duplicate restore attempts racing a user tap right after launch.
        setContent {
            BlueTypeApp(viewModel = viewModel)
        }
    }

    override fun onResume() {
        super.onResume()
        viewModel.ensureForegroundSession()
        viewModel.refreshBluetoothDevices()
    }
}
