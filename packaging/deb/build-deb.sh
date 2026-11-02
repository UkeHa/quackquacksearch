#!/usr/bin/env bash
# ==============================================================================
# QuackQuackSearch - Debian/Ubuntu Package Builder (.deb)
# Usage: ./packaging/deb/build-deb.sh [version] [arch] [publish_dir] [output_file]
# ==============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

RAW_VERSION="${1:-0.1.0}"
VERSION="${RAW_VERSION#v}"
RAW_ARCH="${2:-amd64}"

case "$RAW_ARCH" in
    x64|amd64|x86_64)
        DEB_ARCH="amd64"
        DOTNET_RID="linux-x64"
        ;;
    arm64|aarch64)
        DEB_ARCH="arm64"
        DOTNET_RID="linux-arm64"
        ;;
    *)
        echo "Unsupported architecture: $RAW_ARCH" >&2
        exit 1
        ;;
esac

PUBLISH_DIR="${3:-}"
OUTPUT_FILE="${4:-$REPO_ROOT/quackquacksearch-linux-${DEB_ARCH}.deb}"

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

PKGROOT="$WORK_DIR/quackquacksearch_${VERSION}_${DEB_ARCH}"
mkdir -p \
    "$PKGROOT/DEBIAN" \
    "$PKGROOT/usr/lib/quackquacksearch/cli" \
    "$PKGROOT/usr/lib/quackquacksearch/daemon" \
    "$PKGROOT/usr/lib/quackquacksearch/gui" \
    "$PKGROOT/usr/bin" \
    "$PKGROOT/usr/share/applications" \
    "$PKGROOT/usr/share/krunner/dbusplugins" \
    "$PKGROOT/usr/share/dbus-1/services" \
    "$PKGROOT/usr/share/icons/hicolor/256x256/apps" \
    "$PKGROOT/usr/share/metainfo" \
    "$PKGROOT/usr/lib/systemd/user" \
    "$PKGROOT/usr/share/doc/quackquacksearch"

# Copy self-contained binaries and runtime
cp -a "$PUBLISH_DIR"/. "$PKGROOT/usr/lib/quackquacksearch/"
chmod 755 \
    "$PKGROOT/usr/lib/quackquacksearch/QuackQuackSearch.Cli" \
    "$PKGROOT/usr/lib/quackquacksearch/QuackQuackSearch.Daemon" \
    "$PKGROOT/usr/lib/quackquacksearch/QuackQuackSearch.Gui"

# Compatibility symlinks inside /usr/lib/quackquacksearch/{cli,daemon,gui}
ln -sf ../QuackQuackSearch.Cli "$PKGROOT/usr/lib/quackquacksearch/cli/QuackQuackSearch.Cli"
ln -sf ../QuackQuackSearch.Daemon "$PKGROOT/usr/lib/quackquacksearch/daemon/QuackQuackSearch.Daemon"
ln -sf ../QuackQuackSearch.Gui "$PKGROOT/usr/lib/quackquacksearch/gui/QuackQuackSearch.Gui"

# Symlinks in /usr/bin
ln -sf /usr/lib/quackquacksearch/QuackQuackSearch.Cli "$PKGROOT/usr/bin/qqs"
ln -sf /usr/lib/quackquacksearch/QuackQuackSearch.Daemon "$PKGROOT/usr/bin/quackquacksearch-daemon"
ln -sf /usr/lib/quackquacksearch/QuackQuackSearch.Gui "$PKGROOT/usr/bin/quackquacksearch-gui"

# Desktop entry, KRunner plugin, D-Bus service, Icon, Metainfo, and systemd user unit
install -Dm644 "$REPO_ROOT/packaging/arch/quackquacksearch-bin/quackquacksearch-gui.desktop" \
    "$PKGROOT/usr/share/applications/quackquacksearch-gui.desktop"
install -Dm644 "$REPO_ROOT/packaging/arch/quackquacksearch-bin/quackquacksearch-krunner.desktop" \
    "$PKGROOT/usr/share/krunner/dbusplugins/quackquacksearch.desktop"
