package app.iptvplayer.data

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import app.iptvplayer.model.Account
import kotlinx.serialization.builtins.ListSerializer
import kotlinx.serialization.json.Json
import java.io.File
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

/**
 * Comptes IPTV, chiffrés au repos (AES-256-GCM, clé stockée dans l'Android Keystore : elle ne quitte jamais l'appareil).
 * Équivalent de AccountStore.cs (DPAPI sous Windows).
 */
class AccountStore(context: Context) {
    private val file = File(context.filesDir, "accounts.bin")
    private val json = Json { ignoreUnknownKeys = true }
    private val serializer = ListSerializer(Account.serializer())

    fun load(): List<Account> = try {
        if (!file.exists()) emptyList()
        else json.decodeFromString(serializer, String(decrypt(file.readBytes()), Charsets.UTF_8))
    } catch (e: Exception) { emptyList() }   // clé perdue / fichier corrompu : on repart d'une liste vide

    fun save(accounts: List<Account>) {
        val tmp = File(file.parentFile, "accounts.bin.tmp")
        tmp.writeBytes(encrypt(json.encodeToString(serializer, accounts).toByteArray(Charsets.UTF_8)))
        if (!tmp.renameTo(file)) { file.delete(); tmp.renameTo(file) }
    }

    // ------------------------------------------------------------------ Chiffrement

    private fun key(): SecretKey {
        val ks = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        (ks.getKey(ALIAS, null) as? SecretKey)?.let { return it }
        val gen = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore")
        gen.init(
            KeyGenParameterSpec.Builder(ALIAS, KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .setKeySize(256)
                .build(),
        )
        return gen.generateKey()
    }

    private fun encrypt(plain: ByteArray): ByteArray {
        val c = Cipher.getInstance(TRANSFORMATION).apply { init(Cipher.ENCRYPT_MODE, key()) }
        return c.iv + c.doFinal(plain)            // IV (12 octets) puis texte chiffré + étiquette GCM
    }

    private fun decrypt(data: ByteArray): ByteArray {
        val c = Cipher.getInstance(TRANSFORMATION)
        c.init(Cipher.DECRYPT_MODE, key(), GCMParameterSpec(128, data, 0, IV_SIZE))
        return c.doFinal(data, IV_SIZE, data.size - IV_SIZE)
    }

    private companion object {
        const val ALIAS = "iptvplayer_accounts"
        const val TRANSFORMATION = "AES/GCM/NoPadding"
        const val IV_SIZE = 12
    }
}
