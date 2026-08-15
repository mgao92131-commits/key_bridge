package com.bluetype.android.data.preferences

import android.content.Context
import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.Preferences
import com.bluetype.android.data.security.EncryptedTokenStore
import com.bluetype.android.data.security.SecureTokenStore
import com.bluetype.android.data.session.SessionStorage

/** The application-level composition of the focused preference stores. */
internal class PreferenceStores internal constructor(
    context: Context? = null,
    dataStore: DataStore<Preferences> = context!!.dataStore,
    secureTokenStore: EncryptedTokenStore = SecureTokenStore(context!!),
) {
    private val backingStore = PreferencesBackingStore(dataStore, secureTokenStore)

    val devices = DevicePreferences(backingStore)
    val tokens = TokenPreferences(backingStore)
    val ui = UiPreferences(backingStore)
    val sessions = SessionStorage(backingStore)
    val authorizedComputers = AuthorizedComputerPreferences(backingStore)
}
