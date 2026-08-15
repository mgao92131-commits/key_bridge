package com.bluetype.android.domain.model

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
