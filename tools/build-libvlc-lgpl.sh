#!/bin/bash
# Recompile libVLC pour Android en LGPL (sans x264 ni autres contribs GPL) — à lancer sous Linux / WSL (Ubuntu 24.04).
# Usage : tools/build-libvlc-lgpl.sh [abi ...]      (défaut : arm64 x86_64 ; 32 bits = NDK r21, non géré ici)
# Résultat : android/libvlc-lgpl/jniLibs/<abi>/{libvlc.so,libvlcjni.so,libc++_shared.so}. Durée : ~30 min par architecture (16 cœurs).
set -ex
ABIS=${@:-arm64 x86_64}
WORK=${WORK:-$HOME/libvlc-build}
REPO="$(cd "$(dirname "$0")/.." && pwd)"
mkdir -p "$WORK" && cd "$WORK"

sudo apt-get update -y
sudo apt-get install -y automake ant autopoint cmake build-essential libtool-bin patch pkg-config protobuf-compiler ragel \
    subversion unzip git openjdk-17-jdk flex python3 python-is-python3 wget curl gettext nasm yasm meson ninja-build \
    bison m4 gperf lzip autoconf xz-utils zip p7zip-full

[ -d android-ndk-r27c ] || { wget -q https://dl.google.com/android/repository/android-ndk-r27c-linux.zip -O ndk.zip && unzip -q ndk.zip && rm ndk.zip; }
export ANDROID_NDK="$WORK/android-ndk-r27c"
export ANDROID_HOME=${ANDROID_HOME:-$HOME/Android/Sdk}
export JAVA_HOME=/usr/lib/jvm/java-17-openjdk-amd64

[ -d libvlcjni ] || git clone -b libvlcjni-3.x https://code.videolan.org/videolan/libvlcjni.git
cd libvlcjni
[ -d vlc ] || ./buildsystem/get-vlc.sh

for abi in $ABIS; do
    # --license l : contribs LGPLv3 (GnuTLS pour https, sans x264 ni libdvdnav) ; le défaut de VideoLAN est GPLv3, et --license a (LGPLv2) retire aussi TLS
    ./buildsystem/compile-libvlc.sh -a "$abi" --release --license l
    dest="$REPO/android/libvlc-lgpl/jniLibs/$([ "$abi" = arm64 ] && echo arm64-v8a || echo "$abi")"
    mkdir -p "$dest"
    cp libvlc/jni/libs/*/*.so "$dest/" 2>/dev/null || cp libvlc/jni/libs/$([ "$abi" = arm64 ] && echo arm64-v8a || echo "$abi")/*.so "$dest/"
done
echo "OK : bibliothèques copiées dans android/libvlc-lgpl/jniLibs/"
