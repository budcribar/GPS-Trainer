#!/usr/bin/env bash
# Install the debug APK and capture Play phone screenshots. The emulator must already be booted.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
out="$root/play-screenshots"
mkdir -p "$out"
apk="$(find "$root/src/GpsTrainer.App/bin/Debug" -name '*-Signed.apk' | head -n 1)"
if [[ -z "${apk}" ]]; then
  echo "No debug APK was built."
  exit 1
fi
echo "Installing $apk"
adb install -r "$apk"
adb shell wm size
adb shell wm density
# The default AVD skin is 320x640. Prefer the hardware profile set before boot,
# and override the display if that profile did not stick.
if ! adb shell wm size | tr -d '\r' | grep -q '1080x1920'; then
  echo "Applying a 1080x1920 display override."
  adb shell wm size 1080x1920
  adb shell wm density 480
  sleep 2
  adb shell wm size
fi

# A full battery and a fixed clock, so the status bar is not a CI timestamp.
adb shell settings put global sysui_demo_allowed 1 || true
adb shell am broadcast -a com.android.systemui.demo -e command enter || true
adb shell am broadcast -a com.android.systemui.demo -e command clock -e hhmm 1000 || true
adb shell am broadcast -a com.android.systemui.demo -e command battery -e plugged false -e level 100 || true
adb shell am broadcast -a com.android.systemui.demo -e command network -e wifi show -e level 4 || true
adb shell am broadcast -a com.android.systemui.demo -e command notifications -e visible false || true

adb shell pm grant com.peakswc.gpstrainer.app android.permission.ACCESS_FINE_LOCATION || true
adb shell pm grant com.peakswc.gpstrainer.app android.permission.ACCESS_COARSE_LOCATION || true
adb shell pm grant com.peakswc.gpstrainer.app android.permission.ACCESS_BACKGROUND_LOCATION || true
adb shell pm grant com.peakswc.gpstrainer.app android.permission.POST_NOTIFICATIONS || true
adb emu geo fix -105.2705 40.0150 1650 || true

adb shell monkey -p com.peakswc.gpstrainer.app -c android.intent.category.LAUNCHER 1
python3 "$root/scripts/play-screenshots.py" "$out"
