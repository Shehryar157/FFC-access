using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using Rewired;
using UnityEngine;
using UnityEngine.UI;

namespace FFCAccess
{
    /// <summary>
    /// The game's popups (messages, dice results, item pickups, confirmations) become a read-only text box:
    /// Up/Down read the message line by line, Tab moves between the buttons, Enter presses the game's own button.
    /// The whole popup is still read aloud when it appears.
    /// </summary>
    internal static class PopupReader
    {
        private static readonly PopupDocument doc = new PopupDocument();

        // The game's popup keys we take over while the popup is the active screen.
        private static readonly HashSet<string> StolenActions = new HashSet<string> { "NavUp", "NavDown", "NavLeft", "NavRight", "Confirm" };

        /// <summary>True while a popup is the screen taking input (and we've built its document).</summary>
        public static bool Active
        {
            get
            {
                PopupPanel p = PopupPanel.instance;
                return doc.Panel != null && p != null && PopupPanel.visible && !TextWindow.Open && !SettingsMenu.Open
                    && InputLayers.Receiving("PopupPanel");
            }
        }

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
                doc.Load(panel);
                Speech.SayEvent(Describe(panel), 2f);
                NavAnnouncer.Reset();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Popup read failed: " + e);
            }
        }

        /// <summary>From the input hook: keep the game's own popup navigation from also moving.</summary>
        public static bool InterceptInput(InputActionEventData e)
        {
            return Active && StolenActions.Contains(e.actionName);
        }

        public static bool HandleKeys(bool ctrl, bool shift)
        {
            return Active && doc.HandleKeys(ctrl, shift);
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
                parts.Add(Trading.Describe(panel.Trader));
            }
            if (panel.Better != null && panel.Better.gameObject.activeSelf)
            {
                parts.Add(Trading.Describe(panel.Better));
            }
            List<string> buttons = new List<string>();
            foreach (Button b in panel.buttons)
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

    /// <summary>A popup as a text box. Lines know whether they are a button or the text field.</summary>
    internal class PopupDocument : TextBox
    {
        public PopupPanel Panel { get; private set; }

        // For each line: the game button it stands for (null if none), and whether it's the text field.
        private readonly List<Button> buttonOfLine = new List<Button>();
        private readonly List<bool> inputOfLine = new List<bool>();
        private int buttonCount;

        public void Load(PopupPanel panel)
        {
            Panel = panel;
            List<string> lines = new List<string>();
            List<int> paras = new List<int>();
            buttonOfLine.Clear();
            inputOfLine.Clear();
            int para = 0;
            Action<string, Button, bool> add = (t, b, input) =>
            {
                lines.Add(t);
                paras.Add(para);
                buttonOfLine.Add(b);
                inputOfLine.Add(input);
            };
            if (panel.TitleObject != null && panel.TitleObject.activeSelf && panel.contentsTitle != null)
            {
                string title = TextUtil.Clean(panel.contentsTitle.text);
                if (title.Length > 0) { add(title, null, false); para++; }
            }
            if (panel.contentsLabel != null && panel.contentsLabel.gameObject.activeSelf)
            {
                // Keep the message's own paragraphs; one line per sentence inside them.
                foreach (string p in TextUtil.Clean(panel.contentsLabel.text).Split('\n'))
                {
                    foreach (string sentence in SectionReader.Sentences(p.Trim()))
                    {
                        add(sentence, null, false);
                    }
                    para++;
                }
            }
            foreach (TradePopup t in new[] { panel.Trader, panel.Better })
            {
                if (t != null && t.gameObject.activeSelf)
                {
                    add(Trading.Describe(t), null, false);
                    para++;
                }
            }
            if (panel.inputField != null && panel.inputField.gameObject.activeSelf)
            {
                add("text field", null, true);
                para++;
            }
            buttonCount = 0;
            foreach (Button b in panel.buttons)
            {
                if (b != null && b.gameObject.activeSelf)
                {
                    buttonCount++;
                    add(TextUtil.LabelFor(b.gameObject), b, false);
                    para++;
                }
            }
            if (lines.Count == 0)
            {
                add("Empty message.", null, false);
            }
            SetLines(lines, paras);
        }

        private int ButtonNumber(int line)
        {
            int n = 0;
            for (int i = 0; i <= line && i < buttonOfLine.Count; i++)
            {
                if (buttonOfLine[i] != null) n++;
            }
            return n;
        }

        /// <summary>Buttons and the text field are described live, so changes (like a typed answer) are heard.</summary>
        protected override string SpeakLine(int line)
        {
            if (line < inputOfLine.Count && inputOfLine[line] && Panel != null)
            {
                string value = Panel.inputField.text;
                return TextEntry.LabelFor(Panel.inputField) + ", edit, " + (string.IsNullOrEmpty(value) ? "blank" : value);
            }
            Button b = line < buttonOfLine.Count ? buttonOfLine[line] : null;
            if (b != null)
            {
                return TextUtil.LabelFor(b.gameObject) + ", button, " + ButtonNumber(line) + " of " + buttonCount + (b.interactable ? "" : ", unavailable");
            }
            return base.SpeakLine(line);
        }

        public override bool HandleKeys(bool ctrl, bool shift)
        {
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                MoveToControl(shift ? -1 : 1);
                return true;
            }
            if (!ctrl && !shift && Input.GetKeyDown(KeyCode.Space) && IsControl(CurrentLine))
            {
                OnEnter();
                return true;
            }
            return base.HandleKeys(ctrl, shift);
        }

        private bool IsControl(int line)
        {
            return line < buttonOfLine.Count && (buttonOfLine[line] != null || inputOfLine[line]);
        }

        private void MoveToControl(int dir)
        {
            for (int i = CurrentLine + dir; i >= 0 && i < LineCount; i += dir)
            {
                if (IsControl(i))
                {
                    GoToLine(i);
                    return;
                }
            }
            Say(dir > 0 ? "No more buttons." : "No previous buttons.");
        }

        /// <summary>Enter presses the game's own button, or opens the text field for typing.</summary>
        protected override void OnEnter()
        {
            int line = CurrentLine;
            if (line < inputOfLine.Count && inputOfLine[line])
            {
                Panel.inputField.onSelect?.Invoke(null);
                return;
            }
            Button b = line < buttonOfLine.Count ? buttonOfLine[line] : null;
            if (b == null)
            {
                Say("Not on a button. Press Tab to go to the buttons.");
                return;
            }
            if (!b.interactable)
            {
                Say("That button is unavailable.");
                return;
            }
            b.onClick.Invoke();
        }
    }
}
