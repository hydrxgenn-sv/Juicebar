#!/usr/bin/env bash
# Installs Juicebar for the current user, plus the udev rule that lets it read HID receivers without root.
set -euo pipefail
cd "$(dirname "$0")"

BIN="$HOME/.local/bin"
APPS="$HOME/.local/share/applications"
mkdir -p "$BIN" "$APPS"
install -m 755 Juicebar "$BIN/juicebar"
ICONS="$HOME/.local/share/icons/hicolor/512x512/apps"
mkdir -p "$ICONS"
install -m 644 juicebar.png "$ICONS/juicebar.png"

cat > "$APPS/juicebar.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=Juicebar
Comment=Peripheral batteries and system monitor
Exec=$BIN/juicebar --show
Icon=juicebar
Terminal=false
Categories=Utility;
EOF

if ! cmp -s 70-juicebar.rules /etc/udev/rules.d/70-juicebar.rules 2>/dev/null; then
  echo "Installing the udev rule (asks for the administrator password)..."
  sudo install -m 644 70-juicebar.rules /etc/udev/rules.d/70-juicebar.rules
  sudo udevadm control --reload-rules
  sudo udevadm trigger --subsystem-match=hidraw
fi

echo "Done. Run: $BIN/juicebar --show"
echo "If a receiver was already plugged in and does not show up, unplug it and plug it in again."
echo "On GNOME, tray icons need the AppIndicator extension (gnome-shell-extension-appindicator)."
echo "Bluetooth and Logitech batteries need upower; Razer needs OpenRazer; other headsets need HeadsetControl."
echo "Keyboard shortcut: bind \"$BIN/juicebar --show\" in your desktop's keyboard settings."
