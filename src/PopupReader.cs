using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace FFCAccess
{
    /// <summary>Speaks the game's popup dialogs (messages, item pickups, roll results, confirmations).</summary>
    internal static class PopupReader
    {
        public static void Patch(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(PopupPanel), nameof(PopupPanel.ShowPopup)),
                postfix: new HarmonyMethod(typeof(PopupReader), nameof(ShowPopupPostfix)));
        }

        private static void ShowPopupPostfix(PopupPanel __instance)
        {
            // Some callers set the buttons after ShowPopup, so read on the next frame.
            Plugin.Instance.StartCoroutine(ReadNextFrame(__instance));
        }

        private static IEnumerator ReadNextFrame(PopupPanel panel)
        {
            yield return null;
            try
            {
                Speech.SayPriority(Describe(panel), true, 2f);
                NavAnnouncer.Reset();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Popup read failed: " + e);
            }
        }

        public static string Describe(PopupPanel panel)
        {
            List<string> parts = new List<string>();
            if (panel.TitleObject != null && panel.TitleObject.activeSelf && panel.contentsTitle != null)
            {
                parts.Add(TextUtil.Clean(panel.contentsTitle.text));
            }
            if (panel.contentsLabel != null && panel.contentsLabel.gameObject.activeSelf)
            {
                parts.Add(TextUtil.Clean(panel.contentsLabel.text));
            }
            if (panel.Trader != null && panel.Trader.gameObject.activeSelf)
            {
                parts.Add(TextUtil.Join(TextUtil.TextsUnder(panel.Trader.gameObject, false), ", "));
            }
            if (panel.Better != null && panel.Better.gameObject.activeSelf)
            {
                parts.Add(TextUtil.Join(TextUtil.TextsUnder(panel.Better.gameObject, false), ", "));
            }
            List<string> buttons = new List<string>();
            foreach (UnityEngine.UI.Button b in panel.buttons)
            {
                if (b != null && b.gameObject.activeSelf)
                {
                    buttons.Add(TextUtil.LabelFor(b.gameObject));
                }
            }
            if (buttons.Count > 0)
            {
                parts.Add("Buttons: " + string.Join(", ", buttons.ToArray()));
            }
            if (panel.inputField != null && panel.inputField.gameObject.activeSelf)
            {
                parts.Add("Text entry field");
            }
            return TextUtil.Join(parts);
        }
    }
}
