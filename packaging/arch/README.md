# Arch Linux / AUR Packaging for QuackQuackSearch

This directory provides ready-to-use Arch Linux packaging files for both the pre-built binary release and the source/git version.

## Packages

| Package | Directory | Description |
|---|---|---|
| **`quackquacksearch-bin`** | [`quackquacksearch-bin/`](quackquacksearch-bin) | Fast installation from official GitHub release binaries (recommended) |
| **`quackquacksearch-git`** | [`quackquacksearch-git/`](quackquacksearch-git) | Builds directly from the latest Git `main` branch with the .NET SDK |

---

## Local Installation

### Installing `quackquacksearch-bin` (Recommended)

```bash
cd packaging/arch/quackquacksearch-bin
makepkg -si
```

### Installing `quackquacksearch-git`

```bash
cd packaging/arch/quackquacksearch-git
makepkg -si
```

---

## After Installation

1. Enable and start the systemd user service:
   ```bash
   systemctl --user daemon-reload
   systemctl --user enable --now quackquacksearch.service
   ```

2. Search files:
   ```bash
   qqs <query>
   ```

3. Launch GUI or summon with hotkey:
   - Run `qqs gui`
   - Or press `Meta+Shift+F` anywhere on KDE Plasma!

4. Clean uninstall anytime via pacman:
   ```bash
   sudo pacman -R quackquacksearch-bin
   ```

---

## Publishing to AUR (Arch User Repository)

To publish either package to the AUR:

```bash
# 1. Clone your empty AUR repo
git clone ssh://aur@aur.archlinux.org/quackquacksearch-bin.git
cd quackquacksearch-bin

# 2. Copy the packaging files into it
cp /path/to/quackquacksearch/packaging/arch/quackquacksearch-bin/* .

# 3. Regenerate .SRCINFO if version changed
makepkg --printsrcinfo > .SRCINFO

# 4. Commit and push to AUR
git add PKGBUILD .SRCINFO quackquacksearch.install quackquacksearch.service quackquacksearch-gui.desktop quackquacksearch-krunner.desktop quackquacksearch.png LICENSE
git commit -m "Initial AUR release v0.1.3"
git push origin master
```
