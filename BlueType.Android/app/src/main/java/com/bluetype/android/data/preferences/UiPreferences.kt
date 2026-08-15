package com.bluetype.android.data.preferences

import androidx.datastore.preferences.core.edit
import kotlinx.coroutines.flow.map

internal class UiPreferences(
    private val store: PreferencesBackingStore,
) {
    fun draftText() = store.dataStore.data.map { prefs ->
        prefs[store.draftTextKey].orEmpty()
    }

    suspend fun saveDraftText(value: String) {
        store.dataStore.edit { prefs ->
            if (value.isEmpty()) {
                prefs.remove(store.draftTextKey)
            } else {
                prefs[store.draftTextKey] = value
            }
        }
    }
}
