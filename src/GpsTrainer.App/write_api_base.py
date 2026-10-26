import os
import sys

path, url = sys.argv[1], sys.argv[2]
os.makedirs(os.path.dirname(path) or ".", exist_ok=True)
escaped = url.replace("\\", "\\\\").replace('"', '\\"')
text = (
    "namespace GpsTrainer.App;\n\n"
    "public static partial class ApiAddress\n"
    "{\n"
    f'    internal const string BuiltIn = "{escaped}";\n'
    "}\n"
)
with open(path, "w", encoding="utf-8") as handle:
    handle.write(text)
