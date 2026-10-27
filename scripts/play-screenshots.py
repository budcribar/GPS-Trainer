#!/usr/bin/env python3
"""Portrait Play screenshots from the running emulator. Does not change the app.

The account and course are the throwaway emulator fixtures (androidsmoke / Creek Loop).
The sign-in shot is taken before any text is entered, so the password is not on screen.
"""

import re
import struct
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
from pathlib import Path

OUT = Path(sys.argv[1] if len(sys.argv) > 1 else "play-screenshots")
LOGIN = "androidsmoke"
PASSWORD = "android-smoke-1"
COURSE = "Creek Loop"
COMMENT = "Easy Saturday loop."
WIDTH = 1080
HEIGHT = 1920


def sh(*args: str) -> str:
    result = subprocess.run(args, check=False, capture_output=True, text=True)
    if result.returncode != 0 and result.stderr.strip():
        print(result.stderr.strip())
    return result.stdout


def dump() -> ET.Element | None:
    sh("adb", "shell", "uiautomator", "dump", "/sdcard/uidump.xml")
    target = OUT / "uidump.xml"
    pull = subprocess.run(
        ["adb", "pull", "/sdcard/uidump.xml", str(target)],
        check=False,
        capture_output=True,
        text=True,
    )
    if pull.returncode != 0 or not target.exists():
        return None
    try:
        return ET.parse(target).getroot()
    except ET.ParseError:
        return None


def nodes(root: ET.Element | None):
    if root is None:
        return []
    return list(root.iter("node"))


def label(node: ET.Element) -> str:
    return (node.attrib.get("text") or node.attrib.get("content-desc") or "").strip()


def labels(root: ET.Element | None) -> list[str]:
    return [text for text in (label(node) for node in nodes(root)) if text]


def find(root: ET.Element | None, text: str) -> ET.Element | None:
    for node in nodes(root):
        if label(node) == text:
            return node
    lowered = text.lower()
    for node in nodes(root):
        if lowered in label(node).lower():
            return node
    return None


def box(node: ET.Element) -> tuple[int, int, int, int] | None:
    match = re.match(r"\[(\d+),(\d+)\]\[(\d+),(\d+)\]", node.attrib.get("bounds") or "")
    if not match:
        return None
    return tuple(int(part) for part in match.groups())  # type: ignore[return-value]


