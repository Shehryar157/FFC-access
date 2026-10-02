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
    [BepInPlugin("ffcaccess.screenreader", "FFC Access", "0.2.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static Plugin Instance;
        internal static ManualLogSource Log;
        internal static string PluginDir;

        private static ConfigEntry<bool> autoRead;

        /// <summary>Read each new section aloud in full as soon as it opens.</summary>
        internal static bool AutoRead => autoRead == null || autoRead.Value;

        private Harmony harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            PluginDir = Path.GetDirectoryName(Info.Location);
            ConfigEntry<bool> preferSapi = Config.Bind("Speech", "PreferSAPI", false,
                "Speak with Windows SAPI voices even when a screen reader (NVDA, JAWS) is running.");
            autoRead = Config.Bind("Reading", "AutoRead", true,
                "Read each new section aloud in full when it opens. If false, only the section number and choice count are spoken; use the arrow keys to read.");

            Speech.Init(PluginDir, preferSapi.Value);

            harmony = new Harmony("ffcaccess.screenreader");
            TryPatch("focus announcements", () => NavAnnouncer.PatchAll(harmony));
            TryPatch("section reader", () => SectionReader.Patch(harmony));
            TryPatch("book reader", () => BookReader.Patch(harmony));
            TryPatch("popup reader", () => PopupReader.Patch(harmony));

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

            // Reading keys come first; they only do anything on the book page.
            if (BookReader.HandleKeys(ctrl, shift))
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.F1)) Speech.Say(BookReader.Active ? ReadingHelp + " " + GlobalHelp : GlobalHelp);
            else if (Input.GetKeyDown(KeyCode.F2)) SectionReader.RepeatSection();
            else if (Input.GetKeyDown(KeyCode.F3)) SectionReader.ReadChoices();
            else if (Input.GetKeyDown(KeyCode.F4)) CharacterInfo.ReadStats();
            else if (Input.GetKeyDown(KeyCode.F5)) CharacterInfo.ReadInventory();
            else if (Input.GetKeyDown(KeyCode.F6)) CharacterInfo.OpenAdventureSheet();
            else if (Input.GetKeyDown(KeyCode.F7)) ReadScreen();
            else if (Input.GetKeyDown(KeyCode.F8)) Speech.Say(Speech.Last);
            else if (Input.GetKeyDown(KeyCode.F10)) Diagnostics.DumpScene();
        }

        private const string ReadingHelp =
            "On the book page: Up and Down arrows read the previous or next sentence. Control with Up or Down reads by paragraph. " +
            "Tab and Shift Tab jump to the next or previous choice. Enter picks the choice you are on. Control Home and Control End go to the top or bottom.";

        private const string GlobalHelp =
            "Keys that work anywhere: F1 help. F2 read the whole section again. F3 list the choices. F4 your stats. F5 your inventory. " +
            "F6 open the Adventure Sheet. F7 read everything on screen. F8 repeat the last message. F10 save a screen dump for the mod developer.";

        /// <summary>F7, the fallback for screens the mod doesn't know yet: read every visible piece of text.</summary>
        private static void ReadScreen()
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
