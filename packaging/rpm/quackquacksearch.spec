%global debug_package %{nil}
%global _build_id_links none
%global __strip /bin/true
%global __objdump /bin/true
%global __brp_strip %{nil}
%global __brp_strip_comment_note %{nil}
%global __brp_strip_static_archive %{nil}
%global __brp_check_rpaths %{nil}

Name:           quackquacksearch
Version:        %{pkg_version}
Release:        1%{?dist}
Summary:        Lightning-fast file search for Linux modeled after Everything
License:        MIT
URL:            https://github.com/UkeHa/quackquacksearch
AutoReqProv:    no
Requires:       glibc, libgcc, libstdc++, fontconfig, freetype, libX11, libICE, libSM, libXcursor, libXext, libXi, libXrandr, libXrender

%description
QuackQuackSearch is a lightweight, ultra-fast file indexer and search service
for Linux. It maintains directory paths in a compact in-memory index, tracks
local filesystem changes in real time via Linux inotify, polls network mounts
asynchronously, and serves sub-millisecond search results via CLI (qqs),
a modern desktop GUI, and native KDE Plasma KRunner integration.

%prep
# Nothing to prep, using pre-published binaries

%build
# Pre-built binaries provided via publish_dir

%install
rm -rf %{buildroot}
install -d %{buildroot}/usr/lib/quackquacksearch/cli
install -d %{buildroot}/usr/lib/quackquacksearch/daemon
install -d %{buildroot}/usr/lib/quackquacksearch/gui
install -d %{buildroot}/usr/bin
install -d %{buildroot}/usr/share/applications
install -d %{buildroot}/usr/share/krunner/dbusplugins
install -d %{buildroot}/usr/share/dbus-1/services
install -d %{buildroot}/usr/share/icons/hicolor/256x256/apps
install -d %{buildroot}/usr/share/metainfo
install -d %{buildroot}/usr/lib/systemd/user
install -d %{buildroot}/usr/share/doc/quackquacksearch
install -d %{buildroot}/usr/share/licenses/quackquacksearch

cp -a %{publish_dir}/. %{buildroot}/usr/lib/quackquacksearch/
chmod 0755 %{buildroot}/usr/lib/quackquacksearch/QuackQuackSearch.Cli
chmod 0755 %{buildroot}/usr/lib/quackquacksearch/QuackQuackSearch.Daemon
chmod 0755 %{buildroot}/usr/lib/quackquacksearch/QuackQuackSearch.Gui

ln -sf ../QuackQuackSearch.Cli %{buildroot}/usr/lib/quackquacksearch/cli/QuackQuackSearch.Cli
ln -sf ../QuackQuackSearch.Daemon %{buildroot}/usr/lib/quackquacksearch/daemon/QuackQuackSearch.Daemon
ln -sf ../QuackQuackSearch.Gui %{buildroot}/usr/lib/quackquacksearch/gui/QuackQuackSearch.Gui

ln -sf /usr/lib/quackquacksearch/QuackQuackSearch.Cli %{buildroot}/usr/bin/qqs
ln -sf /usr/lib/quackquacksearch/QuackQuackSearch.Daemon %{buildroot}/usr/bin/quackquacksearch-daemon
ln -sf /usr/lib/quackquacksearch/QuackQuackSearch.Gui %{buildroot}/usr/bin/quackquacksearch-gui

install -Dm644 %{repo_root}/packaging/arch/quackquacksearch-bin/quackquacksearch-gui.desktop %{buildroot}/usr/share/applications/quackquacksearch-gui.desktop
install -Dm644 %{repo_root}/packaging/arch/quackquacksearch-bin/quackquacksearch-krunner.desktop %{buildroot}/usr/share/krunner/dbusplugins/quackquacksearch.desktop
install -Dm644 %{repo_root}/src/QuackQuackSearch.Gui/Assets/quackquacksearch.png %{buildroot}/usr/share/icons/hicolor/256x256/apps/quackquacksearch.png
install -Dm644 %{repo_root}/packaging/flatpak/org.quackquacksearch.QuackQuackSearch.metainfo.xml %{buildroot}/usr/share/metainfo/org.quackquacksearch.QuackQuackSearch.metainfo.xml
install -Dm644 %{repo_root}/packaging/arch/quackquacksearch-bin/quackquacksearch.service %{buildroot}/usr/lib/systemd/user/quackquacksearch.service

cat << 'EOF' > %{buildroot}/usr/share/dbus-1/services/org.quackquacksearch.Daemon.service
[D-BUS Service]
Name=org.quackquacksearch.Daemon
Exec=/usr/bin/quackquacksearch-daemon
SystemdService=quackquacksearch.service
EOF

install -Dm644 %{repo_root}/LICENSE %{buildroot}/usr/share/licenses/quackquacksearch/LICENSE
install -Dm644 %{repo_root}/README.md %{buildroot}/usr/share/doc/quackquacksearch/README.md
install -Dm644 %{repo_root}/README-DE.md %{buildroot}/usr/share/doc/quackquacksearch/README-DE.md
install -Dm644 %{repo_root}/CHANGELOG.md %{buildroot}/usr/share/doc/quackquacksearch/CHANGELOG.md

%post
if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database -q /usr/share/applications 2>/dev/null || true
fi
if command -v systemctl >/dev/null 2>&1; then
    systemctl --global enable quackquacksearch.service 2>/dev/null || true
fi
echo ":: QuackQuackSearch has been installed successfully!"
echo ":: To start the indexing daemon in your active user session:"
echo "::   systemctl --user daemon-reload && systemctl --user enable --now quackquacksearch.service"

%preun
if [ "$1" -eq 0 ]; then
    if command -v systemctl >/dev/null 2>&1; then
        systemctl --global disable quackquacksearch.service 2>/dev/null || true
    fi
fi

%files
/usr/lib/quackquacksearch
/usr/bin/qqs
/usr/bin/quackquacksearch-daemon
/usr/bin/quackquacksearch-gui
/usr/share/applications/quackquacksearch-gui.desktop
/usr/share/krunner/dbusplugins/quackquacksearch.desktop
/usr/share/dbus-1/services/org.quackquacksearch.Daemon.service
/usr/share/icons/hicolor/256x256/apps/quackquacksearch.png
/usr/share/metainfo/org.quackquacksearch.QuackQuackSearch.metainfo.xml
/usr/lib/systemd/user/quackquacksearch.service
/usr/share/licenses/quackquacksearch/LICENSE
/usr/share/doc/quackquacksearch