def center(node: ET.Element) -> tuple[int, int] | None:
    bounds = box(node)
    if bounds is None or bounds[2] <= bounds[0] or bounds[3] <= bounds[1]:
        return None
    return ((bounds[0] + bounds[2]) // 2, (bounds[1] + bounds[3]) // 2)


def tap(node: ET.Element | None) -> bool:
    if node is None:
        return False
    point = center(node)
    if point is None:
        return False
    sh("adb", "shell", "input", "tap", str(point[0]), str(point[1]))
    time.sleep(0.7)
    return True


def hide_keyboard() -> None:
    shown = sh("adb", "shell", "dumpsys", "input_method")
    if "mInputShown=true" not in shown and "mImeWindowVis=1" not in shown:
        return
    sh("adb", "shell", "input", "keyevent", "111")
    time.sleep(0.4)


def is_anr(root: ET.Element | None) -> bool:
    if find(root, "isn't responding") is not None:
        return True
    return find(root, "Close app") is not None and find(root, "Wait") is not None


def clear_blockers() -> ET.Element | None:
    root = None
    for _ in range(4):
        root = dump()
        if root is None:
            time.sleep(1)
            continue
        if is_anr(root):
            print("Dismissing the not-responding dialog.")
            tap(find(root, "Wait"))
            time.sleep(2)
            continue
        return root
    return root


def wait_for(text: str, seconds: int = 45) -> ET.Element | None:
    deadline = time.time() + seconds
    root = None
    while time.time() < deadline:
        root = clear_blockers()
        if find(root, text) is not None:
            return root
        time.sleep(1)
    print(f"Timed out waiting for {text!r}. Visible: {labels(root)[:30]}")
    return None


def shot(name: str) -> Path:
    hide_keyboard()
    time.sleep(0.6)
    target = OUT / name
    with target.open("wb") as handle:
        subprocess.run(["adb", "exec-out", "screencap", "-p"], check=False, stdout=handle)
    return target


def field(root: ET.Element | None, hint: str) -> ET.Element | None:
    for node in nodes(root):
        if node.attrib.get("class", "").endswith("EditText") and hint.lower() in label(node).lower():
            return node
    return None


def type_into(hint: str, value: str) -> bool:
    root = wait_for(hint, 20)
    target = field(root, hint) or find(root, hint)
    if not tap(target):
        print(f"Could not focus {hint!r}.")
        return False
    sh("adb", "shell", "input", "text", value.replace(" ", "%s"))
    time.sleep(0.4)
    hide_keyboard()
    return True


def png_size(path: Path) -> tuple[int, int, int]:
    data = path.read_bytes()
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        raise SystemExit(f"{path} is not a PNG ({len(data)} bytes).")
    width, height = struct.unpack(">II", data[16:24])
    color_type = data[25]
    return width, height, color_type


def flatten(path: Path) -> tuple[int, int]:
    from PIL import Image

    image = Image.open(path).convert("RGB")
    image.save(path, "PNG", optimize=True)
    width, height, color_type = png_size(path)
    if color_type != 2:
        raise SystemExit(f"{path.name} color type is {color_type}, expected 24-bit RGB.")
    if (width, height) != (WIDTH, HEIGHT):
        raise SystemExit(f"{path.name} is {width}x{height}, expected {WIDTH}x{HEIGHT}.")
    ratio = height / width
    if not (9 / 16 - 0.01) <= ratio <= (16 / 9 + 0.01) and not (9 / 16 - 0.01) <= (width / height) <= (16 / 9 + 0.01):
        # Portrait 9:16 is height/width == 16/9. Accept that exact band.
        if abs(ratio - (16 / 9)) > 0.02:
            raise SystemExit(f"{path.name} aspect {width}x{height} is outside 16:9 to 9:16.")
    return width, height


def require(name: str, text: str) -> None:
    root = wait_for(text, 40)
    if root is None:
        raise SystemExit(f"Did not reach {text!r} for {name}.")
    path = shot(name)
    width, height, _ = png_size(path)
    print(f"captured {path} {width}x{height}")
    if (width, height) != (WIDTH, HEIGHT):
        raise SystemExit(f"{name} is {width}x{height}, expected {WIDTH}x{HEIGHT}.")


def main() -> int:
    OUT.mkdir(parents=True, exist_ok=True)
    size = sh("adb", "shell", "wm", "size")
    print(size.strip())
    if f"{WIDTH}x{HEIGHT}" not in size.replace(" ", ""):
        print(f"Display is not {WIDTH}x{HEIGHT}; requesting an override.")
        sh("adb", "shell", "wm", "size", f"{WIDTH}x{HEIGHT}")
        sh("adb", "shell", "wm", "density", "480")
        time.sleep(2)
        size = sh("adb", "shell", "wm", "size")
        print(size.strip())
    if f"{WIDTH}x{HEIGHT}" not in size.replace(" ", ""):
        print(f"Display is not {WIDTH}x{HEIGHT}.")
        return 1

    if wait_for("Login", 70) is None:
        return 1
    require("sign-in.png", "User Name:")

    if not type_into("User Name:", LOGIN):
        return 1
    if not type_into("Password:", PASSWORD):
        return 1
    root = wait_for("Login", 15)
    if not tap(find(root, "Login")):
        print("Could not tap Login.")
        return 1
    if wait_for("Courses", 30) is None:
        return 1

    root = clear_blockers()
    if not tap(find(root, "New Course")):
        print("Could not open New Course.")
        return 1
    if not type_into("Name:", COURSE):
        return 1
    if not type_into("Comments:", COMMENT):
        return 1
    root = wait_for("OK", 15)
    if not tap(find(root, "OK")):
        print("Could not save the course.")
        return 1
    if wait_for(COURSE, 25) is None:
        return 1
    require("courses.png", "Courses")

    root = clear_blockers()
    if not tap(find(root, COURSE)):
        print("Could not open the sample course.")
        return 1
    if wait_for("Learning mode", 25) is None:
        return 1
    require("course.png", COURSE)

    root = clear_blockers()
    if not tap(find(root, "Learning mode")):
        print("Could not open Learning mode.")
        return 1
    if wait_for("Location while you ride", 25) is None:
        return 1
    require("location-disclosure.png", "Location while you ride")

    root = clear_blockers()
    if not tap(find(root, "Allow")):
        print("Could not tap Allow on the disclosure.")
        return 1
    # The in-app choice is followed by the system location prompts.
    deadline = time.time() + 40
    while time.time() < deadline:
        root = clear_blockers()
        if find(root, "Location while you ride") is not None:
            tap(find(root, "Allow"))
            time.sleep(1)
            continue
        tapped = False
        for caption in ("While using the app", "Allow all the time", "Allow"):
            if tap(find(root, caption)):
                tapped = True
                break
        if not tapped and (find(root, "Time:") is not None or find(root, "Distance:") is not None):
            break
        time.sleep(1)
    if wait_for("Time:", 20) is None and wait_for("Distance:", 10) is None:
        return 1
    require("ride.png", "Distance:")

    print("sizes:")
    for name in (
        "sign-in.png",
        "location-disclosure.png",
        "courses.png",
        "course.png",
        "ride.png",
    ):
        width, height = flatten(OUT / name)
        print(f"  {name} {width}x{height}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
