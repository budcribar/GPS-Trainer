#!/usr/bin/env bash
# Install the debug APK, open the sign-in screen, then try to reach a course screen.
# The APK is built with DefaultApiBaseUrl=http://10.0.2.2:5097 so it does not call the live site.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
out="$root/android-screenshots"
mkdir -p "$out"
apk="$(find "$root/src/GpsTrainer.App/bin/Debug" -name '*.apk' | head -n 1)"
if [[ -z "${apk}" ]]; then
  echo "No debug APK was built."
  exit 1
fi
echo "Installing $apk"
adb install -r "$apk"
adb shell monkey -p com.peakSwc.gpsTrainer -c android.intent.category.LAUNCHER 1
sleep 12
adb exec-out screencap -p > "$out/sign-in.png"
adb shell uiautomator dump /sdcard/uidump.xml || true
adb pull /sdcard/uidump.xml "$out/sign-in.xml" || true
adb logcat -d -t 500 > "$out/logcat.txt" || true

set +e
python3 "$root/scripts/android-drive.py" "$out"
status=$?
set -e
adb exec-out screencap -p > "$out/after-drive.png"
if [[ ! -f "$out/course.png" ]]; then
  echo "The course screen was not reached. Sign-in screenshot is in android-screenshots/sign-in.png."
  if [[ -f "$out/logcat.txt" ]]; then
    echo "---- crash log ----"
    grep -E "FATAL EXCEPTION|AndroidRuntime|dotnet|monodroid|gpsTrainer keeps" "$out/logcat.txt" | tail -n 80 || true
  fi
  exit "${status:-1}"
fi
exit 0
