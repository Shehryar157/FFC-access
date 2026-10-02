using System.Collections.Generic;
using UnityEngine;

namespace FFCAccess
{
    /// <summary>
    /// A text box with no visuals, driven by the keyboard and spoken through the screen reader. It behaves like a
    /// Windows edit field: arrows move by character and line, Ctrl+arrows by word and paragraph, Home/End,
    /// Shift extends the selection, Ctrl+A/C/X/V. Read-only by default; when editable it also accepts typing.
    /// Subclasses decide what the lines mean (for example, which lines are choices) by overriding the virtual methods.
    /// </summary>
    internal class TextBox
    {
        protected string text = "";
        protected int caret;
        /// <summary>Where the selection started, or -1 for no selection. The selection runs from anchor to caret.</summary>
        protected int anchor = -1;
        public bool ReadOnly = true;

        private readonly List<int> lineStarts = new List<int> { 0 };
        private List<int> paragraphOfLine = new List<int> { 0 };
        private int desiredColumn = -1;

        public string Text => text;
        public int LineCount => lineStarts.Count;
        public int CurrentLine => LineOf(caret);

        // ---------- Content ----------

        /// <summary>
        /// Replace the content with these lines. paragraphs[i] is the paragraph number of line i (used by Ctrl+Up/Down);
        /// pass null to make every line its own paragraph. keepLine puts the caret at the start of that line.
        /// </summary>
        public void SetLines(IList<string> lines, IList<int> paragraphs, int keepLine = -1)
        {
            List<string> clean = new List<string>();
            foreach (string l in lines)
            {
                clean.Add((l ?? "").Replace("\n", " "));
            }
            text = string.Join("\n", clean.ToArray());
            Reindex();
            paragraphOfLine = new List<int>();
            for (int i = 0; i < lineStarts.Count; i++)
            {
                paragraphOfLine.Add(paragraphs != null && i < paragraphs.Count ? paragraphs[i] : i);
            }
            anchor = -1;
            desiredColumn = -1;
            caret = keepLine >= 0 ? LineStart(Mathf.Min(keepLine, LineCount - 1)) : 0;
        }

        /// <summary>Replace the content with plain text; each line is its own paragraph.</summary>
        public void SetText(string value)
        {
            SetLines((value ?? "").Replace("\r", "").Split('\n'), null);
        }