install -Dm644 "$REPO_ROOT/src/QuackQuackSearch.Gui/Assets/quackquacksearch.png" \
    "$PKGROOT/usr/share/icons/hicolor/256x256/apps/quackquacksearch.png"
install -Dm644 "$REPO_ROOT/packaging/flatpak/org.quackquacksearch.QuackQuackSearch.metainfo.xml" \
    "$PKGROOT/usr/share/metainfo/org.quackquacksearch.QuackQuackSearch.metainfo.xml"
install -Dm644 "$REPO_ROOT/packaging/arch/quackquacksearch-bin/quackquacksearch.service" \
    "$PKGROOT/usr/lib/systemd/user/quackquacksearch.service"

cat << 'EOF' > "$PKGROOT/usr/share/dbus-1/services/org.quackquacksearch.Daemon.service"
[D-BUS Service]
Name=org.quackquacksearch.Daemon
Exec=/usr/bin/quackquacksearch-daemon
SystemdService=quackquacksearch.service
EOF

# Documentation & License
install -Dm644 "$REPO_ROOT/LICENSE" "$PKGROOT/usr/share/doc/quackquacksearch/copyright"
install -Dm644 "$REPO_ROOT/README.md" "$PKGROOT/usr/share/doc/quackquacksearch/README.md"
install -Dm644 "$REPO_ROOT/README-DE.md" "$PKGROOT/usr/share/doc/quackquacksearch/README-DE.md"
install -Dm644 "$REPO_ROOT/CHANGELOG.md" "$PKGROOT/usr/share/doc/quackquacksearch/CHANGELOG.md"

INSTALLED_SIZE="$(du -sk "$PKGROOT/usr" | cut -f1)"

cat << EOF > "$PKGROOT/DEBIAN/control"
Package: quackquacksearch
Version: ${VERSION}
Section: utils
Priority: optional
Architecture: ${DEB_ARCH}
Installed-Size: ${INSTALLED_SIZE}
Depends: libc6, libgcc-s1, libstdc++6, libfontconfig1, libfreetype6, libx11-6, libice6, libsm6, libxcursor1, libxext6, libxrandr2, libxrender1, libxi6
Recommends: systemd, dbus-user-session
Suggests: plasma-workspace
Maintainer: Uke <https://github.com/UkeHa>
Homepage: https://github.com/UkeHa/quackquacksearch
Description: Lightning-fast file search for Linux modeled after Everything
 QuackQuackSearch is a lightweight, ultra-fast file indexer and search service
 for Linux. It maintains directory paths in a compact in-memory index, tracks
 local filesystem changes in real time via Linux inotify, polls network mounts
 asynchronously, and serves sub-millisecond search results via CLI (qqs),
 a modern desktop GUI, and native KDE Plasma KRunner integration.
EOF

cat << 'EOF' > "$PKGROOT/DEBIAN/postinst"
#!/bin/sh
set -e
if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database -q /usr/share/applications 2>/dev/null || true
fi
if command -v gtk-update-icon-cache >/dev/null 2>&1; then
    gtk-update-icon-cache -q -t -f /usr/share/icons/hicolor 2>/dev/null || true
fi
if command -v systemctl >/dev/null 2>&1; then
    systemctl --global enable quackquacksearch.service 2>/dev/null || true
fi
echo ":: QuackQuackSearch has been installed successfully!"
echo ":: To start the indexing daemon in your active user session:"
echo "::   systemctl --user daemon-reload && systemctl --user enable --now quackquacksearch.service"
EOF
chmod 0755 "$PKGROOT/DEBIAN/postinst"

cat << 'EOF' > "$PKGROOT/DEBIAN/prerm"
#!/bin/sh
set -e
if [ "$1" = "remove" ] || [ "$1" = "purge" ]; then
    if command -v systemctl >/dev/null 2>&1; then
        systemctl --global disable quackquacksearch.service 2>/dev/null || true
    fi
fi
EOF
chmod 0755 "$PKGROOT/DEBIAN/prerm"

dpkg-deb --build --root-owner-group -Zxz "$PKGROOT" "$OUTPUT_FILE"
echo "✓ Built Debian package: $OUTPUT_FILE"
