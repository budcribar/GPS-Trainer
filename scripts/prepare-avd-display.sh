#!/bin/sh
# Runs as one command before the emulator boots. The runner invokes each
# workflow line in its own shell, so this file holds the whole change.
cfg="${HOME}/.android/avd/playshots.avd/config.ini"
if [ ! -f "$cfg" ]; then
  echo "Missing $cfg"
  ls -la "${HOME}/.android/avd" || true
  exit 1
fi
set_kv() {
  if grep -q "^$1=" "$cfg"; then
    sed -i "s|^$1=.*|$1=$2|" "$cfg"
  else
    printf '%s=%s\n' "$1" "$2" >> "$cfg"
  fi
}
set_kv hw.lcd.width 1080
set_kv hw.lcd.height 1920
set_kv hw.lcd.density 480
set_kv hw.keyboard no
set_kv showDeviceFrame no
set_kv skin.dynamic yes
sed -i '/^skin.name=/d' "$cfg"
sed -i '/^skin.path=/d' "$cfg"
echo "--- avd display ---"
grep -E '^(hw.lcd.|skin.|showDeviceFrame)' "$cfg"
