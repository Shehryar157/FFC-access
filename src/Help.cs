using System;
using System.Collections.Generic;

namespace FFCAccess
{
    /// <summary>
    /// F1: every key the mod adds, as a list in a text window, grouped by screen. The cursor starts on the group for
    /// the screen you're on. Enter on a command (stats, map, settings...) closes the help and does it.
    /// </summary>
    internal static class Help
    {
        private class Entry
        {
            public string Text;
            public Action Run;
        }

        private class Group
        {
            public string Name;
            public List<Entry> Entries = new List<Entry>();

            public Group Key(string text, Action run = null)
            {
                Entries.Add(new Entry { Text = text, Run = run });
                return this;
            }
        }

        private static List<Group> Build()
        {
            List<Group> g = new List<Group>();

            g.Add(new Group { Name = "Anywhere" }
                .Key("F1: this help.")
                .Key("F2: read the whole section again.", BookReader.ReadAll)
                .Key("I: your inventory.", CharacterInfo.ReadInventory)
                .Key("M: the map, in books that have one.", MapReader.Open)
                .Key("F6: open the game's Adventure Sheet.", CharacterInfo.OpenAdventureSheet)
                .Key("F7: read everything on the screen.", Plugin.ReadScreen)
                .Key("F8: repeat the last message.", () => Speech.Say(lastBeforeHelp))
                .Key("F9: mod settings, also on the Accessibility tab of the game's options.", SettingsMenu.Toggle)
                .Key("F10: save a screen dump for reporting problems.", Diagnostics.DumpScene));

            g.Add(new Group { Name = "Book page" }
                .Key("Up and Down arrows: previous or next sentence, choices included.")
                .Key("Control with Up or Down: previous or next paragraph.")
                .Key("Left and Right arrows: previous or next character. With Control: by word.")
                .Key("Home and End: start or end of the line. With Control: top or bottom.")
                .Key("Shift with any movement key: select text. Control C copies, Control A selects all.")
                .Key("Tab and Shift Tab: next or previous choice.")
                .Key("Enter or Space: take the choice you are on.")
                .Key("S: your stats.", CharacterInfo.ReadStats)
                .Key("D: full description of the illustration.", BookReader.DescribePicture)
                .Key("B: go back to the previous section. The game asks first.", FreeRead.GoBack)
                .Key("F: free choice, unlocking every choice in this section. The game asks first.", FreeRead.FreeChoice)
                .Key("H: heal your Stamina. The game asks first.", FreeRead.Heal)
                .Key("Page Up and Page Down: turn pages, in page by page layout."));

            g.Add(new Group { Name = "Popups" }
                .Key("Up and Down arrows: read the message line by line.")
                .Key("Tab and Shift Tab: next or previous button.")
                .Key("Enter or Space: press the button, or type in a text field."));

            g.Add(new Group { Name = "Fights" }
                .Key("Left and Right arrows: choose an action, such as Attack or Test your Luck.")
                .Key("Enter: take the action.")
                .Key("C: both sides' Skill and Stamina.", CombatReader.ReadStatus)
                .Key("S: your stats.", CharacterInfo.ReadStats));

            g.Add(new Group { Name = "Trading and betting" }
                .Key("Left and Right arrows: change the first amount.")
                .Key("Shift with Left or Right: change the second amount.")
                .Key("T: read both amounts."));

            g.Add(new Group { Name = "Book shelf" }
                .Key("Left and Right arrows: along the row. With Control: 5 books. With Alt: 10 books.")
                .Key("Up and Down arrows: change rows.")
                .Key("Home and End: start or end of the row. With Control: first or last book.")
                .Key("Enter: open the book."));

            g.Add(new Group { Name = "Text windows: stats, inventory, map, descriptions and this help" }
                .Key("Arrows and Control arrows: read, as on the book page.")
                .Key("Enter: use an inventory item, get a route on the map, or do a command in this help.")
                .Key("Escape: close the window."));

            g.Add(new Group { Name = "Typing answers and names" }
                .Key("Type as usual. Arrows, Backspace, Delete and Control V work as in any text field.")
                .Key("Enter: confirm. Escape: cancel."));
            return g;
        }

        /// <summary>Which group to start on, depending on what's on screen.</summary>
        private static string CurrentGroup()
        {
            if (CombatReader.InCombat) return "Fights";
            if (PopupReader.Active) return "Popups";
            if (BookReader.Active) return "Book page";
            if (ShelfNav.Active) return "Book shelf";
            return "Anywhere";
        }

        private static string lastBeforeHelp = "";

        public static void Open()
        {
            lastBeforeHelp = Speech.Last;
            List<Group> groups = Build();
            string start = CurrentGroup();
            List<string> lines = new List<string>();
            List<int> paras = new List<int>();
            List<Action> actions = new List<Action>();
            int startLine = 0;
            for (int i = 0; i < groups.Count; i++)
            {
                if (groups[i].Name == start) startLine = lines.Count;
                lines.Add(groups[i].Name + ":");
                paras.Add(i);
                actions.Add(null);
                foreach (Entry e in groups[i].Entries)
                {
                    lines.Add(e.Text);
                    paras.Add(i);
                    actions.Add(e.Run);
                }
            }
            HelpBox box = new HelpBox(actions);
            box.SetLines(lines, paras, startLine);
            TextWindow.ShowBox("Help", box);
        }

        private class HelpBox : TextBox
        {
            private readonly List<Action> actions;

            public HelpBox(List<Action> actions)
            {
                this.actions = actions;
            }

            protected override void OnEnter()
            {
                Action run = CurrentLine < actions.Count ? actions[CurrentLine] : null;
                if (run == null)
                {
                    Say("That's a key to use directly.");
                    return;
                }
                // Close the help first: the command may open its own window or need the screen behind.
                TextWindow.Close(false);
                run();
            }
        }
    }
}
