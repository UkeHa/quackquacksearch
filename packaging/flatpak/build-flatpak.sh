#!/usr/bin/env bash
# ==============================================================================
# QuackQuackSearch - Flatpak Bundle Builder (.flatpak)
# Usage: ./packaging/flatpak/build-flatpak.sh [version] [arch] [publish_dir] [output_file]
# ==============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

RAW_VERSION="${1:-0.1.0}"
VERSION="${RAW_VERSION#v}"
RAW_ARCH="${2:-x86_64}"

case "$RAW_ARCH" in
    x64|amd64|x86_64)
        FLATPAK_ARCH="x86_64"
        DOTNET_RID="linux-x64"
        ;;
    arm64|aarch64)
        FLATPAK_ARCH="aarch64"
        DOTNET_RID="linux-arm64"
        ;;
    *)
        echo "Unsupported architecture: $RAW_ARCH" >&2
        exit 1
        ;;
esac

PUBLISH_DIR="${3:-}"
OUTPUT_FILE="${4:-$REPO_ROOT/quackquacksearch-linux-${FLATPAK_ARCH}.flatpak}"
APP_ID="org.quackquacksearch.QuackQuackSearch"

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

BUILD_DIR="$WORK_DIR/flatpak-build"
OSTREE_REPO="$WORK_DIR/flatpak-repo"

mkdir -p \
    "$BUILD_DIR/files/lib/quackquacksearch" \
    "$BUILD_DIR/files/bin" \
    "$BUILD_DIR/files/share/applications" \
    "$BUILD_DIR/files/share/icons/hicolor/256x256/apps" \
    "$BUILD_DIR/files/share/metainfo" \
    "$BUILD_DIR/var"

cat << EOF > "$BUILD_DIR/metadata"
[Application]
name=${APP_ID}
runtime=org.freedesktop.Platform/${FLATPAK_ARCH}/24.08
sdk=org.freedesktop.Sdk/${FLATPAK_ARCH}/24.08
EOF

cp -a "$PUBLISH_DIR"/. "$BUILD_DIR/files/lib/quackquacksearch/"
chmod 755 \
    "$BUILD_DIR/files/lib/quackquacksearch/QuackQuackSearch.Cli" \
    "$BUILD_DIR/files/lib/quackquacksearch/QuackQuackSearch.Daemon" \
    "$BUILD_DIR/files/lib/quackquacksearch/QuackQuackSearch.Gui"

install -Dm755 "$SCRIPT_DIR/quackquacksearch-launcher" "$BUILD_DIR/files/bin/quackquacksearch-launcher"
ln -sf /app/lib/quackquacksearch/QuackQuackSearch.Cli "$BUILD_DIR/files/bin/qqs"
ln -sf /app/lib/quackquacksearch/QuackQuackSearch.Daemon "$BUILD_DIR/files/bin/quackquacksearch-daemon"
ln -sf /app/lib/quackquacksearch/QuackQuackSearch.Gui "$BUILD_DIR/files/bin/quackquacksearch-gui"

install -Dm644 "$SCRIPT_DIR/${APP_ID}.desktop" "$BUILD_DIR/files/share/applications/${APP_ID}.desktop"
install -Dm644 "$SCRIPT_DIR/${APP_ID}.metainfo.xml" "$BUILD_DIR/files/share/metainfo/${APP_ID}.metainfo.xml"
install -Dm644 "$REPO_ROOT/src/QuackQuackSearch.Gui/Assets/quackquacksearch.png" "$BUILD_DIR/files/share/icons/hicolor/256x256/apps/${APP_ID}.png"

flatpak build-finish "$BUILD_DIR" \
    --share=ipc \
    --socket=x11 \
    --socket=fallback-x11 \
    --socket=wayland \
    --device=dri \
    --filesystem=host \
    --talk-name=org.quackquacksearch.Daemon \
    --own-name=org.quackquacksearch.Daemon \
    --talk-name=org.kde.krunner1 \
    --talk-name=org.freedesktop.Notifications \
    --command=quackquacksearch-launcher

flatpak build-export --arch="$FLATPAK_ARCH" "$OSTREE_REPO" "$BUILD_DIR" stable
flatpak build-bundle --arch="$FLATPAK_ARCH" --runtime-repo=https://flathub.org/repo/flathub.flatpakrepo "$OSTREE_REPO" "$OUTPUT_FILE" "$APP_ID" stable

echo "✓ Built Flatpak bundle: $OUTPUT_FILE"
