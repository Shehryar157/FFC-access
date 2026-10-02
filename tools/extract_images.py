"""
Extract the illustrations from Fighting Fantasy Classics' downloaded book bundles into PNG files,
so they can be looked at and described.

Each installed book has a folder under LocalLow\Tin Man Games\Fighting Fantasy Classics, and its pictures
live in a Unity "asset bundle" file in that folder whose name ends in "standalonewindows". UnityPy can open
those bundles the way the game does and save the textures as normal images.

Usage:  python tools/extract_images.py            (all installed books)
        python tools/extract_images.py forestofdoom   (only books whose folder name contains this)
Output: images/<book id>/<picture name>.png       (not committed to git: the art belongs to the publisher)
"""
import os
import sys

import UnityPy

GAME_DATA = os.path.expandvars(r"%USERPROFILE%\AppData\LocalLow\Tin Man Games\Fighting Fantasy Classics")
OUT_DIR = os.path.join(os.path.dirname(__file__), "..", "images")


def extract_book(book_dir, book_id):
    count = 0
    for file_name in os.listdir(book_dir):
        if not file_name.endswith("standalonewindows"):
            continue
        env = UnityPy.load(os.path.join(book_dir, file_name))
        out = os.path.join(OUT_DIR, book_id)
        os.makedirs(out, exist_ok=True)
        for obj in env.objects:
            if obj.type.name not in ("Texture2D", "Sprite"):
                continue
            data = obj.read()
            name = getattr(data, "m_Name", None) or getattr(data, "name", None)
            if not name:
                continue
            path = os.path.join(out, name + ".png")
            if os.path.exists(path):
                continue  # a sprite and its texture often share a name
            try:
                data.image.save(path)
                count += 1
            except Exception as e:  # some textures use formats UnityPy can't decode; skip those
                print("  could not save", name, ":", e)
    return count


def main():
    only = sys.argv[1] if len(sys.argv) > 1 else ""
    for book_id in sorted(os.listdir(GAME_DATA)):
        book_dir = os.path.join(GAME_DATA, book_id)
        if not os.path.isdir(book_dir) or only not in book_id:
            continue
        n = extract_book(book_dir, book_id)
        if n:
            print(book_id, ":", n, "images")


if __name__ == "__main__":
    main()
