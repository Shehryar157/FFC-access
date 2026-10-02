FFC Access: screen reader accessibility for Fighting Fantasy Classics
=====================================================================

This mod makes the Steam version of Fighting Fantasy Classics (Tin Man Games) playable with a screen reader.
It speaks through NVDA or JAWS, and uses Windows voices (SAPI) if no screen reader is running.


Installing
----------

1. Run FFCAccess Installer. It finds the game through Steam and asks before changing anything.
2. Start the game from Steam as usual. After a few seconds you should hear
   "Fighting Fantasy Classics accessibility loaded. Press F1 for help."

To update, run "FFCAccess Installer" again (a copy is kept in the game folder). It checks for a newer version
online, installs it, and removes files the older version installed that are no longer needed. Your own files
are kept: your settings, screen dumps, and a .bak backup of any picture description you edited.

The installer includes BepInEx 5, the mod loader that runs this mod.

To uninstall, delete winhttp.dll from the game folder (that switches all mods off), or delete the
BepInEx\plugins\FFCAccess folder to remove only this mod.


Moving around
-------------

The game's own keys still work: arrow keys or W A S D to move, Enter or Space to select, Escape for the menu,
I for the inventory. The mod announces whatever is focused.

On the book shelf: Left and Right move along a row (with Control by 5 books, with Alt by 10),
Up and Down change rows, Home and End go to the ends of a row, Control Home and Control End to the first
and last book. To order the shelf by book number, use the game's own setting: Options, General tab,
Sort By, and press Left or Right.


Reading a book
--------------

Each new section is read aloud when it opens. The text then works like a read-only text box:

  Up and Down              previous or next sentence
  Control Up and Down      previous or next paragraph
  Left and Right           previous or next character
  Control Left and Right   previous or next word
  Home and End             start or end of the line
  Control Home and End     top or bottom
  Shift with any of these  select text; Control C copies, Control A selects all
  Tab and Shift Tab        next or previous choice
  Enter or Space           take the choice you are on (the mod turns to the right page for you)
  D                        full description of an illustration
  B, F, H                  Go Back, Free Choice and Heal (the game's free-read helpers; each asks first)
  Page Up and Page Down    turn pages, in page-by-page layout

Popups work the same way: the arrows read the message, Tab moves between buttons, Enter presses one.


Anywhere in a book
------------------

  S        your stats (on the book page and in fights)
  I        your inventory; Enter on a usable item uses it
  M        the map, in books that have one: where you are, paths with compass directions and distances,
           and other places you know; Enter on a place gives a route
  C        in a fight, both sides' Skill and Stamina; Left and Right choose an action
  T        in a trade or bet, read both amounts; Left and Right change the first, Shift Left and Right the second
  F1       help
  F2       read the whole section again
  F6       open the game's Adventure Sheet
  F7       read everything on the screen
  F8       repeat the last message
  F9       mod settings (also on the Accessibility tab of the game's Options)
  F10      save a screen dump, useful when reporting a problem

Stats, inventory, the map and picture descriptions open in a text window. Escape closes it.
Riddle answers and names open an editable text box: type, then Enter to confirm or Escape to cancel.


Settings (F9)
-------------

  Reading layout                 whole section, or page by page like the printed book
  Read new sections automatically
  Announce page breaks           the breaks the book's authors placed on purpose
  Use Windows voices even with a screen reader

Settings are saved in BepInEx\config\ffcaccess.screenreader.cfg.


Picture descriptions
--------------------

Descriptions are text files in BepInEx\plugins\FFCAccess\descriptions, one per book. Books without a file
just announce "Illustration, no description yet." You can correct or add descriptions with any text editor;
keep the quotes and commas in place.


If the mod doesn't start
------------------------

- If the installer can't find the game, paste the game folder when it asks.
- Finding the game folder: in your Steam library, open the game's context menu, choose Manage, then Browse local files.
- Still silent? Look for BepInEx\LogOutput.log in the game folder. If it's missing, BepInEx didn't run, which usually
  means winhttp.dll is missing from the game folder. Running the installer again puts it back.


Reporting problems
------------------

Please include BepInEx\LogOutput.log, and if a screen was silent or wrong, press F10 on it first and include
the newest file from BepInEx\plugins\FFCAccess\dumps.


Credits
-------

BepInEx (LGPL 2.1) and HarmonyX (MIT) load and patch the game.
Tolk by Davy Kager (LGPL 3) and the NVDA Controller Client (LGPL 2.1) connect to screen readers.
Fighting Fantasy Classics is by Tin Man Games; Fighting Fantasy is created by Steve Jackson and Ian Livingstone.
This mod contains no game files.
