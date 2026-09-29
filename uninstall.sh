#!/usr/bin/env bash
# ==============================================================================
# QuackQuackSearch - Uninstallation Script
# Everything-like ultra fast file search for Linux
# ==============================================================================

set -euo pipefail

XDG_DATA_HOME="${XDG_DATA_HOME:-$HOME/.local/share}"
XDG_CONFIG_HOME="${XDG_CONFIG_HOME:-$HOME/.config}"
XDG_CACHE_HOME="${XDG_CACHE_HOME:-$HOME/.cache}"
XDG_STATE_HOME="${XDG_STATE_HOME:-$HOME/.local/state}"

PREFIX="${PREFIX:-$HOME/.local}"
INSTALL_LIB="$PREFIX/lib/quackquacksearch"
INSTALL_BIN="$PREFIX/bin"

if [[ "$PREFIX" == "$HOME/.local" ]]; then
    DESKTOP_DIR="${XDG_DATA_HOME}/applications"
    KRUNNER_DIR="${XDG_DATA_HOME}/krunner/dbusplugins"
    ICON_DIR="${XDG_DATA_HOME}/icons/hicolor/512x512/apps"
else
    DESKTOP_DIR="$PREFIX/share/applications"
    KRUNNER_DIR="$PREFIX/share/krunner/dbusplugins"
    ICON_DIR="$PREFIX/share/icons/hicolor/512x512/apps"
fi

SYSTEMD_USER_DIR="${XDG_CONFIG_HOME}/systemd/user"
PURGE_DATA=false

# Parse arguments
while [[ $# -gt 0 ]]; do
    case "$1" in
        --prefix)
            PREFIX="$2"
            INSTALL_LIB="$PREFIX/lib/quackquacksearch"
            INSTALL_BIN="$PREFIX/bin"
            if [[ "$PREFIX" == "$HOME/.local" ]]; then
                DESKTOP_DIR="${XDG_DATA_HOME}/applications"
                KRUNNER_DIR="${XDG_DATA_HOME}/krunner/dbusplugins"
                ICON_DIR="${XDG_DATA_HOME}/icons/hicolor/512x512/apps"
            else
                DESKTOP_DIR="$PREFIX/share/applications"
                KRUNNER_DIR="$PREFIX/share/krunner/dbusplugins"
                ICON_DIR="$PREFIX/share/icons/hicolor/512x512/apps"
            fi
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

# 4b. Remove icon
if [ -f "$ICON_DIR/quackquacksearch.png" ]; then
    rm -f "$ICON_DIR/quackquacksearch.png"
    echo "✓ Removed application icon."
fi

# 5. Handle user data & cache
CONFIG_DIR="${XDG_CONFIG_HOME}/quackquacksearch"
CACHE_DIR="${XDG_CACHE_HOME}/quackquacksearch"
STATE_DIR="${XDG_STATE_HOME}/quackquacksearch"

if [ "$PURGE_DATA" = true ]; then
    echo "⚠️  Purging configuration, cache, and state..."
    rm -rf "$CONFIG_DIR"
    rm -rf "$CACHE_DIR"
    rm -rf "$STATE_DIR"
    echo "✓ Removed $CONFIG_DIR, $CACHE_DIR, and $STATE_DIR."
else
    echo ""
    echo "ℹ️  User configuration and index cache were preserved:"
    echo "   • Config: $CONFIG_DIR"
    echo "   • Cache:  $CACHE_DIR"
    echo "   • State:  $STATE_DIR"
    echo "   (Run ./uninstall.sh --purge to remove them as well)"
fi

echo ""
echo "=========================================================="
echo "✅ QuackQuackSearch has been successfully uninstalled!"
echo "=========================================================="
echo ""
