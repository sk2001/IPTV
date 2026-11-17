plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.compose)
    alias(libs.plugins.kotlin.serialization)
}

import java.util.Properties

val keystoreProps = Properties().apply {
    val f = rootProject.file("keystore.properties")      // non versionné : voir android/README.md
    if (f.exists()) f.inputStream().use { load(it) }
}

// Moteur libVLC : si libvlc-lgpl/jniLibs existe (voir tools/build-libvlc-lgpl.sh), on utilise la version recompilée en LGPL
// (sans x264 ni autres composants GPL) ; sinon, le paquet officiel de VideoLAN (compilé en GPLv3 par défaut : usage privé seulement).
val lgplJni = rootProject.file("libvlc-lgpl/jniLibs")
val useLgplVlc = lgplJni.isDirectory
val vlcAbis = if (useLgplVlc) lgplJni.listFiles { f -> f.isDirectory }!!.map { it.name }.sorted() else listOf("arm64-v8a", "armeabi-v7a", "x86_64")

android {
    namespace = "app.iptvplayer"
    compileSdk = 36

    defaultConfig {
        applicationId = "app.iptvplayer"
        minSdk = 24                  // Android 7.0
        targetSdk = 36
        versionCode = 1
        versionName = "0.1.0"

    }

    // libVLC embarque ses bibliothèques natives : un APK par architecture (+ un universel) au lieu d'un seul APK de ~170 Mo.
    splits {
        abi {
            isEnable = true
            reset()
            include(*vlcAbis.toTypedArray())
            isUniversalApk = true
        }
    }

    signingConfigs {
        if (keystoreProps.containsKey("storeFile")) create("release") {
            storeFile = rootProject.file(keystoreProps.getProperty("storeFile"))
            storePassword = keystoreProps.getProperty("storePassword")
            keyAlias = keystoreProps.getProperty("keyAlias")
            keyPassword = keystoreProps.getProperty("keyPassword")
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = false          // libVLC utilise JNI : pas de réduction de code sans règles dédiées
            signingConfigs.findByName("release")?.let { signingConfig = it }
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    buildFeatures {
        compose = true
    }

    // Langues de l'interface : anglais (par défaut) et français.
    androidResources {
        localeFilters += listOf("en", "fr")
    }
}

dependencies {
    implementation(libs.androidx.core.ktx)
    implementation(libs.androidx.activity.compose)
    implementation(libs.androidx.lifecycle.runtime.compose)

    implementation(platform(libs.androidx.compose.bom))
    implementation(libs.androidx.compose.ui)
    implementation(libs.androidx.compose.foundation)
    implementation(libs.androidx.compose.material3)
    implementation(libs.androidx.compose.material.icons.extended)
    implementation(libs.androidx.tv.material)
    debugImplementation(libs.androidx.compose.ui.tooling)

    implementation(libs.androidx.appcompat)
    implementation(libs.androidx.lifecycle.viewmodel.compose)
    implementation(libs.kotlinx.coroutines.android)
    implementation(libs.kotlinx.serialization.json)
    implementation(libs.coil.compose)
    implementation(libs.coil.network.okhttp)
    implementation(libs.zxing.core)          // génération du QR code d'appairage (Apache-2.0)
    implementation(libs.zxing.embedded)      // lecture du QR code par la caméra (Apache-2.0)

    testImplementation(libs.junit)
    testImplementation(libs.kotlinx.serialization.json)

    // Moteur de lecture : le même VLC que la version Windows (LGPL-2.1+).
    if (useLgplVlc) implementation(project(":libvlc-lgpl")) else implementation(libs.libvlc)
}
