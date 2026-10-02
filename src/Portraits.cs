using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace FFCAccess
{
    /// <summary>
    /// The portrait picker (new adventure screen, and the player tab in options) flips through pictures with
    /// left and right buttons and says nothing. Announce which portrait is showing, with a description from
    /// descriptions/portraits.json when there is one.
    /// </summary>
    internal static class Portraits
    {
        public static void Patch(Harmony harmony)
        {
            MethodInfoPatch(harmony, typeof(NewAdventureDisplay));
            MethodInfoPatch(harmony, typeof(OptionsMenu));
        }

        private static void MethodInfoPatch(Harmony harmony, Type t)
        {
            harmony.Patch(AccessTools.Method(t, "CyclePortrait"),
                postfix: new HarmonyMethod(typeof(Portraits), nameof(CyclePostfix)));
        }

        private static void CyclePostfix(object __instance)
        {
            try
            {
                Image img = Traverse.Create(__instance).Field("Portrait").GetValue<Image>();
                int index = Traverse.Create(__instance).Field("curr_portrait").GetValue<int>();
                int count = ResourceMaster.AvailablePortraits().Count;
                Speech.Say(Describe(img != null ? img.sprite : null, index, count));
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Portrait announce failed: " + e);
            }
        }

        public static string Describe(Sprite sprite, int index, int count)
        {
            string name = sprite != null ? sprite.name : "";
            Descriptions.Entry e = Descriptions.FindIn("portraits", name);
            if (e == null)
            {
                Plugin.Log.LogInfo("Portrait without description: " + name);
            }
            string text = e != null && !string.IsNullOrEmpty(e.Short) ? e.Short : TextUtil.Humanize(name);
            return "Portrait " + (index + 1) + " of " + count + ": " + text;
        }
    }
}
