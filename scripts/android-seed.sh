#!/usr/bin/env bash
# Dummy account and course for the emulator smoke test. Nothing here is a real person.
set -euo pipefail
base="${GPS_TRAINER_URL:-http://127.0.0.1:5097}"
login="androidsmoke"
password="android-smoke-1"
curl -sf -X POST "$base/api/users" \
  -H "content-type: application/json" \
  -d "{\"login\":\"$login\",\"password\":\"$password\",\"email\":\"androidsmoke@example.com\",\"country\":\"US\",\"isMale\":true}" \
  >/tmp/android-signup.json || true
curl -sf -X POST "$base/api/auth/login" \
  -H "content-type: application/json" \
  -d "{\"login\":\"$login\",\"password\":\"$password\"}" \
  >/tmp/android-login.json
python3 - <<'PY'
import json
from pathlib import Path
login = json.loads(Path("/tmp/android-login.json").read_text())
Path("/tmp/android-account.env").write_text(
    "ANDROID_LOGIN=androidsmoke\nANDROID_PASSWORD=android-smoke-1\nANDROID_TOKEN=%s\n" % login["token"]
)
print("seeded", login.get("user", {}).get("login"))
PY
