#!/usr/bin/env bash
# ==============================================================================
# QuackQuackSearch - Installation Script
# Everything-like ultra fast file search for Linux
# ==============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PREFIX="${PREFIX:-$HOME/.local}"
INSTALL_LIB="$PREFIX/lib/quackquacksearch"
INSTALL_BIN="$PREFIX/bin"
DESKTOP_DIR="$PREFIX/share/applications"
KRUNNER_DIR="$HOME/.local/share/krunner/dbusplugins"
SYSTEMD_USER_DIR="${XDG_CONFIG_HOME:-$HOME/.config}/systemd/user"
START_SERVICE=true

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
        --no-service)
            START_SERVICE=false
            shift
            ;;
        -h|--help)
            echo "Usage: ./install.sh [options]"
            echo ""
            echo "Options:"
            echo "  --prefix <path>    Installation target prefix (default: \$HOME/.local)"
            echo "  --no-service       Do not enable and start the systemd user service"
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
echo "    🦆 QuackQuackSearch Installer                        "
echo "=========================================================="
echo "Installation prefix: $PREFIX"
echo ""

# 1. Check prerequisites
if ! command -v dotnet >/dev/null 2>&1; then
    echo "❌ Error: 'dotnet' SDK / runtime is not installed or not found in PATH."
    echo "   Please install .NET 10 (or later): https://dotnet.microsoft.com/download"
    exit 1
fi

DOTNET_VERSION=$(dotnet --version)
echo "✓ Found .NET: $DOTNET_VERSION"

# 2. Build and publish projects in Release mode
echo ""
echo "🔨 Compiling and publishing Release binaries..."

echo "   -> Building CLI (QuackQuackSearch.Cli)..."
dotnet publish "$SCRIPT_DIR/src/QuackQuackSearch.Cli/QuackQuackSearch.Cli.csproj" \
    -c Release -o "$INSTALL_LIB/cli" --nologo -v q

echo "   -> Building Daemon (QuackQuackSearch.Daemon)..."
dotnet publish "$SCRIPT_DIR/src/QuackQuackSearch.Daemon/QuackQuackSearch.Daemon.csproj" \
    -c Release -o "$INSTALL_LIB/daemon" --nologo -v q

echo "   -> Building GUI (QuackQuackSearch.Gui)..."
dotnet publish "$SCRIPT_DIR/src/QuackQuackSearch.Gui/QuackQuackSearch.Gui.csproj" \
    -c Release -o "$INSTALL_LIB/gui" --nologo -v q

# 3. Create symlinks in bin
echo ""
echo "🔗 Creating symlinks in $INSTALL_BIN..."
mkdir -p "$INSTALL_BIN"
rm -f "$INSTALL_BIN/qqs" "$INSTALL_BIN/quackquacksearch-daemon" "$INSTALL_BIN/quackquacksearch-gui"

ln -sf "$INSTALL_LIB/cli/QuackQuackSearch.Cli" "$INSTALL_BIN/qqs"
ln -sf "$INSTALL_LIB/daemon/QuackQuackSearch.Daemon" "$INSTALL_BIN/quackquacksearch-daemon"
ln -sf "$INSTALL_LIB/gui/QuackQuackSearch.Gui" "$INSTALL_BIN/quackquacksearch-gui"

# 4. Install Desktop file for GUI
echo "🖥️  Installing Desktop entry..."
mkdir -p "$DESKTOP_DIR"
cat << EOF > "$DESKTOP_DIR/quackquacksearch-gui.desktop"
[Desktop Entry]
Name=QuackQuackSearch
GenericName=File Search
Comment=Lightning-fast desktop file search
Exec=$INSTALL_BIN/quackquacksearch-gui
Icon=system-search
Terminal=false
Type=Application
Categories=Utility;Core;Filesystem;
Keywords=search;files;find;everything;
StartupNotify=true
X-KDE-Shortcuts=Meta+Shift+F
EOF

if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database "$DESKTOP_DIR" 2>/dev/null || true
fi

# 4b. Register global shortcut (Meta+Shift+F)
echo "⌨️  Registering global hotkey (Meta+Shift+F)..."
if command -v kwriteconfig6 >/dev/null 2>&1; then
    kwriteconfig6 --file kglobalshortcutsrc --group "quackquacksearch-gui.desktop" --key "_k_friendly_name" "QuackQuackSearch" 2>/dev/null || true
    kwriteconfig6 --file kglobalshortcutsrc --group "quackquacksearch-gui.desktop" --key "_launch" "Meta+Shift+F,none,Launch QuackQuackSearch" 2>/dev/null || true
    kquitapp6 kglobalaccel 2>/dev/null || true
