#!/usr/bin/env bash
# ==============================================================================
# QuackQuackSearch - RPM Package Builder (.rpm)
# Usage: ./packaging/rpm/build-rpm.sh [version] [arch] [publish_dir] [output_file]
# ==============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

RAW_VERSION="${1:-0.1.0}"
VERSION="${RAW_VERSION#v}"
VERSION="${VERSION//-/.}"
RAW_ARCH="${2:-x86_64}"

case "$RAW_ARCH" in
    x64|amd64|x86_64)
        RPM_ARCH="x86_64"
        DOTNET_RID="linux-x64"
        ;;
    arm64|aarch64)
        RPM_ARCH="aarch64"
        DOTNET_RID="linux-arm64"
        ;;
    *)
        echo "Unsupported architecture: $RAW_ARCH" >&2
        exit 1
        ;;
esac

PUBLISH_DIR="${3:-}"
OUTPUT_FILE="${4:-$REPO_ROOT/quackquacksearch-linux-${RPM_ARCH}.rpm}"

WORK_DIR="$(mktemp -d)"
trap 'rm -rf "$WORK_DIR"' EXIT

if [[ -z "$PUBLISH_DIR" ]]; then
    PUBLISH_DIR="$WORK_DIR/publish"
    mkdir -p "$PUBLISH_DIR"
    echo "-> Publishing self-contained binaries for $DOTNET_RID..."
    dotnet publish "$REPO_ROOT/src/QuackQuackSearch.Cli/QuackQuackSearch.Cli.csproj" -c Release -r "$DOTNET_RID" --self-contained true -o "$PUBLISH_DIR" --nologo -v q
    dotnet publish "$REPO_ROOT/src/QuackQuackSearch.Daemon/QuackQuackSearch.Daemon.csproj" -c Release -r "$DOTNET_RID" --self-contained true -o "$PUBLISH_DIR" --nologo -v q
    dotnet publish "$REPO_ROOT/src/QuackQuackSearch.Gui/QuackQuackSearch.Gui.csproj" -c Release -r "$DOTNET_RID" --self-contained true -o "$PUBLISH_DIR" --nologo -v q
else
    PUBLISH_DIR="$(cd "$PUBLISH_DIR" && pwd)"
fi

RPMBUILD_DIR="$WORK_DIR/rpmbuild"
mkdir -p "$RPMBUILD_DIR"/{BUILD,RPMS,SOURCES,SPECS,SRPMS}

rpmbuild -bb \
    --target "$RPM_ARCH" \
    --define "_topdir $RPMBUILD_DIR" \
    --define "pkg_version $VERSION" \
    --define "publish_dir $PUBLISH_DIR" \
    --define "repo_root $REPO_ROOT" \
    "$SCRIPT_DIR/quackquacksearch.spec"

BUILT_RPM="$(find "$RPMBUILD_DIR/RPMS" -name "*.rpm" | head -n 1)"
if [[ -z "$BUILT_RPM" ]]; then
    echo "Error: Failed to locate built RPM in $RPMBUILD_DIR/RPMS" >&2
    exit 1
fi

cp -f "$BUILT_RPM" "$OUTPUT_FILE"
echo "✓ Built RPM package: $OUTPUT_FILE"
