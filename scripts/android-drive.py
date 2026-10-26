#!/usr/bin/env python3
"""Tap the sign-in form, then open New Course. Screenshots stay under android-screenshots/."""

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
    field = find(root, hint)
    if field is None:
        fields = [node for node in nodes(root) if node.attrib.get("class", "").endswith("EditText")]
        if hint.lower().startswith("password") and len(fields) >= 2:
            field = fields[1]
        elif fields:
            field = fields[0]
    if not tap(field):
        return False
    sh("adb", "shell", "input", "text", value)
    time.sleep(0.4)
    return True


def main() -> int:
    root = dump("sign-in.xml")
    for _ in range(3):
        if not dismiss_permissions(root):
            break
        time.sleep(1)
        root = dump("sign-in.xml")
    shot("sign-in.png")

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
    if not tap(find(root, "New Course")):
        print("The course list was visible, but New Course was not.")
        return 1
    time.sleep(3)
    shot("course.png")
    print("Captured sign-in, course list, and new-course screens.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
