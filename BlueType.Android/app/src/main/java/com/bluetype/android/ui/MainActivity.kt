package com.bluetype.android.ui

import android.os.Bundle
import androidx.activity.result.contract.ActivityResultContracts
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.viewModels
import com.bluetype.android.util.PermissionHelper
import com.bluetype.android.ui.viewmodel.MainViewModel

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
        viewModel.ensureForegroundSession()
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
