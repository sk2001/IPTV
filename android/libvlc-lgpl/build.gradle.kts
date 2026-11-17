// libVLC pour Android recompilé en LGPL (sans composants GPL comme x264), à partir des sources de VideoLAN.
// Les bibliothèques natives (jniLibs/<abi>/libvlc.so, libvlcjni.so, libc++_shared.so) sont produites par tools/build-libvlc-lgpl.sh
// (non versionnées : ~40 Mo par architecture). Les sources Java ci-dessous sont celles de libvlcjni (LGPL-2.1+, voir COPYING.LIB,
// LIBVLCJNI_COMMIT et VLC_COMMIT pour la version exacte).
plugins {
    alias(libs.plugins.android.library)
}

android {
    namespace = "org.videolan"
    compileSdk = 36
    defaultConfig { minSdk = 24 }
    buildFeatures { buildConfig = true }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    sourceSets["main"].jniLibs.srcDir("jniLibs")
}

dependencies {
    api(libs.androidx.annotation)
    api(libs.androidx.legacy.support.v4)
}
