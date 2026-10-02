using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FFCAccess
{
    [BepInPlugin("ffcaccess.screenreader", "FFC Access", "0.8.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static Plugin Instance;
        internal static ManualLogSource Log;
        internal static string PluginDir;

        private Harmony harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            PluginDir = Path.GetDirectoryName(Info.Location);
            ModSettings.Bind(Config);
            Speech.Init(PluginDir, ModSettings.PreferSapi.Value);

            harmony = new Harmony("ffcaccess.screenreader");
            TryPatch("focus announcements", () => NavAnnouncer.PatchAll(harmony));
            TryPatch("section reader", () => SectionReader.Patch(harmony));
            TryPatch("book reader", () => BookReader.Patch(harmony));
            TryPatch("popup reader", () => PopupReader.Patch(harmony));
            TryPatch("options tab", () => OptionsTab.Patch(harmony));
            TryPatch("combat and dice", () => CombatReader.Patch(harmony));
            TryPatch("text entry", () => TextEntry.Patch(harmony));
            TryPatch("portraits", () => Portraits.Patch(harmony));
            TryPatch("achievements", () => Achievements.Patch(harmony));

            SoundPacks.Init();

            SceneManager.sceneLoaded += (scene, mode) => Log.LogInfo("Scene loaded: " + scene.name);
            Speech.Say("Fighting Fantasy Classics accessibility loaded. Press F1 for help.");
        }

        private static void TryPatch(string what, Action patch)
        {
            try
            {
                patch();
            }
            catch (Exception e)
            {
                Log.LogError("Failed to set up " + what + ": " + e);
            }
        }

        private void Update()
        {
            Diagnostics.TryLogBindings();
            NavAnnouncer.Tick();
            BookReader.Tick();
            try
            {
                HandleKeys();
            }
            catch (Exception e)
            {
                Log.LogError("Hotkey failed: " + e);
            }
        }

        private void HandleKeys()
        {
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            // An open text window (stats, inventory, picture description) takes every key.
            if (TextWindow.HandleKeys(ctrl, shift))
            {
                BookReader.InventoryRequested = false;
                return;
            }
            // The Accessibility tab in the game's options asked for our settings menu.
            if (OptionsTab.OpenRequested)
            {
                OptionsTab.OpenRequested = false;
                if (!SettingsMenu.Open)
                {
                    SettingsMenu.Toggle();
                }
                return;
            }
            // So does our settings menu.
            if (SettingsMenu.HandleKeys())
            {
                return;
            }
            if (Input.GetKeyDown(KeyCode.F9))
            {
                SettingsMenu.Toggle();
                return;
            }
            // The game's own Inventory key, borrowed on the book page.
            if (BookReader.InventoryRequested)
            {
                BookReader.InventoryRequested = false;
                CharacterInfo.ReadInventory();
                return;
            }
            // The book shelf: Home and End within a row.
            if (ShelfNav.HandleKeys(ctrl))
            {
                return;
            }
            // Trade and bet popups: amounts on Left/Right.
            if (Trading.HandleKeys(shift))
            {
                return;
            }
            // An open popup works like a text box: arrows read it, Tab and Enter work its buttons.
            if (PopupReader.HandleKeys(ctrl, shift))
            {
                return;
            }
            // Reading keys come next; they only do anything on the book page.
            if (BookReader.HandleKeys(ctrl, shift))
            {
                return;
            }
            // Letter hotkeys: never while typing in a text field, and not with Ctrl (Ctrl+S etc. are left alone).
            if (!ctrl && !TextUtil.TypingInField())
            {
                if (Input.GetKeyDown(KeyCode.C) && CombatReader.InCombat)
                {
                    CombatReader.ReadStatus();
                    return;
                }
                if (Input.GetKeyDown(KeyCode.D) && GalleryOpen())
                {
                    if (NavAnnouncer.GalleryPicture != null) Descriptions.ShowFull(NavAnnouncer.GalleryPicture);
                    else Speech.Say("No picture selected.");
                    return;
                }
                if (Input.GetKeyDown(KeyCode.M) && SectionReader.InBook())
                {
                    MapReader.Open();
                    return;
                }
                if (Input.GetKeyDown(KeyCode.S) && StatsKeyActive())
                {
                    CharacterInfo.ReadStats();
                    return;
                }
                if (Input.GetKeyDown(KeyCode.I) && !Diagnostics.KeyboardInventoryBound)
                {
                    CharacterInfo.ReadInventory();
                    return;
                }
            }

            if (Input.GetKeyDown(KeyCode.F1)) Help.Open();
            else if (Input.GetKeyDown(KeyCode.F2)) BookReader.ReadAll();
            else if (Input.GetKeyDown(KeyCode.F6)) CharacterInfo.OpenAdventureSheet();
            else if (Input.GetKeyDown(KeyCode.F7)) ReadScreen();
            else if (Input.GetKeyDown(KeyCode.F8)) Speech.Say(Speech.Last);
            else if (Input.GetKeyDown(KeyCode.F10)) Diagnostics.DumpScene();
        }

        /// <summary>
        /// S is also the game's "down" key (W A S D move). It only means "stats" on the book page and in fights,
        /// where the game has no use for down; everywhere else it's left to the game.
        /// </summary>
        internal static bool StatsKeyActive()
        {
            return (BookReader.Active || CombatReader.InCombat) && !TextUtil.TypingInField();
        }

        internal static bool GalleryOpen()
        {
            GalleryMenu gm = UnityEngine.Object.FindObjectOfType<GalleryMenu>();
            return gm != null && gm.tweener != null && gm.tweener.visible;
        }

        /// <summary>F7, the fallback for screens the mod doesn't know yet: read every visible piece of text.</summary>
        internal static void ReadScreen()
        {
            List<string> texts = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                foreach (GameObject root in SceneManager.GetSceneAt(i).GetRootGameObjects())
                {
                    foreach (string t in TextUtil.TextsUnder(root))
                    {
                        if (!texts.Contains(t))
                        {
                            texts.Add(t);
                        }
                    }
                }
            }
            Speech.Say(texts.Count > 0 ? string.Join("\n", texts.ToArray()) : "No text on screen.");
        }

        private void OnApplicationQuit()
        {
            Speech.Shutdown();
        }
    }
}
