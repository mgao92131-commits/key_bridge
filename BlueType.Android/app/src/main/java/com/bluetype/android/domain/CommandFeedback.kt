package com.bluetype.android.domain

enum class CommandFeedbackState {
    QUEUED,
    SUCCEEDED,
    FAILED,
}

data class CommandFeedback(
    val requestId: String,
    val action: String,
    val state: CommandFeedbackState,
    val message: String,
)
