using System.Collections.Generic;
using UnityEngine;

namespace FFCAccess
{
    /// <summary>
    /// A modal "window" holding a read-only TextBox: used for stats, inventory and picture descriptions.
    /// While it's open, all keys go to it and the game receives no input. Escape closes it.
    /// </summary>
    internal static class TextWindow
    {
        private static TextBox box;
        private static string title;
        private static int openedFrame = -1;

        public static bool Open => box != null;

        /// <summary>Show lines of text. paragraphs[i] groups lines for Ctrl+Up/Down (null: each line its own).</summary>
        public static void Show(string windowTitle, IList<string> lines, IList<int> paragraphs = null)
        {
            TextBox b = new TextBox();
            b.SetLines(lines, paragraphs);
            ShowBox(windowTitle, b);
        }

        /// <summary>Show any TextBox, including subclasses with their own key behaviour (like the inventory).</summary>
        public static void ShowBox(string windowTitle, TextBox content)
        {
            title = windowTitle;
            box = content;
            openedFrame = Time.frameCount;
            BookReader.BlockGameInputBriefly();
            Speech.SayPriority(title + ". " + box.LineText(0), true, 0.5f);
        }

        /// <summary>Show prose: paragraphs separated by blank lines, one line per sentence.</summary>
        public static void ShowText(string windowTitle, string text)
        {
            List<string> lines = new List<string>();
            List<int> paras = new List<int>();
            int p = 0;
            foreach (string para in text.Replace("\r", "").Split(new[] { "\n\n" }, System.StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (string sentence in SectionReader.Sentences(para.Replace('\n', ' ').Trim()))
                {
                    lines.Add(sentence);
                    paras.Add(p);
                }
                p++;
            }
            if (lines.Count == 0)
            {
                lines.Add("blank");
                paras.Add(0);
            }
            Show(windowTitle, lines, paras);
        }

        public static void Close(bool announce = true)
        {
            box = null;
            BookReader.BlockGameInputBriefly();
            if (announce)
            {
                Speech.Say(title + " closed.");
            }
        }

        /// <summary>While open, the window uses every key, so nothing else sees them.</summary>
        public static bool HandleKeys(bool ctrl, bool shift)
        {
            if (box == null)
            {
                return false;
            }
            // Ignore the keypress that opened the window (often the same Enter), so it isn't used twice.
            if (Time.frameCount <= openedFrame + 1)
            {
                return true;
            }
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Close();
                return true;
            }
            box.HandleKeys(ctrl, shift);
            return true;
        }
    }
}
