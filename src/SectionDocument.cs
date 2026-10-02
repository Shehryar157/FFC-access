using System.Collections.Generic;
using UnityEngine;

namespace FFCAccess
{
    /// <summary>
    /// The book section as a read-only text box. Inherits all the normal text keys from TextBox and adds:
    /// one line per sentence, choice lines at the end, Tab/Shift+Tab to jump between choices,
    /// and Enter or Space on a choice to pick it.
    /// </summary>
    internal class SectionDocument : TextBox
    {
        public SectionContent Content { get; private set; } = new SectionContent();

        // For each line: which choice it is (-1 if none), and which illustration (null if none).
        private readonly List<int> choiceOfLine = new List<int>();
        private readonly List<string> imageOfLine = new List<string>();

        public void Load(SectionContent content, bool keepPosition)
        {
            int keep = keepPosition ? CurrentLine : -1;
            Content = content;
            List<string> lines = new List<string>();
            List<int> paragraphs = new List<int>();
            choiceOfLine.Clear();
            imageOfLine.Clear();
            // One line per sentence; a choice is its own line, in the text where it occurs.
            foreach (Block b in content.Blocks)
            {
                switch (b.Kind)
                {
                    case BlockKind.Text:
                        foreach (string sentence in SectionReader.Sentences(b.Text))
                        {
                            Add(lines, paragraphs, sentence, b.Para, -1, null);
                        }
                        break;
                    case BlockKind.Choice:
                        Add(lines, paragraphs, SectionReader.DescribeChoice(content, b.ChoiceIndex), b.Para, b.ChoiceIndex, null);
                        break;
                    default:
                        Add(lines, paragraphs, SectionReader.BlockSpeech(content, b), b.Para, -1, b.ImageKey);
                        break;
                }
            }
            if (lines.Count == 0)
            {
                Add(lines, paragraphs, "No text.", 0, -1, null);
            }
            SetLines(lines, paragraphs, keep);
        }

        private void Add(List<string> lines, List<int> paragraphs, string text, int para, int choice, string image)
        {
            lines.Add(text);
            paragraphs.Add(para);
            choiceOfLine.Add(choice);
            imageOfLine.Add(image);
        }

        private int ChoiceAt(int line) => line >= 0 && line < choiceOfLine.Count ? choiceOfLine[line] : -1;

        public string ImageAt(int line) => line >= 0 && line < imageOfLine.Count ? imageOfLine[line] : null;

        /// <summary>Choice lines are described fresh each time, so "unavailable" stays up to date after rolls.</summary>
        protected override string SpeakLine(int line)
        {
            int choice = ChoiceAt(line);
            if (choice >= 0 && choice < Content.Choices.Count)
            {
                return SectionReader.DescribeChoice(Content, choice);
            }
            return base.SpeakLine(line);
        }

        public override bool HandleKeys(bool ctrl, bool shift)
        {
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                MoveChoice(shift ? -1 : 1);
                return true;
            }
            if (!ctrl && Input.GetKeyDown(KeyCode.D))
            {
                ShowPicture();
                return true;
            }
            if (!ctrl && !shift && Input.GetKeyDown(KeyCode.Space) && ChoiceAt(CurrentLine) >= 0)
            {
                OnEnter();
                return true;
            }
            return base.HandleKeys(ctrl, shift);
        }

        protected override void OnEnter()
        {
            int choice = ChoiceAt(CurrentLine);
            if (choice < 0)
            {
                Say(Content.Choices.Count > 0 ? "Not on a choice. Press Tab to go to the choices." : "No choices here.");
                return;
            }
            BookReader.ActivateChoice(Content.Choices[choice].Link);
        }

        /// <summary>D: the full description of the picture on this line, or else the section's first picture.</summary>
        public void ShowPicture()
        {
            string key = ImageAt(CurrentLine);
            if (key == null)
            {
                foreach (string k in imageOfLine)
                {
                    if (k != null) { key = k; break; }
                }
            }
            if (key == null)
            {
                Say("No illustration here.");
                return;
            }
            Descriptions.ShowFull(key);
        }

        private void MoveChoice(int dir)
        {
            if (Content.Choices.Count == 0)
            {
                Say("No choices here.");
                return;
            }
            for (int i = CurrentLine + dir; i >= 0 && i < LineCount; i += dir)
            {
                if (ChoiceAt(i) >= 0)
                {
                    GoToLine(i);
                    return;
                }
            }
            Say(dir > 0 ? "No more choices." : "No previous choices.");
        }
    }
}
