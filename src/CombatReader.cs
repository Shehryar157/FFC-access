using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace FFCAccess
{
    /// <summary>
    /// Speaks dice rolls and everything shown on the combat screen. The combat logic updates the screen through a
    /// handful of EnemySheet methods (enemy appears, narration line, attack dice, damage), so hooking those lets us
    /// say exactly what a sighted player sees without re-implementing the rules.
    /// </summary>
    internal static class CombatReader
    {
        private static string lastDescription;

        public static void Patch(Harmony harmony)
        {
            Type t = typeof(CombatReader);
            harmony.Patch(AccessTools.Method(typeof(BBDiceThrower), nameof(BBDiceThrower.reportDiceValues)),
                prefix: new HarmonyMethod(t, nameof(DiceReportPrefix)));
            harmony.Patch(AccessTools.Method(typeof(RollLink), nameof(RollLink.RandomDiceValues)),
                postfix: new HarmonyMethod(t, nameof(QuickDicePostfix)));
            harmony.Patch(AccessTools.Method(typeof(EnemySheet), nameof(EnemySheet.Setup)),
                postfix: new HarmonyMethod(t, nameof(EnemySetupPostfix)));
            harmony.Patch(AccessTools.Method(typeof(EnemySheet), nameof(EnemySheet.SetDescription)),
                postfix: new HarmonyMethod(t, nameof(DescriptionPostfix)));
            harmony.Patch(AccessTools.Method(typeof(EnemySheet), nameof(EnemySheet.ShowAttackRoutine)),
                postfix: new HarmonyMethod(t, nameof(AttackRoutinePostfix)));
            harmony.Patch(AccessTools.Method(typeof(EnemySheet), nameof(EnemySheet.CompareRollRoutine)),
                postfix: new HarmonyMethod(t, nameof(CompareRollPostfix)));
            harmony.Patch(AccessTools.Method(typeof(EnemySheet), nameof(EnemySheet.TakeHit)),
                postfix: new HarmonyMethod(t, nameof(EnemyHitPostfix)));
            harmony.Patch(AccessTools.Method(typeof(AdventureSheet), nameof(AdventureSheet.TakeHitAll)),
                postfix: new HarmonyMethod(t, nameof(PlayerHitPostfix)));

            // Sounds: the end of a fight, Luck tests, and the game's own hit and dice sounds.
            harmony.Patch(AccessTools.Method(typeof(CombatController), nameof(CombatController.CheckWin)),
                postfix: new HarmonyMethod(t, nameof(CheckWinPostfix)));
            harmony.Patch(AccessTools.Method(typeof(CombatController), nameof(CombatController.CheckLose)),
                postfix: new HarmonyMethod(t, nameof(CheckLosePostfix)));
            harmony.Patch(AccessTools.Method(typeof(CombatController), nameof(CombatController.Lucky)),
                postfix: new HarmonyMethod(t, nameof(LuckyPostfix)));
            harmony.Patch(AccessTools.Method(typeof(CombatController), nameof(CombatController.Unlucky)),
                postfix: new HarmonyMethod(t, nameof(UnluckyPostfix)));
            harmony.Patch(AccessTools.Method(typeof(BBSoundFX), nameof(BBSoundFX.BBPlayCombatHit)),
                prefix: new HarmonyMethod(t, nameof(GameHitSoundPrefix)));
            harmony.Patch(AccessTools.Method(typeof(BBSoundFX), nameof(BBSoundFX.BBPlayRoll)),
                prefix: new HarmonyMethod(t, nameof(GameDiceSoundPrefix)));
        }

        public static bool InCombat => CombatController.instance != null && CombatController.instance.inCombat;

        /// <summary>The game keeps two enemy sheets (book style and flow style); only speak for the one in use.</summary>
        private static bool IsActiveSheet(EnemySheet sheet)
        {
            return CombatController.instance != null && CombatController.instance.enemySheet == sheet;
        }

        /// <summary>"3 and 5", "2, 4 and 6".</summary>
        public static string ListDice(IList<int> dice)
        {
            if (dice.Count == 0) return "nothing";
            if (dice.Count == 1) return dice[0].ToString();
            List<string> parts = new List<string>();
            for (int i = 0; i < dice.Count - 1; i++) parts.Add(dice[i].ToString());
            return string.Join(", ", parts.ToArray()) + " and " + dice[dice.Count - 1];
        }

        private static string RollText(IList<int> dice)
        {
            int total = 0;
            foreach (int d in dice) total += d;
            return "You rolled " + ListDice(dice) + (dice.Count > 1 ? ", total " + total : "") + ".";
        }

        // ---------- Dice outside combat ----------

        /// <summary>Every 3D dice throw ends here. In combat, the combat sheet's own display is spoken instead.</summary>
        private static void DiceReportPrefix(BBDiceThrower __instance)
        {
            try
            {
                if (InCombat)
                {
                    return;
                }
                Traverse tr = Traverse.Create(__instance);
                GameObject[] dice = tr.Field("dice").GetValue<GameObject[]>();
                int count = tr.Field("diceCount").GetValue<int>();
                List<int> values = new List<int>();
                for (int i = 0; i < count && dice != null && i < dice.Length; i++)
                {
                    values.Add(__instance.rollValue(dice[i]));
                }
                if (values.Count > 0)
                {
                    Speech.SayEvent(RollText(values));
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Dice announce failed: " + e);
            }
        }

        /// <summary>With the game's "Quick Dice" option the dice aren't thrown; the values come from here.</summary>
        private static void QuickDicePostfix(ArrayList __result)
        {
            if (InCombat || __result == null)
            {
                return;
            }
            List<int> values = new List<int>();
            foreach (object o in __result)
            {
                if (o is int i) values.Add(i);
            }
            if (values.Count > 0)
            {
                Speech.SayEvent(RollText(values));
            }
        }

        // ---------- Combat ----------

        private static string StatName(string configKey, string fallback)
        {
            string s = null;
            try { s = ConfigManager.instance.stringForKey(configKey); } catch { }
            return string.IsNullOrWhiteSpace(s) ? fallback : TextUtil.Clean(s);
        }

        private static void EnemySetupPostfix(EnemySheet __instance, Hashtable enemy)
        {
            if (!IsActiveSheet(__instance))
            {
                return;
            }
            try
            {
                lastDescription = null;
                string name = TextUtil.Clean(enemy["name"] as string);
                string skillName = StatName("ff combat skill name", "Skill");
                string staminaName = StatName("ff combat stamina name", "Stamina");
                Speech.SayEvent("Fight: " + name + ". " + skillName + " " + __instance["skill"].total + ", " + staminaName + " " + __instance["stamina"].stat + ".", 2f);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Enemy announce failed: " + e);
            }
        }

        private static void DescriptionPostfix(EnemySheet __instance, string s)
        {
            if (!IsActiveSheet(__instance))
            {
                return;
            }
            string text = TextUtil.Clean(s);
            // "Rolling..." is just filler while the dice tumble; the result follows straight after.
            if (text.Length == 0 || text == lastDescription || text.StartsWith("Rolling"))
            {
                return;
            }
            lastDescription = text;
            Speech.SayEvent(text);
        }

        /// <summary>
        /// ShowAttackRoutine animates both sides' dice and then writes the attack strengths on screen. A postfix on a
        /// coroutine method runs when it is created, so we start our own coroutine that waits for the totals to appear.
        /// </summary>
        private static void AttackRoutinePostfix(EnemySheet __instance, int[] player_dice, int[] enemy_dice)
        {
            Plugin.Instance.StartCoroutine(SpeakAttack(__instance, player_dice, enemy_dice));
        }

        private static IEnumerator SpeakAttack(EnemySheet sheet, int[] playerDice, int[] enemyDice)
        {
            yield return null;
            float deadline = Time.unscaledTime + 6f;
            while (string.IsNullOrEmpty(sheet.EnemyAttackValue.text) && Time.unscaledTime < deadline)
            {
                yield return null;
            }
            try
            {
                string enemyName = TextUtil.Clean(sheet["title"]?.Value?.text);
                if (string.IsNullOrEmpty(enemyName)) enemyName = "The enemy";
                List<string> parts = new List<string>();
                if (playerDice.Length > 0)
                {
                    parts.Add("You roll " + ListDice(playerDice) + ": attack strength " + sheet.player_total + ".");
                }
                if (enemyDice.Length > 0)
                {
                    parts.Add(enemyName + " rolls " + ListDice(enemyDice) + ": attack strength " + sheet.enemy_total + ".");
                }
                if (playerDice.Length > 0 && enemyDice.Length > 0)
                {
                    if (sheet.player_total > sheet.enemy_total) parts.Add("You win the round.");
                    else if (sheet.player_total < sheet.enemy_total) parts.Add(enemyName + " wins the round.");
                    else
                    {
                        parts.Add("A draw: nobody is hurt.");
                        SoundPacks.Play(SoundPacks.Draw);
                    }
                }
                Speech.SayEvent(string.Join(" ", parts.ToArray()));
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Attack announce failed: " + e);
            }
        }

        /// <summary>Tests during combat (like Testing your Luck) show the roll against your stat on the same sheet.</summary>
        private static void CompareRollPostfix(EnemySheet __instance, ArrayList rolls, string stat)
        {
            if (!IsActiveSheet(__instance))
            {
                return;
            }
            List<int> values = new List<int>();
            foreach (object o in rolls)
            {
                if (o is int i) values.Add(i);
            }
            Plugin.Instance.StartCoroutine(SpeakCompare(__instance, values, stat));
        }

        private static IEnumerator SpeakCompare(EnemySheet sheet, List<int> dice, string stat)
        {
            yield return null;
            float deadline = Time.unscaledTime + 6f;
            while (string.IsNullOrEmpty(sheet.EnemyAttackValue.text) && Time.unscaledTime < deadline)
            {
                yield return null;
            }
            int total = 0;
            foreach (int d in dice) total += d;
            Speech.SayEvent("Testing " + TextUtil.Humanize(stat) + ": you rolled " + ListDice(dice) + ", total " + total + ", against " + sheet.EnemyAttackValue.text + ".");
        }

        private static void EnemyHitPostfix(EnemySheet __instance, string target)
        {
            if (target != "enemy" || !IsActiveSheet(__instance))
            {
                return;
            }
            SoundPacks.Play(SoundPacks.PlayerHit);
            string name = TextUtil.Clean(__instance["title"]?.Value?.text);
            Speech.SayEvent((string.IsNullOrEmpty(name) ? "Enemy" : name) + " " + StatName("ff combat stamina name", "Stamina") + " " + __instance["stamina"].stat + ".");
        }

        private static void PlayerHitPostfix()
        {
            if (!InCombat)
            {
                return;
            }
            SoundPacks.Play(SoundPacks.EnemyHit);
            AdventureSheetStat s = CombatController.instance.playerSheet["stamina"];
            if (s != null)
            {
                Speech.SayEvent("Your " + StatName("ff combat stamina name", "Stamina") + " " + s.stat + ".");
            }
        }

        // ---------- Sounds ----------

        private static void CheckWinPostfix(bool __result)
        {
            if (__result) SoundPacks.Play(SoundPacks.EnemyDefeated);
        }

        private static void CheckLosePostfix(bool __result)
        {
            if (__result) SoundPacks.Play(SoundPacks.PlayerDefeated);
        }

        private static void LuckyPostfix()
        {
            SoundPacks.Play(SoundPacks.Lucky);
        }

        private static void UnluckyPostfix()
        {
            SoundPacks.Play(SoundPacks.Unlucky);
        }

        /// <summary>
        /// The game plays one generic hit sound for every hit. When the current set has its own hit sounds, ours play
        /// instead (from the damage hooks, which know who was hit), so skip the game's.
        /// </summary>
        private static bool GameHitSoundPrefix()
        {
            return !(SoundPacks.Has(SoundPacks.PlayerHit) || SoundPacks.Has(SoundPacks.EnemyHit));
        }

        /// <summary>Dice sound: play the set's own if it has one, otherwise let the game play its usual sound.</summary>
        private static bool GameDiceSoundPrefix()
        {
            return !SoundPacks.Play(SoundPacks.Dice);
        }

        /// <summary>C during a fight: both sides' current numbers.</summary>
        public static void ReadStatus()
        {
            if (!InCombat)
            {
                Speech.Say("You are not in a fight.");
                return;
            }
            CombatController cc = CombatController.instance;
            string skillName = StatName("ff combat skill name", "Skill");
            string staminaName = StatName("ff combat stamina name", "Stamina");
            EnemySheet es = cc.enemySheet;
            string enemyName = TextUtil.Clean(es["title"]?.Value?.text);
            Speech.Say("You: " + skillName + " " + cc.playerSheet["skill"].total + ", " + staminaName + " " + cc.playerSheet["stamina"].stat +
                ". " + (string.IsNullOrEmpty(enemyName) ? "Enemy" : enemyName) + ": " + skillName + " " + es["skill"].total + ", " + staminaName + " " + es["stamina"].stat + ".");
        }
    }
}
