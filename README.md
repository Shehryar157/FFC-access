# FFC Access

A screen reader accessibility mod for **Fighting Fantasy Classics** by Tin Man Games (Steam, Windows).
It speaks through **NVDA** or **JAWS**, and falls back to Windows voices (**SAPI**) when no screen reader is running.

With it you can browse the book shelf and menus, read every section like a document, take choices, roll dice,
fight, manage your inventory, explore book maps by compass direction, and hear descriptions of the illustrations.

## Installing

1. Download the latest zip from the [Releases](../../releases) page.
2. Unpack it into the game folder (in Steam: right-click the game, Manage, Browse local files),
   so that `winhttp.dll` sits next to `Fighting Fantasy Classics.exe`.
3. Start the game. You should hear "Fighting Fantasy Classics accessibility loaded. Press F1 for help."

The zip contains BepInEx 5 (the mod loader), the mod, and `FFCAccess-Readme.txt` with every key and setting.
It contains no game files. To uninstall, delete `winhttp.dll` from the game folder.

## What it does

- **Menus and the book shelf:** whatever has focus is announced. The shelf works like a table of rows.
- **Reading:** each section is a read-only text box. Arrows move by sentence and letter, Ctrl+arrows by paragraph
  and word, Shift selects, Ctrl+C copies. Choices sit in the text where they occur; Tab jumps between them and
  Enter takes one, turning to the right page first. There's an optional page-by-page layout.
- **Popups** (dice results, item pickups, questions) are text boxes too, with Tab and Enter for their buttons.
- **Combat and dice:** every roll, attack strength, hit and Stamina change is spoken.
- **Character:** stats, inventory (using Provisions and potions), typed answers for riddles and names,
  portraits, trading and betting amounts.
- **Maps:** where you are, the paths from here with compass directions and distances, and routes to places you know.
- **Pictures:** written descriptions, one file per book. Done so far: *The Warlock of Firetop Mountain*.
- **Settings** on F9, also reachable as an *Accessibility* tab in the game's own Options screen.

## How it was made

The mod was written with [Claude Code](https://claude.com/claude-code), with the project owner testing in the game
and reporting back. Neither the game's source code nor any official modding support was available.

**Tools**

- **ILSpy** (`ilspycmd`) to decompile the game's `Assembly-CSharp.dll` back into readable C#, so its code could be
  read and the right places to hook found. The decompiled code is not part of this repository.
- **BepInEx 5** loads the mod into the running game, through a proxy `winhttp.dll` that Windows loads from the game folder.
- **HarmonyX** (bundled with BepInEx) patches the game's methods at runtime, with *prefixes* (run before a method,
  optionally skipping it) and *postfixes* (run after it, seeing its result).
- **Tolk** talks to the screen reader, through the NVDA controller client, JAWS's COM interface or SAPI.
  It's called from C# with P/Invoke.
- **.NET SDK** builds the mod (a C# library targeting .NET Framework 4.7.2, to match the game's Mono runtime).
- **Python with UnityPy** extracts the books' illustrations from the game's asset bundles, so they could be described.
  `tools/package.py` builds the release zip.

**How it hooks into the game**

- **Focus:** almost every screen has a private method named `RefreshNavSelection` that moves its highlight.
  At startup the mod uses reflection to find every class with that method and adds a postfix, which describes
  whatever got highlighted.
- **Sections:** books are stored as token lists (text, formatting tags, conditional filters and links).
  A postfix on `PageTurner.EndSection` rebuilds the section from those tokens, honouring the same filters the game
  uses, so the reader doesn't depend on how the text was split across pages.
- **Input:** every key action passes through `InputLayerManager.OnInputUpdate`. A prefix there hands the arrows and
  Enter to the mod's reader when the book page or a popup is the active screen, by checking the game's own stack of
  input layers. Everywhere else the game keeps its keys.
- **Choices and buttons** are activated by pressing the game's own buttons (`StoryLinkAction.OnClick`,
  `Button.onClick`), so the game's rules, dice and confirmations all still apply.
- **Combat:** the combat logic updates the screen through a few `EnemySheet` methods (enemy appears, narration,
  attack dice, damage), and postfixes on those speak what a sighted player sees. One prefix on
  `BBDiceThrower.reportDiceValues` catches every 3D dice roll.
- **Popups:** a postfix on `PopupPanel.ShowPopup` reads the popup and turns it into a navigable text box.
- **Text entry:** a prefix on `TMP_InputField.ActivateInputField` opens an accessible editable text box instead of
  the game's silent field, then hands the text back.
- **Data the game already has** is used wherever possible: each book's inventory layout for stat names and groups,
  map locations and their connections, Steam for achievement names.

## Project layout

- `src/`: the mod (C#). `Plugin.cs` is the entry point. `TextBox.cs` is the reusable accessible text box that the
  reader, popups and windows build on.
- `descriptions/`: picture descriptions as JSON, one file per book. Corrections are welcome.
- `docs/README.txt`: the player-facing readme that ships in the zip.
- `native/`: Tolk and the screen reader drivers.
- `tools/`: image extraction and release packaging (Python).

## Building

You need the .NET SDK and the game installed (the project references the game's own DLLs).
Set `GameDir` in `FFCAccess.csproj` if your game is not in `D:\Steam\steamapps\common\Fighting Fantasy Classics`,
then run `dotnet build -c Release`. The build copies the mod into the game's `BepInEx\plugins\FFCAccess` folder.
`python tools/package.py` makes the release zip.

## Credits

- Fighting Fantasy Classics is by Tin Man Games; Fighting Fantasy was created by Steve Jackson and Ian Livingstone.
  This project is unofficial and not affiliated with them.
- BepInEx (LGPL 2.1) and HarmonyX (MIT).
- Tolk by Davy Kager (LGPL 3), and the NVDA Controller Client (LGPL 2.1).
