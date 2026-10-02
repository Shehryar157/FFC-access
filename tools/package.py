"""
Build a release zip of FFC Access that players unpack into their game folder.

It contains:
  winhttp.dll, doorstop_config.ini, .doorstop_version   the BepInEx loader (copied from the local game install)
  BepInEx/core/...                                      BepInEx itself
  BepInEx/config/BepInEx.cfg                            with HideManagerGameObject = true (the mod needs it)
  BepInEx/plugins/FFCAccess/...                         the mod, Tolk + screen reader drivers, picture descriptions
  FFCAccess-Readme.txt                                  instructions
It deliberately leaves out everything else in the game folder (game files, logs, caches, dumps).

Usage:  python tools/package.py
Output: dist/FFCAccess-<version>.zip
"""
import os
import re
import shutil
import subprocess
import zipfile

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
GAME = r"D:\Steam\steamapps\common\Fighting Fantasy Classics"
PLUGIN = os.path.join(GAME, "BepInEx", "plugins", "FFCAccess")


def version():
    # The version lives in one place: the BepInPlugin attribute in Plugin.cs.
    src = open(os.path.join(ROOT, "src", "Plugin.cs"), encoding="utf-8").read()
    return re.search(r'BepInPlugin\("[^"]+", "[^"]+", "([^"]+)"\)', src).group(1)


def main():
    # 1. Build (which also copies the mod into the local game install).
    subprocess.run(["dotnet", "build", "-c", "Release"], cwd=ROOT, check=True, stdout=subprocess.DEVNULL)

    ver = version()
    stage = os.path.join(ROOT, "dist", "stage")
    shutil.rmtree(stage, ignore_errors=True)
    os.makedirs(stage)

    # 2. The BepInEx loader files next to the game's exe, and BepInEx's core folder.
    for f in ("winhttp.dll", "doorstop_config.ini", ".doorstop_version"):
        shutil.copy2(os.path.join(GAME, f), stage)
    shutil.copytree(os.path.join(GAME, "BepInEx", "core"), os.path.join(stage, "BepInEx", "core"))

    # 3. A BepInEx config with the one setting the mod depends on.
    cfg = open(os.path.join(GAME, "BepInEx", "config", "BepInEx.cfg"), encoding="utf-8").read()
    cfg = re.sub(r"HideManagerGameObject = \w+", "HideManagerGameObject = true", cfg)
    os.makedirs(os.path.join(stage, "BepInEx", "config"))
    open(os.path.join(stage, "BepInEx", "config", "BepInEx.cfg"), "w", encoding="utf-8").write(cfg)

    # 4. The mod: our DLL, the speech libraries, and the picture descriptions (no dumps).
    dest = os.path.join(stage, "BepInEx", "plugins", "FFCAccess")
    os.makedirs(dest)
    for f in ("FFCAccess.dll", "Tolk.dll", "nvdaControllerClient64.dll", "SAAPI64.dll"):
        shutil.copy2(os.path.join(PLUGIN, f), dest)
    shutil.copytree(os.path.join(ROOT, "descriptions"), os.path.join(dest, "descriptions"))

    # 5. The readme, named so it's easy to spot in the game folder.
    shutil.copy2(os.path.join(ROOT, "docs", "README.txt"), os.path.join(stage, "FFCAccess-Readme.txt"))

    # 6. Zip it, with paths relative to the game folder.
    out = os.path.join(ROOT, "dist", "FFCAccess-%s.zip" % ver)
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
        for folder, _, files in os.walk(stage):
            for f in files:
                full = os.path.join(folder, f)
                z.write(full, os.path.relpath(full, stage))
    shutil.rmtree(stage)
    print("Wrote", out)


if __name__ == "__main__":
    main()
