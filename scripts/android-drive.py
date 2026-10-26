#!/usr/bin/env python3
"""Sign in and screenshot each reachable Android page, including Settings and GPS Info."""

import re
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
from pathlib import Path

OUT = Path(sys.argv[1] if len(sys.argv) > 1 else "android-screenshots")
LOGIN = "androidsmoke"
PASSWORD = "android-smoke-1"


def sh(*args: str) -> str:
    result = subprocess.run(args, check=False, capture_output=True, text=True)
    if result.returncode != 0:
        print(result.stdout)
        print(result.stderr)
    return result.stdout


def dump(name: str) -> ET.Element | None:
    sh("adb", "shell", "uiautomator", "dump", "/sdcard/uidump.xml")
    target = OUT / name
    pull = subprocess.run(["adb", "pull", "/sdcard/uidump.xml", str(target)], check=False, capture_output=True, text=True)
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


def center(node: ET.Element) -> tuple[int, int] | None:
    bounds = node.attrib.get("bounds") or ""
    match = re.match(r"\[(\d+),(\d+)\]\[(\d+),(\d+)\]", bounds)
    if not match:
        return None
    x1, y1, x2, y2 = (int(part) for part in match.groups())
    if x2 <= x1 or y2 <= y1:
        return None
    return ((x1 + x2) // 2, (y1 + y2) // 2)


def find(root: ET.Element | None, text: str) -> ET.Element | None:
    for node in nodes(root):
        if label(node) == text:
            return node
    for node in nodes(root):
        if text.lower() in label(node).lower():
            return node
    return None


def tap(node: ET.Element | None) -> bool:
    if node is None:
        return False
    point = center(node)
    if point is None:
        return False
    sh("adb", "shell", "input", "tap", str(point[0]), str(point[1]))
    time.sleep(0.6)
    return True


def shot(name: str) -> None:
    target = OUT / name
    with target.open("wb") as handle:
        subprocess.run(["adb", "exec-out", "screencap", "-p"], check=False, stdout=handle)


def dismiss_permissions(root: ET.Element | None) -> bool:
    for caption in ("Don't allow", "Deny", "While using the app", "Allow"):
        node = find(root, caption)
        if node is not None and tap(node):
            return True
    return False


def type_into(root: ET.Element | None, hint: str, value: str) -> bool:
    field = find_field(root, hint)
    if field is None:
        fields = [node for node in nodes(root) if node.attrib.get("class", "").endswith("EditText")]
        if hint.lower().startswith("password") and len(fields) >= 2:
            field = fields[1]
        elif fields:
            field = fields[0]
    if not tap(field):
        return False
    sh("adb", "shell", "input", "text", value.replace(" ", "%s"))
    time.sleep(0.4)
    return True


def find_field(root: ET.Element | None, hint: str) -> ET.Element | None:
    fields = [node for node in nodes(root) if node.attrib.get("class", "").endswith("EditText")]
    for node in fields:
        if hint.lower() in label(node).lower():
            return node
    return None


def back() -> None:
    sh("adb", "shell", "input", "keyevent", "KEYCODE_BACK")
    time.sleep(0.8)


def open_overflow(root: ET.Element | None) -> bool:
    for caption in ("More options", "More", "Overflow"):
        if tap(find(root, caption)):
            return True
    for node in nodes(root):
        desc = (node.attrib.get("content-desc") or "").lower()
        if "more" in desc or "overflow" in desc:
            return tap(node)
    return False


def open_toolbar(title: str) -> bool:
    root = dump("toolbar.xml")
    if find(root, title) is not None:
        return tap(find(root, title))
    if not open_overflow(root):
        return False
    time.sleep(0.6)
    root = dump("toolbar-open.xml")
    return tap(find(root, title))


def visit(title: str, image: str) -> bool:
    if not open_toolbar(title):
        print(f"Could not open {title}.")
        shot(image)
        return False
    time.sleep(1.2)
    shot(image)
    back()
    return True


def main() -> int:
    sh("adb", "shell", "pm", "grant", "com.peakSwc.gpsTrainer", "android.permission.ACCESS_FINE_LOCATION")
    sh("adb", "shell", "pm", "grant", "com.peakSwc.gpsTrainer", "android.permission.ACCESS_COARSE_LOCATION")
    sh("adb", "emu", "geo", "fix", "-105.27", "40.015", "1650")
    root = dump("sign-in.xml")
    for _ in range(3):
        if not dismiss_permissions(root):
            break
        time.sleep(1)
        root = dump("sign-in.xml")
    shot("sign-in.png")
    if tap(find(root, "Sign up for a login")):
        time.sleep(1)
        shot("signup.png")
        back()
        root = dump("sign-in.xml")
    else:
        print("Could not open Sign up.")
        shot("signup.png")

    if not type_into(root, "User Name:", LOGIN):
        print("Could not find the user name field.")
        return 1
    root = dump("password.xml")
    if not type_into(root, "Password:", PASSWORD):
        print("Could not find the password field.")
        return 1
    root = dump("login.xml")
    if not tap(find(root, "Login")):
        print("Could not find the Login button.")
        return 1
    time.sleep(8)
    root = dump("courses.xml")
    shot("courses.png")
    if find(root, "Courses") is None and find(root, "New Course") is None:
        print("Sign-in did not reach the course list.")
        return 1

    menu_ok = True
    for title, image in (
        ("Settings", "settings.png"),
        ("Data", "data.png"),
        ("GPS Info", "gps-info.png"),
        ("About", "about.png"),
    ):
        menu_ok = visit(title, image) and menu_ok
    if not menu_ok:
        print("A toolbar page was not captured.")
        return 1

    # Give the GPS page a moment after the injected fix, then capture it again.
    if open_toolbar("GPS Info"):
        for _ in range(8):
            time.sleep(1)
            root = dump("gps-info.xml")
            if find(root, "Latitude") is not None or find(root, "40.015") is not None:
                break
        shot("gps-info.png")
        back()

    root = dump("courses.xml")
    if not tap(find(root, "New Course")):
        print("The course list was visible, but New Course was not.")
        return 1
    time.sleep(1)
    shot("course.png")
    root = dump("new-course.xml")
    if type_into(root, "Name:", "Smoke"):
        sh("adb", "shell", "input", "keyevent", "111")
        time.sleep(0.4)
        root = dump("new-course.xml")
        if tap(find(root, "OK")):
            time.sleep(2)
            root = dump("courses.xml")
            shot("courses.png")
            if tap(find(root, "Smoke")):
                time.sleep(1.5)
                shot("course-details.png")
                root = dump("course-details.xml")
                if tap(find(root, "Map mode")):
                    time.sleep(2)
                    shot("map.png")
                    back()
                    root = dump("course-details.xml")
                if tap(find(root, "Learning mode")):
                    time.sleep(1)
                    shot("running.png")
                    root = dump("running.xml")
                    if tap(find(root, "Allow")):
                        time.sleep(2)
                        shot("running.png")
                        root = dump("running.xml")
                        if tap(find(root, "Add Waypoint")):
                            time.sleep(1)
                            shot("insert-waypoint.png")
                            back()
                    back()
                    root = dump("course-details.xml")
                if tap(find(root, "Races")):
                    time.sleep(1)
                    shot("races.png")
                    root = dump("races.xml")
                    if tap(find(root, "New race")):
                        time.sleep(1)
                        shot("race-edit.png")
                        root = dump("race-edit.xml")
                        if tap(find(root, "OK")):
                            time.sleep(1)
                            root = dump("races.xml")
                            shot("races.png")
                            race = find_button(root)
                            if race is not None and "New race" not in label(race):
                                tap(race)
                                time.sleep(1)
                                shot("race-details.png")
                                root = dump("race-details.xml")
                                if tap(find(root, "Stage")):
                                    time.sleep(1.5)
                                    shot("staging.png")
                                    root = dump("staging.xml")
                                    if tap(find(root, "Start")):
                                        time.sleep(1.5)
                                        shot("running.png")
                                        back()
                                    back()
                back()
    print("Captured sign-in, signup, courses, settings, data, GPS info, about, and new course.")
    return 0


def find_button(root: ET.Element | None) -> ET.Element | None:
    for node in nodes(root):
        if not node.attrib.get("class", "").endswith("Button"):
            continue
        text = label(node)
        if text and text not in ("New race", "OK", "Cancel"):
            return node
    return None


if __name__ == "__main__":
    raise SystemExit(main())