elif command -v kwriteconfig5 >/dev/null 2>&1; then
    kwriteconfig5 --file kglobalshortcutsrc --group "quackquacksearch-gui.desktop" --key "_k_friendly_name" "QuackQuackSearch" 2>/dev/null || true
    kwriteconfig5 --file kglobalshortcutsrc --group "quackquacksearch-gui.desktop" --key "_launch" "Meta+Shift+F,none,Launch QuackQuackSearch" 2>/dev/null || true
fi

# 5. Install KDE KRunner plugin
echo "⚡ Installing KDE KRunner D-Bus plugin..."
mkdir -p "$KRUNNER_DIR"
cat << 'EOF' > "$KRUNNER_DIR/quackquacksearch.desktop"
[Desktop Entry]
Name=QuackQuackSearch
Comment=Everything-like ultra fast file search
Type=Service
X-KDE-ServiceTypes=Plasma/Runner
X-Plasma-Runner-Min-Letter-Count=2
X-Plasma-Runner-Match-Regex=^.*$
X-Plasma-Runner-Syntaxes=:q:
X-Plasma-Runner-Syntax-Descriptions=Fast file search matching query
X-Plasma-API=DBus2
X-Plasma-DBusRunner-Service=org.quackquacksearch.Daemon
X-Plasma-DBusRunner-Path=/quackquacksearch
EOF

# Reload KRunner if running
if command -v kquitapp6 >/dev/null 2>&1; then
    kquitapp6 krunner 2>/dev/null || true
elif command -v kquitapp5 >/dev/null 2>&1; then
    kquitapp5 krunner 2>/dev/null || true
fi

# 6. Install and enable systemd user service
echo "⚙️  Configuring systemd user service..."
mkdir -p "$SYSTEMD_USER_DIR"
cat << EOF > "$SYSTEMD_USER_DIR/quackquacksearch.service"
[Unit]
Description=QuackQuackSearch Indexing Daemon
Documentation=https://github.com/quackquacksearch/quackquacksearch
After=default.target

[Service]
Type=simple
ExecStart=$INSTALL_BIN/quackquacksearch-daemon
Restart=on-failure
RestartSec=3s
Nice=10

[Install]
WantedBy=default.target
EOF

if [ "$START_SERVICE" = true ] && command -v systemctl >/dev/null 2>&1; then
    echo "🚀 Enabling and starting systemd user service..."
    systemctl --user daemon-reload
    systemctl --user enable --now quackquacksearch.service || {
        echo "⚠️ Note: Could not start service automatically (headless/chroot?). You can start it later with: systemctl --user start quackquacksearch.service"
    }
fi

echo ""
echo "=========================================================="
echo "✅ Installation completed successfully!"
echo "=========================================================="
echo ""
echo "Installed components:"
echo "  • CLI:     $INSTALL_BIN/qqs"
echo "  • Daemon:  $INSTALL_BIN/quackquacksearch-daemon"
echo "  • GUI:     $INSTALL_BIN/quackquacksearch-gui"
echo "  • Desktop: $DESKTOP_DIR/quackquacksearch-gui.desktop"
echo "  • KRunner: $KRUNNER_DIR/quackquacksearch.desktop"
echo "  • Service: $SYSTEMD_USER_DIR/quackquacksearch.service"
echo ""

# Check if PATH contains bin
if [[ ":$PATH:" != *":$INSTALL_BIN:"* ]]; then
    echo "⚠️  Note: '$INSTALL_BIN' is not in your current PATH."
    echo "   Add the following line to your ~/.bashrc or ~/.zshrc:"
    echo "     export PATH=\"$INSTALL_BIN:\$PATH\""
    echo ""
fi

echo "Quick Start:"
echo "  • Check status:         qqs status"
echo "  • Search files:         qqs search <filename> [--fuzzy]"
echo "  • Add folder:           qqs add /path/to/folder"
echo "  • Open GUI:             qqs gui (or via app launcher)"
echo "  • Global Hotkey:        Press Meta+Shift+F anytime to toggle GUI!"
echo "  • KRunner (KDE):        Press Alt+Space and start typing!"
echo ""
