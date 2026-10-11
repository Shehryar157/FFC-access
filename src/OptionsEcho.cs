using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using Rewired;
using UnityEngine;

namespace FFCAccess
{
    /// <summary>
    /// In the game's options and new-adventure screens, Left/Right/Enter change the focused row (library sorting,
    /// mood music, sliders, toggles...). The game changes the value after our focus announcement has already run,
    /// so a moment later we read the row again to say its new value. Nothing about the game's controls is changed.
    /// </summary>
    internal static class OptionsEcho
    {
        public static void OnInput(InputActionEventData e)
        {
            string a = e.actionName;
            if ((a != "NavLeft" && a != "NavRight" && a != "Confirm") || !e.GetButtonDown())
            {
                return;
            }
            object screen = null;
            if (InputLayers.Receiving("OptionsMenu")) screen = OptionsMenu.instance;
            else if (InputLayers.Receiving("NewAdventuresMenu")) screen = Object.FindObjectOfType<NewAdventureDisplay>();
            if (screen != null)
            {
                GameObject unused;
                Plugin.Instance.StartCoroutine(Echo(screen, FocusedRow(screen, out unused), ++newest));
            }
        }

        /// <summary>Counts the echoes started, so an older one stops when the player presses again.</summary>
        private static int newest;

        /// <summary>The focused row as we'd speak it ("Music volume, slider, 60 percent"), or null if there's none.</summary>
        private static string FocusedRow(object screen, out GameObject target)
        {
            target = null;
            try
            {
                Traverse t = Traverse.Create(screen);
                int navIndex = t.Field("navIndex").GetValue<int>();
                OptionsNav nav = t.Field("navButtons").GetValue<OptionsNav>();
                if (navIndex < 0 || nav == null)
                {
                    return null;
                }
                List<OptionsNav.OptionsItem> items = nav.ActiveOptions();
                return navIndex < items.Count ? NavAnnouncer.DescribeOptionItem(items[navIndex], out target) : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// before is the row as it read when the key was pressed. Watch it every frame and speak as soon as it has
        /// changed and then stayed the same for three frames (sliders glide to their new value over 0.1 seconds, so
        /// the first changed reading isn't the final one). If it never changes, speak after 0.2 seconds as before.
        /// </summary>
        private static IEnumerator Echo(object screen, string before, int id)
        {
            float deadline = Time.unscaledTime + 0.2f;
            string last = before;
            int still = 0;
            GameObject target;
            while (true)
            {
                yield return null;
                if (id != newest || TextWindow.Open || SettingsMenu.Open)
                {
                    yield break;
                }
                string now = FocusedRow(screen, out target);
                still = now == last ? still + 1 : 0;
                last = now;
                if ((now != before && still >= 2) || Time.unscaledTime >= deadline)
                {
                    break;
                }
            }
            if (!string.IsNullOrEmpty(last))
            {
                Speech.Say(last);
                NavAnnouncer.Remember(screen, target, last);
            }
        }
    }
}
