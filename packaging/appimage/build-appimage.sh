#!/usr/bin/env bash
# ==============================================================================
# QuackQuackSearch - AppImage Builder (.AppImage)
# Usage: ./packaging/appimage/build-appimage.sh [version] [arch] [publish_dir] [output_file]
# ==============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

RAW_VERSION="${1:-0.1.0}"
VERSION="${RAW_VERSION#v}"
RAW_ARCH="${2:-x86_64}"

case "$RAW_ARCH" in
    x64|amd64|x86_64)
        APPIMAGE_ARCH="x86_64"
        DOTNET_RID="linux-x64"
        ;;
    arm64|aarch64)
        APPIMAGE_ARCH="aarch64"
        DOTNET_RID="linux-arm64"
        ;;
    *)
        echo "Unsupported architecture: $RAW_ARCH" >&2
        exit 1
        ;;
esac

PUBLISH_DIR="${3:-}"
OUTPUT_FILE="${4:-$REPO_ROOT/quackquacksearch-linux-${APPIMAGE_ARCH}.AppImage}"

WORK_DIR="$(mktemp -d)"
trap 'rm -rf "$WORK_DIR"' EXIT

if [[ -z "$PUBLISH_DIR" ]]; then
    PUBLISH_DIR="$WORK_DIR/publish"
    mkdir -p "$PUBLISH_DIR"
    echo "-> Publishing self-contained binaries for $DOTNET_RID..."
    dotnet publish "$REPO_ROOT/src/QuackQuackSearch.Cli/QuackQuackSearch.Cli.csproj" -c Release -r "$DOTNET_RID" --self-contained true -o "$PUBLISH_DIR" --nologo -v q
    dotnet publish "$REPO_ROOT/src/QuackQuackSearch.Daemon/QuackQuackSearch.Daemon.csproj" -c Release -r "$DOTNET_RID" --self-contained true -o "$PUBLISH_DIR" --nologo -v q
    dotnet publish "$REPO_ROOT/src/QuackQuackSearch.Gui/QuackQuackSearch.Gui.csproj" -c Release -r "$DOTNET_RID" --self-contained true -o "$PUBLISH_DIR" --nologo -v q
fi

APPDIR="$WORK_DIR/QuackQuackSearch.AppDir"
mkdir -p \
    "$APPDIR/usr/lib/quackquacksearch/cli" \
    "$APPDIR/usr/lib/quackquacksearch/daemon" \
    "$APPDIR/usr/lib/quackquacksearch/gui" \
    "$APPDIR/usr/bin" \
    "$APPDIR/usr/share/applications" \
    "$APPDIR/usr/share/icons/hicolor/256x256/apps" \
    "$APPDIR/usr/share/metainfo"

cp -a "$PUBLISH_DIR"/. "$APPDIR/usr/lib/quackquacksearch/"
chmod 755 \
    "$APPDIR/usr/lib/quackquacksearch/QuackQuackSearch.Cli" \
    "$APPDIR/usr/lib/quackquacksearch/QuackQuackSearch.Daemon" \
    "$APPDIR/usr/lib/quackquacksearch/QuackQuackSearch.Gui"

ln -sf ../QuackQuackSearch.Cli "$APPDIR/usr/lib/quackquacksearch/cli/QuackQuackSearch.Cli"
ln -sf ../QuackQuackSearch.Daemon "$APPDIR/usr/lib/quackquacksearch/daemon/QuackQuackSearch.Daemon"
ln -sf ../QuackQuackSearch.Gui "$APPDIR/usr/lib/quackquacksearch/gui/QuackQuackSearch.Gui"

ln -sf ../lib/quackquacksearch/QuackQuackSearch.Cli "$APPDIR/usr/bin/qqs"
ln -sf ../lib/quackquacksearch/QuackQuackSearch.Daemon "$APPDIR/usr/bin/quackquacksearch-daemon"
ln -sf ../lib/quackquacksearch/QuackQuackSearch.Gui "$APPDIR/usr/bin/quackquacksearch-gui"

install -Dm755 "$SCRIPT_DIR/AppRun" "$APPDIR/AppRun"

cat << 'EOF' > "$APPDIR/quackquacksearch.desktop"
[Desktop Entry]
Name=QuackQuackSearch
GenericName=File Search
Comment=Lightning-fast desktop file search
Exec=quackquacksearch-gui
Icon=quackquacksearch
Terminal=false
Type=Application
Categories=Utility;System;Core;Filesystem;
Keywords=search;files;find;everything;
StartupNotify=true
StartupWMClass=quackquacksearch-gui
X-AppImage-Version=1.0
EOF

install -Dm644 "$APPDIR/quackquacksearch.desktop" "$APPDIR/usr/share/applications/quackquacksearch.desktop"
install -Dm644 "$REPO_ROOT/src/QuackQuackSearch.Gui/Assets/quackquacksearch.png" "$APPDIR/quackquacksearch.png"
ln -sf quackquacksearch.png "$APPDIR/.DirIcon"
install -Dm644 "$REPO_ROOT/src/QuackQuackSearch.Gui/Assets/quackquacksearch.png" "$APPDIR/usr/share/icons/hicolor/256x256/apps/quackquacksearch.png"
install -Dm644 "$REPO_ROOT/packaging/flatpak/org.quackquacksearch.QuackQuackSearch.metainfo.xml" \
    "$APPDIR/usr/share/metainfo/org.quackquacksearch.QuackQuackSearch.metainfo.xml"

HOST_ARCH="$(uname -m)"
if command -v appimagetool >/dev/null 2>&1; then
    APPIMAGETOOL="appimagetool"
else
    APPIMAGETOOL="$WORK_DIR/appimagetool-${HOST_ARCH}.AppImage"
    curl -fsSL -o "$APPIMAGETOOL" \
        "https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-${HOST_ARCH}.AppImage"
    chmod +x "$APPIMAGETOOL"
fi

RUNTIME_ARGS=()
if [[ "$APPIMAGE_ARCH" != "$HOST_ARCH" ]]; then
    RUNTIME_FILE="$WORK_DIR/runtime-${APPIMAGE_ARCH}"
    curl -fsSL -o "$RUNTIME_FILE" \
        "https://github.com/AppImage/type2-runtime/releases/download/continuous/runtime-${APPIMAGE_ARCH}"
    RUNTIME_ARGS=(--runtime-file "$RUNTIME_FILE")
fi

ARCH="$APPIMAGE_ARCH" VERSION="$VERSION" APPIMAGE_EXTRACT_AND_RUN=1 \
    "$APPIMAGETOOL" "${RUNTIME_ARGS[@]}" --no-appstream "$APPDIR" "$OUTPUT_FILE"

chmod +x "$OUTPUT_FILE"
echo "✓ Built AppImage: $OUTPUT_FILE"
