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
                Plugin.Instance.StartCoroutine(Echo(screen));
            }
        }

        private static IEnumerator Echo(object screen)
        {
            // Sliders animate to their new value over 0.1 seconds; wait until that's done.
            yield return new WaitForSecondsRealtime(0.2f);
            if (TextWindow.Open || SettingsMenu.Open)
            {
                yield break;
            }
            Traverse t = Traverse.Create(screen);
            int navIndex = t.Field("navIndex").GetValue<int>();
            OptionsNav nav = t.Field("navButtons").GetValue<OptionsNav>();
            if (navIndex < 0 || nav == null)
            {
                yield break;
            }
            List<OptionsNav.OptionsItem> items = nav.ActiveOptions();
            if (navIndex >= items.Count)
            {
                yield break;
            }
            GameObject target;
            string text = NavAnnouncer.DescribeOptionItem(items[navIndex], out target);
            if (!string.IsNullOrEmpty(text))
            {
                Speech.Say(text);
            }
        }
    }
}
