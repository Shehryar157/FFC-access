using System;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace FFCAccess
{
    /// <summary>
    /// All typing in the game (riddle answers, number puzzles, your character's name) goes through Unity's
    /// TMP_InputField. When the game activates one, we open our own editable TextBox instead, so every keystroke
    /// and the whole text can be reviewed with the screen reader. Enter puts the text into the game's field.
    /// </summary>
    internal static class TextEntry
    {
        public static void Patch(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(TMP_InputField), nameof(TMP_InputField.ActivateInputField)),
                prefix: new HarmonyMethod(typeof(TextEntry), nameof(ActivatePrefix)));
        }

        /// <summary>Returning false stops the game's own (silent) field from taking the keyboard.</summary>
        private static bool ActivatePrefix(TMP_InputField __instance)
        {
            if (__instance == null || !__instance.gameObject.activeInHierarchy || TextWindow.Open)
            {
                return true;
            }
            try
            {
                FieldEditor editor = new FieldEditor(__instance);
                TextWindow.ShowBox("Editing " + LabelFor(__instance), editor);
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not open text entry: " + e);
                return true;
            }
        }

        /// <summary>What the field is for: its placeholder text ("Type your answer"), or the popup's message.</summary>
        public static string LabelFor(TMP_InputField field)
        {
            string label = null;
            if (field.placeholder is TMP_Text ph)
            {
                label = TextUtil.Clean(ph.text);
            }
            if (string.IsNullOrEmpty(label) && PopupPanel.instance != null && field == PopupPanel.instance.inputField)
            {
                label = TextUtil.Clean(PopupPanel.instance.contentsLabel.text);
            }
            return string.IsNullOrEmpty(label) ? "text" : label;
        }
    }

    /// <summary>An editable TextBox tied to one of the game's input fields.</summary>
    internal class FieldEditor : TextBox
    {
        private readonly TMP_InputField field;

        public FieldEditor(TMP_InputField field)
        {
            this.field = field;
            ReadOnly = false;
            SetText(field.text ?? "");
            // Start at the end, ready to type more.
            caret = text.Length;
        }

        /// <summary>Single-line field: Enter means "done".</summary>
        protected override void OnEnter()
        {
            string value = text.Replace("\n", " ");
            if (field.characterLimit > 0 && value.Length > field.characterLimit)
            {
                value = value.Substring(0, field.characterLimit);
            }
            field.text = value;
            // Tell the game the text changed, the way a finished edit would.
            field.onValueChanged?.Invoke(value);
            field.onEndEdit?.Invoke(value);
            TextWindow.Close(false);
            Speech.Say(value.Length > 0 ? "Entered " + value + "." : "Left blank.");
        }
    }
}
