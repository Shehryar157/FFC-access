"""
Build a release of FFC Access: one installer program with the mod's files packed inside it.

The packed files ("payload") are:
  winhttp.dll, doorstop_config.ini, .doorstop_version   the BepInEx loader (copied from the local game install)
  BepInEx/core/...                                      BepInEx itself
  BepInEx/config/BepInEx.cfg                            with HideManagerGameObject = true (the mod needs it)
  BepInEx/plugins/FFCAccess/...                         the mod, Tolk + screen reader drivers, picture descriptions,
                                                        and version.txt (so the installer knows what's installed)
  FFCAccess-Readme.txt                                  instructions
It deliberately leaves out everything else in the game folder (game files, logs, caches, dumps).

Order matters: the payload zip has to exist before the installer is built, because the build packs it inside.

Usage:  python tools/package.py
Output: dist/FFCAccess Installer.exe   (the release download)
        dist/FFCAccess-<version>.zip   (the same files as a zip, for testing; not published)
"""
import os
import re
import shutil
import subprocess
import zipfile

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
GAME = r"D:\Steam\steamapps\common\Fighting Fantasy Classics"
PLUGIN = os.path.join(GAME, "BepInEx", "plugins", "FFCAccess")
INSTALLER = os.path.join(ROOT, "installer")


def version():
    # The version lives in one place: the BepInPlugin attribute in Plugin.cs.
    src = open(os.path.join(ROOT, "src", "Plugin.cs"), encoding="utf-8").read()
    return re.search(r'BepInPlugin\("[^"]+", "[^"]+", "([^"]+)"\)', src).group(1)


def main():
    # 1. Build the mod (which also copies it into the local game install).
    subprocess.run(["dotnet", "build", "-c", "Release"], cwd=ROOT, check=True, stdout=subprocess.DEVNULL)

    ver = version()
    dist = os.path.join(ROOT, "dist")
    stage = os.path.join(dist, "stage")
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

    # 4. The mod: our DLL, the speech libraries, the picture descriptions, and the version number.
    dest = os.path.join(stage, "BepInEx", "plugins", "FFCAccess")
    os.makedirs(dest)
    for f in ("FFCAccess.dll", "Tolk.dll", "nvdaControllerClient64.dll", "SAAPI64.dll"):
        shutil.copy2(os.path.join(PLUGIN, f), dest)
    shutil.copytree(os.path.join(ROOT, "descriptions"), os.path.join(dest, "descriptions"))
    open(os.path.join(dest, "version.txt"), "w").write(ver)

    # 5. The readme, named so it's easy to spot in the game folder.
    shutil.copy2(os.path.join(ROOT, "docs", "README.txt"), os.path.join(stage, "FFCAccess-Readme.txt"))

    # 6. Zip it, with paths relative to the game folder.
    zip_path = os.path.join(dist, "FFCAccess-%s.zip" % ver)
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as z:
        for folder, _, files in os.walk(stage):
            for f in files:
                full = os.path.join(folder, f)
                z.write(full, os.path.relpath(full, stage))
    shutil.rmtree(stage)

    # 7. Pack the zip inside the installer, build it, and put it in dist.
    shutil.copy2(zip_path, os.path.join(INSTALLER, "payload.zip"))
    subprocess.run(["dotnet", "build", "-c", "Release"], cwd=INSTALLER, check=True, stdout=subprocess.DEVNULL)
    shutil.copy2(os.path.join(INSTALLER, "bin", "Release", "FFCAccess Installer.exe"), dist)
    print("Wrote", os.path.join(dist, "FFCAccess Installer.exe"), "with version", ver, "inside")


if __name__ == "__main__":
    main()
