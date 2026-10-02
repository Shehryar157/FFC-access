using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FFCAccess
{
    [BepInPlugin("ffcaccess.screenreader", "FFC Access", "0.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static Plugin Instance;
        internal static ManualLogSource Log;
        internal static string PluginDir;

        private ConfigEntry<bool> preferSapi;
        private Harmony harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            PluginDir = Path.GetDirectoryName(Info.Location);
            preferSapi = Config.Bind("Speech", "PreferSAPI", false,
                "Speak with Windows SAPI voices even when a screen reader (NVDA, JAWS) is running.");

            Speech.Init(PluginDir, preferSapi.Value);

            harmony = new Harmony("ffcaccess.screenreader");
            TryPatch("focus announcements", () => NavAnnouncer.PatchAll(harmony));
            TryPatch("section reader", () => SectionReader.Patch(harmony));
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
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (Input.GetKeyDown(KeyCode.F1))
            {
                Speech.Say(HelpText);
            }
            else if (Input.GetKeyDown(KeyCode.F2))
            {
                SectionReader.RepeatSection();
            }
            else if (Input.GetKeyDown(KeyCode.F3))
            {
                SectionReader.ReadChoices();
            }
            else if (Input.GetKeyDown(KeyCode.F4))
            {
                ReadStats();
            }
            else if (Input.GetKeyDown(KeyCode.F5))
            {
                ReadScreen();
            }
            else if (Input.GetKeyDown(KeyCode.F6))
            {
                SectionReader.StepParagraph(-1);
            }
            else if (Input.GetKeyDown(KeyCode.F7))
            {
                SectionReader.StepParagraph(1);
            }
            else if (Input.GetKeyDown(KeyCode.F8))
            {
                Speech.Say(Speech.Last);
            }
            else if (Input.GetKeyDown(KeyCode.F12))
            {
                Diagnostics.DumpScene();
            }
        }

        private const string HelpText =
            "Accessibility keys. F1: this help. F2: read the current section again. F3: list the choices in this section. " +
            "F4: your stats. F5: read everything on screen. F6 and F7: previous and next paragraph. F8: repeat last message. " +
            "F12: save a screen dump for the mod developer. Use the game's arrow keys and Enter to move and select.";

        private static void ReadStats()
        {
            Character c = BBGameController.instance?.character;
            if (c == null || c.inventory == null || !SectionReader.InBook())
            {
                Speech.Say("No adventure is in progress.");
                return;
            }
            List<string> parts = new List<string>();
            foreach (string id in new[] { "skill", "stamina", "luck" })
            {
                BBInventoryItem item = c.InventoryItem(id);
                if (item != null)
                {
                    parts.Add((string.IsNullOrEmpty(item.name) ? id : item.name) + " " + item.totalQuantity);
                }
            }
            // Log the whole inventory so the mod can learn this book's item types.
            StringBuilder sb = new StringBuilder("Inventory dump:\n");
            foreach (DictionaryEntry e in c.inventory)
            {
                if (e.Value is BBInventoryItem it)
                {
                    sb.Append("  ").Append(it.gameID).Append(" type=").Append(it.type).Append(" name=").Append(it.name)
                      .Append(" qty=").Append(it.quantity).Append(" bonus=").Append(it.bonusQuantity)
                      .Append(" base=").Append(it.baseValue).Append(" slots=").Append(it.slots).Append('\n');
                }
            }
            Log.LogInfo(sb.ToString());
            Speech.Say(parts.Count > 0 ? string.Join(", ", parts.ToArray()) : "No stats found.");
        }

        /// <summary>Fallback for screens the mod doesn't know yet: read every visible piece of text.</summary>
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