        private void Reindex()
        {
            lineStarts.Clear();
            lineStarts.Add(0);
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    lineStarts.Add(i + 1);
                }
            }
        }

        protected int LineOf(int pos)
        {
            int line = 0;
            for (int i = 1; i < lineStarts.Count; i++)
            {
                if (lineStarts[i] <= pos) line = i; else break;
            }
            return line;
        }

        protected int LineStart(int line) => lineStarts[line];

        /// <summary>Index just past the line's last character (where its line break is).</summary>
        protected int LineEnd(int line) => line + 1 < lineStarts.Count ? lineStarts[line + 1] - 1 : text.Length;

        public string LineText(int line) => text.Substring(LineStart(line), LineEnd(line) - LineStart(line));

        protected int ParagraphOf(int line) => line < paragraphOfLine.Count ? paragraphOfLine[line] : line;

        // ---------- What gets spoken (subclasses can change these) ----------

        /// <summary>What to say when the caret lands on a line.</summary>
        protected virtual string SpeakLine(int line)
        {
            string t = LineText(line);
            return t.Trim().Length == 0 ? "blank" : t;
        }

        /// <summary>Called when Enter is pressed. Editable boxes may want a new line; ours don't by default.</summary>
        protected virtual void OnEnter()
        {
        }

        /// <summary>Called after the text is edited.</summary>
        protected virtual void OnTextChanged()
        {
        }

        public static string CharName(char c)
        {
            switch (c)
            {
                case ' ': return "space";
                case '\t': return "tab";
                case '\n': return "blank";
                case '.': return "dot";
                case ',': return "comma";
                case '!': return "bang";
                case '?': return "question";
                case ':': return "colon";
                case ';': return "semi";
                case '\'':
                case '‘':
                case '’': return "tick";
                case '"':
                case '“':
                case '”': return "quote";
                case '-': return "dash";
                case '–': return "en dash";
                case '—': return "em dash";
                case '…': return "dot dot dot";
                case '(': return "left paren";
                case ')': return "right paren";
                case '[': return "left bracket";
                case ']': return "right bracket";
                case '/': return "slash";
                case '&': return "and";
                case '*': return "star";
                case '+': return "plus";
                case '=': return "equals";
            }
            if (char.IsUpper(c))
            {
                return "cap " + c;
            }
            return c.ToString();
        }

        private string CharAt(int pos) => pos >= text.Length ? "blank" : CharName(text[pos]);

        private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '\'' || c == '’';

        private string WordAt(int pos)
        {
            if (pos >= text.Length || text[pos] == '\n')
            {
                return "blank";
            }
            if (!IsWordChar(text[pos]))
            {
                return CharName(text[pos]);
            }
            int end = pos;
            while (end < text.Length && IsWordChar(text[end])) end++;
            return text.Substring(pos, end - pos);
        }

        /// <summary>Text for speaking a stretch the caret moved over (single characters get their names).</summary>
        private static string Readable(string s)
        {
            if (s.Length == 1)
            {
                return CharName(s[0]);
            }
            return s.Replace('\n', ' ');
        }

        protected static void Say(string s) => Speech.Say(s);

        // ---------- Keys ----------

        /// <summary>Handle a frame's keys. Returns true if one was used.</summary>
        public virtual bool HandleKeys(bool ctrl, bool shift)
        {
            if (KeyRepeat.Pressed(KeyCode.RightArrow)) { if (ctrl) MoveWord(1, shift); else MoveChar(1, shift); return true; }
            if (KeyRepeat.Pressed(KeyCode.LeftArrow)) { if (ctrl) MoveWord(-1, shift); else MoveChar(-1, shift); return true; }
            if (KeyRepeat.Pressed(KeyCode.DownArrow)) { if (ctrl) MoveParagraph(1, shift); else MoveLine(1, shift); return true; }
            if (KeyRepeat.Pressed(KeyCode.UpArrow)) { if (ctrl) MoveParagraph(-1, shift); else MoveLine(-1, shift); return true; }
            if (Input.GetKeyDown(KeyCode.Home))
            {
                if (ctrl) MoveTo(0, shift, () => SpeakLine(0));
                else MoveTo(LineStart(CurrentLine), shift, () => CharAt(caret));
                return true;
            }
            if (Input.GetKeyDown(KeyCode.End))
            {
                if (ctrl) MoveTo(text.Length, shift, () => SpeakLine(LineCount - 1));
                else MoveTo(LineEnd(CurrentLine), shift, () => "blank");
                return true;
            }
            if (ctrl && Input.GetKeyDown(KeyCode.A)) { SelectAll(); return true; }
            if (ctrl && Input.GetKeyDown(KeyCode.C)) { Copy(); return true; }
            if (ctrl && Input.GetKeyDown(KeyCode.X)) { Cut(); return true; }
            if (ctrl && Input.GetKeyDown(KeyCode.V)) { Paste(); return true; }
            if (KeyRepeat.Pressed(KeyCode.Backspace)) { DeleteBack(); return true; }
            if (KeyRepeat.Pressed(KeyCode.Delete)) { DeleteForward(); return true; }
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) { OnEnter(); return true; }
            if (!ReadOnly && !ctrl)
            {
                string typed = TypedText();
                if (typed.Length > 0)
                {
                    Insert(typed);
                    return true;
                }
            }
            return false;
        }

        private static string TypedText()
        {
            // Unity collects this frame's typed characters here; drop control characters like backspace and enter.
            string s = Input.inputString;
            if (string.IsNullOrEmpty(s))
            {
                return "";
            }
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (char c in s)
            {
                if (!char.IsControl(c)) sb.Append(c);
            }
            return sb.ToString();
        }

        // ---------- Movement ----------

        /// <summary>
        /// Move the caret. Without Shift, clear the selection and speak describe(). With Shift, extend the
        /// selection and speak the text moved over, followed by "selected" or "unselected".
        /// </summary>
        private void MoveTo(int pos, bool shift, System.Func<string> describe)
        {
            pos = Mathf.Clamp(pos, 0, text.Length);
            if (!shift)
            {
                anchor = -1;
                caret = pos;
                Say(describe());
                return;
            }
            if (pos == caret)
            {
                Say(describe());
                return;
            }
            if (anchor < 0)
            {
                anchor = caret;
            }
            int old = caret;
            caret = pos;
            string moved = text.Substring(Mathf.Min(old, pos), Mathf.Abs(pos - old));
            bool growing = Mathf.Abs(caret - anchor) > Mathf.Abs(old - anchor);
            if (caret == anchor)
            {
                anchor = -1;
            }
            Say(Readable(moved) + (growing ? " selected" : " unselected"));
        }

        private void MoveChar(int dir, bool shift)
        {
            desiredColumn = -1;
            int pos = caret + dir;
            if (pos < 0 || pos > text.Length)
            {
                Say(dir < 0 ? "Top. " + CharAt(caret) : "blank");
                return;
            }
            // NVDA says the character the caret lands on; when selecting, the character passed over.
            MoveTo(pos, shift, () => CharAt(caret));
        }

        private void MoveWord(int dir, bool shift)
        {
            desiredColumn = -1;
            int p = caret;
            if (dir > 0)
            {
                if (p < text.Length)
                {
                    if (IsWordChar(text[p])) { while (p < text.Length && IsWordChar(text[p])) p++; }
                    else if (text[p] != ' ' && text[p] != '\t') p++;
                }
                while (p < text.Length && (text[p] == ' ' || text[p] == '\t')) p++;
            }
            else
            {
                while (p > 0 && (text[p - 1] == ' ' || text[p - 1] == '\t')) p--;
                if (p > 0)
                {
                    if (IsWordChar(text[p - 1])) { while (p > 0 && IsWordChar(text[p - 1])) p--; }
                    else p--;
                }
            }
            MoveTo(p, shift, () => WordAt(caret));
        }

        private void MoveLine(int dir, bool shift)
        {
            int line = CurrentLine;
            int target = line + dir;
            if (target < 0 || target >= LineCount)
            {
                if (!shift)
                {
                    Say((dir < 0 ? "Top. " : "Bottom. ") + SpeakLine(line));
                }
                else
                {
                    MoveTo(dir < 0 ? 0 : text.Length, true, () => "");
                }
                return;
            }
            // Keep the column when moving through lines of different lengths, like a real edit field.
            if (desiredColumn < 0)
            {
                desiredColumn = caret - LineStart(line);
            }
            int column = desiredColumn;
            int pos = Mathf.Min(LineStart(target) + column, LineEnd(target));
            MoveTo(pos, shift, () => SpeakLine(target));
            desiredColumn = column;
        }

        private void MoveParagraph(int dir, bool shift)
        {
            desiredColumn = -1;
            int para = ParagraphOf(CurrentLine);
            int targetLine = -1;
            if (dir > 0)
            {
                for (int i = CurrentLine + 1; i < LineCount; i++)
                {
                    if (ParagraphOf(i) != para) { targetLine = i; break; }
                }
            }
            else
            {
                // Find the first line of the previous paragraph.
                int i = CurrentLine;
                while (i > 0 && ParagraphOf(i - 1) == para) i--;
                if (i > 0)
                {
                    int prev = ParagraphOf(i - 1);
                    i--;
                    while (i > 0 && ParagraphOf(i - 1) == prev) i--;
                    targetLine = i;
                }
            }
            if (targetLine < 0)
            {
                Say(dir < 0 ? "Top." : "Bottom.");
                return;
            }
            MoveTo(LineStart(targetLine), shift, () => SpeakParagraph(targetLine));
        }

        /// <summary>All lines of the paragraph that starts at this line.</summary>
        protected string SpeakParagraph(int firstLine)
        {
            int para = ParagraphOf(firstLine);
            List<string> parts = new List<string>();
            for (int i = firstLine; i < LineCount && ParagraphOf(i) == para; i++)
            {
                parts.Add(SpeakLine(i));
            }
            return string.Join(" ", parts.ToArray());
        }

        /// <summary>Put the caret at the start of a line and read it.</summary>
        public void GoToLine(int line, string prefix = "")
        {
            if (LineCount == 0)
            {
                return;
            }
            line = Mathf.Clamp(line, 0, LineCount - 1);
            anchor = -1;
            desiredColumn = -1;
            caret = LineStart(line);
            Say(prefix + SpeakLine(line));
        }

        // ---------- Selection and clipboard ----------

        protected bool HasSelection => anchor >= 0 && anchor != caret;

        protected string SelectedText()
        {
            if (!HasSelection) return "";
            int a = Mathf.Min(anchor, caret);
            return text.Substring(a, Mathf.Abs(caret - anchor));
        }

        private void SelectAll()
        {
            anchor = 0;
            caret = text.Length;
            Say(text.Length > 0 ? "Selected all" : "blank");
        }

        private void Copy()
        {
            if (!HasSelection)
            {
                Say("Nothing selected");
                return;
            }
            GUIUtility.systemCopyBuffer = SelectedText();
            Say("Copied");
        }

        private void Cut()
        {
            if (ReadOnly) { Say("Read only"); return; }
            if (!HasSelection) { Say("Nothing selected"); return; }
            GUIUtility.systemCopyBuffer = SelectedText();
            DeleteSelection();
            Say("Cut");
            Changed();
        }

        private void Paste()
        {
            if (ReadOnly) { Say("Read only"); return; }
            string clip = GUIUtility.systemCopyBuffer;
            if (string.IsNullOrEmpty(clip)) { Say("Clipboard is empty"); return; }
            Insert(clip.Replace("\r", ""), false);
            Say("Pasted");
        }

        // ---------- Editing ----------

        private void DeleteSelection()
        {
            int a = Mathf.Min(anchor, caret);
            text = text.Remove(a, Mathf.Abs(caret - anchor));
            caret = a;
            anchor = -1;
        }

        protected void Insert(string s, bool speak = true)
        {
            if (ReadOnly) { Say("Read only"); return; }
            if (HasSelection) DeleteSelection();
            text = text.Insert(caret, s);
            caret += s.Length;
            Changed();
            if (speak) Say(Readable(s));
        }

        private void DeleteBack()
        {
            if (ReadOnly) { Say("Read only"); return; }
            if (HasSelection) { DeleteSelection(); Say("Deleted"); Changed(); return; }
            if (caret == 0) { Say("blank"); return; }
            char removed = text[caret - 1];
            text = text.Remove(caret - 1, 1);
            caret--;
            Changed();
            Say(CharName(removed));
        }

        private void DeleteForward()
        {
            if (ReadOnly) { Say("Read only"); return; }
            if (HasSelection) { DeleteSelection(); Say("Deleted"); Changed(); return; }
            if (caret >= text.Length) { Say("blank"); return; }
            text = text.Remove(caret, 1);
            Changed();
            Say(CharAt(caret));
        }

        private void Changed()
        {
            anchor = -1;
            desiredColumn = -1;
            Reindex();
            // After an edit the old paragraph numbers no longer line up; treat each line as a paragraph.
            paragraphOfLine = new List<int>();
            for (int i = 0; i < lineStarts.Count; i++) paragraphOfLine.Add(i);
            OnTextChanged();
        }
    }
}
