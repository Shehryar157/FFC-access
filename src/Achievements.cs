using System;
using HarmonyLib;
using Steamworks;

namespace FFCAccess
{
    /// <summary>
    /// Steam shows achievements as a visual pop-up. Announce them instead, with the name and description
    /// that Steam has for them. Only new unlocks are announced.
    /// </summary>
    internal static class Achievements
    {
        public static void Patch(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(AchievementController), "CompleteAchievement"),
                prefix: new HarmonyMethod(typeof(Achievements), nameof(Prefix)),
                postfix: new HarmonyMethod(typeof(Achievements), nameof(Postfix)));
        }

        /// <summary>Before the game unlocks it: was it already unlocked? Harmony passes __state from prefix to postfix.</summary>
        private static void Prefix(AchievementController.Achievement achievement, out bool __state)
        {
            __state = true;
            try
            {
                bool achieved;
                if (SteamManager.Initialized && SteamUserStats.GetAchievement(achievement.SteamID, out achieved))
                {
                    __state = achieved;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Achievement check failed: " + e.Message);
            }
        }

        private static void Postfix(AchievementController.Achievement achievement, bool __state)
        {
            if (__state)
            {
                return;
            }
            try
            {
                string name = SteamUserStats.GetAchievementDisplayAttribute(achievement.SteamID, "name");
                string desc = SteamUserStats.GetAchievementDisplayAttribute(achievement.SteamID, "desc");
                if (string.IsNullOrEmpty(name))
                {
                    name = TextUtil.Humanize(achievement.id);
                }
                Speech.SayEvent("Achievement unlocked: " + name + "." + (string.IsNullOrEmpty(desc) ? "" : " " + desc), 2f);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Achievement announce failed: " + e.Message);
            }
        }
    }
}
