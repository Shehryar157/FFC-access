using System.Collections.Generic;
using HarmonyLib;
using Rewired;
using Tin;

namespace FFCAccess
{
    /// <summary>
    /// Adds an "Accessibility" tab to the game's own options screen, next to General, Book and Player.
    /// Pressing Enter or Down on it (or clicking it) opens the mod's settings menu; Escape comes back to the tabs.
    /// </summary>
    internal static class OptionsTab
    {
        public const string Key = "accessibility";

        /// <summary>Set from the input hook; the plugin opens the settings menu on its next update.</summary>
        public static bool OpenRequested;

        public static void Patch(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(OptionsMenu), nameof(OptionsMenu.Init)),
                postfix: new HarmonyMethod(typeof(OptionsTab), nameof(InitPostfix)));
            harmony.Patch(AccessTools.Method(typeof(OptionsMenu), nameof(OptionsMenu.ShowWindow)),
                prefix: new HarmonyMethod(typeof(OptionsTab), nameof(ShowWindowPrefix)));
        }

        /// <summary>After the game builds its tabs, add ours with the game's own method.</summary>
        private static void InitPostfix(OptionsMenu __instance)
        {
            try
            {
                // Tab labels are looked up by key; give ours a label so the lookup finds it.
                Data.Set("title_settings_" + Key, "ACCESSIBILITY");
                __instance.CreateWindowButton(Key);
                Plugin.Log.LogInfo("Added Accessibility tab to the options screen.");
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError("Could not add Accessibility tab: " + e);
            }
        }

        /// <summary>
        /// The game shows a tab's window when the tab is chosen. Ours has no game window, so skip the original
        /// (returning false) and leave the screen as it was. A mouse click opens our menu straight away.
        /// </summary>
        private static bool ShowWindowPrefix(string s, ref bool __result)
        {
            if (s != Key)
            {
                return true;
            }
            __result = true;
            InputLayerManager ilm = InputLayerManager.instance;
            InputLayerManager.InputSource source = ilm != null ? ilm.GetLastSource() : InputLayerManager.InputSource.None;
            if (source == InputLayerManager.InputSource.Mouse || source == InputLayerManager.InputSource.Touch)
            {
                OpenRequested = true;
            }
            return false;
        }

        /// <summary>True when keyboard focus is on our tab in the game's options screen.</summary>
        private static bool OnOurTab()
        {
            OptionsMenu om = OptionsMenu.instance;
            if (om == null || !om.TweenerVisible)
            {
                return false;
            }
            Traverse t = Traverse.Create(om);
            if (t.Field("navIndex").GetValue<int>() >= 0)
            {
                return false;
            }
            int tab = t.Field("navTabIndex").GetValue<int>();
            List<string> tabs = om.ActiveTabs();
            return tab >= 0 && tab < tabs.Count && tabs[tab] == Key;
        }

        /// <summary>Called from the input hook. Returns true if the action was used here (so the game must not see it).</summary>
        public static bool InterceptInput(InputActionEventData e)
        {
            if (e.actionName != "Confirm" && e.actionName != "NavDown")
            {
                return false;
            }
            if (!OnOurTab())
            {
                return false;
            }
            if (e.GetButtonDown())
            {
                OpenRequested = true;
            }
            return true;
        }
    }
}
