#!/usr/bin/env bash
# ==============================================================================
# QuackQuackSearch - Uninstallation Script
# Everything-like ultra fast file search for Linux
# ==============================================================================

set -euo pipefail

PREFIX="${PREFIX:-$HOME/.local}"
INSTALL_LIB="$PREFIX/lib/quackquacksearch"
INSTALL_BIN="$PREFIX/bin"
DESKTOP_DIR="$PREFIX/share/applications"
KRUNNER_DIR="$HOME/.local/share/krunner/dbusplugins"
SYSTEMD_USER_DIR="${XDG_CONFIG_HOME:-$HOME/.config}/systemd/user"
PURGE_DATA=false

# Parse arguments
while [[ $# -gt 0 ]]; do
    case "$1" in
        --prefix)
            PREFIX="$2"
            INSTALL_LIB="$PREFIX/lib/quackquacksearch"
            INSTALL_BIN="$PREFIX/bin"
            DESKTOP_DIR="$PREFIX/share/applications"
            shift 2
            ;;
        --purge)
            PURGE_DATA=true
            shift
            ;;
        -h|--help)
            echo "Usage: ./uninstall.sh [options]"
            echo ""
            echo "Options:"
            echo "  --prefix <path>    Installation target prefix (default: \$HOME/.local)"
            echo "  --purge            Also remove all user configuration and index cache"
            echo "  -h, --help         Show this help message"
            exit 0
            ;;
        *)
            echo "Unknown option: $1"
            echo "Use --help for usage information."
            exit 1
            ;;
    esac
done

echo "=========================================================="
echo "    🧹 QuackQuackSearch Uninstaller                      "
echo "=========================================================="
echo "Installation prefix: $PREFIX"
echo ""

# 1. Stop and disable systemd user service
if command -v systemctl >/dev/null 2>&1; then
    echo "⏹️  Stopping and disabling systemd service..."
    systemctl --user stop quackquacksearch.service 2>/dev/null || true
    systemctl --user disable quackquacksearch.service 2>/dev/null || true
fi

if [ -f "$SYSTEMD_USER_DIR/quackquacksearch.service" ]; then
    rm -f "$SYSTEMD_USER_DIR/quackquacksearch.service"
    if command -v systemctl >/dev/null 2>&1; then
        systemctl --user daemon-reload 2>/dev/null || true
    fi
    echo "✓ Removed systemd user unit."
fi

# 2. Remove binaries and wrappers
echo "🗑️  Removing executables and libraries..."
rm -f "$INSTALL_BIN/qqs"
rm -f "$INSTALL_BIN/quackquacksearch-daemon"
rm -f "$INSTALL_BIN/quackquacksearch-gui"
rm -rf "$INSTALL_LIB"
echo "✓ Removed binaries from $INSTALL_BIN and $INSTALL_LIB."

# 3. Remove Desktop entry
if [ -f "$DESKTOP_DIR/quackquacksearch-gui.desktop" ]; then
    rm -f "$DESKTOP_DIR/quackquacksearch-gui.desktop"
    if command -v update-desktop-database >/dev/null 2>&1; then
        update-desktop-database "$DESKTOP_DIR" 2>/dev/null || true
    fi
    echo "✓ Removed application desktop entry."
fi

# 4. Remove KRunner plugin
if [ -f "$KRUNNER_DIR/quackquacksearch.desktop" ]; then
    rm -f "$KRUNNER_DIR/quackquacksearch.desktop"
    if command -v kquitapp6 >/dev/null 2>&1; then
        kquitapp6 krunner 2>/dev/null || true
    elif command -v kquitapp5 >/dev/null 2>&1; then
        kquitapp5 krunner 2>/dev/null || true
    fi
    echo "✓ Removed KDE KRunner plugin."
fi

# 5. Handle user data & cache
CONFIG_DIR="${XDG_CONFIG_HOME:-$HOME/.config}/quackquacksearch"
CACHE_DIR="${XDG_CACHE_HOME:-$HOME/.cache}/quackquacksearch"

if [ "$PURGE_DATA" = true ]; then
    echo "⚠️  Purging configuration and index cache..."
    rm -rf "$CONFIG_DIR"
    rm -rf "$CACHE_DIR"
    echo "✓ Removed $CONFIG_DIR and $CACHE_DIR."
else
    echo ""
    echo "ℹ️  User configuration and index cache were preserved:"
    echo "   • Config: $CONFIG_DIR"
    echo "   • Cache:  $CACHE_DIR"
    echo "   (Run ./uninstall.sh --purge to remove them as well)"
fi

echo ""
echo "=========================================================="
echo "✅ QuackQuackSearch has been successfully uninstalled!"
echo "=========================================================="
echo ""
