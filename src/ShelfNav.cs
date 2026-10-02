using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Rewired;
using UnityEngine;
using UnityEngine.UI;

namespace FFCAccess
{
    /// <summary>
    /// The book shelf as a table: rows of books. Left/Right move along a row and stop at the ends,
    /// Up/Down change row, Home/End go to the row's ends, Ctrl+Home/End to the first and last book.
    /// We drive the game's own selection (its row and column numbers), so Enter still opens the book as usual.
    /// </summary>
    internal static class ShelfNav
    {
        private static readonly MethodInfo Refresh = AccessTools.Method(typeof(BookButtonCreator), "RefreshNavSelection");

        public static bool Active => BookButtonCreator.instance != null && BookButtonCreator.instance.isActiveAndEnabled && InputLayers.Receiving("Shelves");

        private static List<HorizontalLayoutGroup> Shelves =>
            Traverse.Create(BookButtonCreator.instance).Field("shelves").GetValue<List<HorizontalLayoutGroup>>();

        private static int Row
        {
            get => Traverse.Create(typeof(BookButtonCreator)).Field("rowSelected").GetValue<int>();
            set => Traverse.Create(typeof(BookButtonCreator)).Field("rowSelected").SetValue(value);
        }

        private static int Col
        {
            get => Traverse.Create(typeof(BookButtonCreator)).Field("colSelected").GetValue<int>();
            set => Traverse.Create(typeof(BookButtonCreator)).Field("colSelected").SetValue(value);
        }

        /// <summary>Row number, number of rows, position in row and row length, for announcements.</summary>
        public static bool Position(out int row, out int rows, out int col, out int cols)
        {
            row = rows = col = cols = 0;
            List<HorizontalLayoutGroup> shelves = BookButtonCreator.instance != null ? Shelves : null;
            if (shelves == null || shelves.Count == 0)
            {
                return false;
            }
            row = Mathf.Clamp(Row, 0, shelves.Count - 1);
            rows = shelves.Count;
            cols = shelves[row].transform.childCount;
            col = Mathf.Clamp(Col, 0, Mathf.Max(0, cols - 1));
            return true;
        }

        /// <summary>From the input hook: take over the arrow actions on the shelf. Returns true if used.</summary>
        public static bool InterceptInput(InputActionEventData e)
        {
            string a = e.actionName;
            if (a != "NavLeft" && a != "NavRight" && a != "NavUp" && a != "NavDown")
            {
                return false;
            }
            if (!Active)
            {
                return false;
            }
            if (e.GetButtonDown())
            {
                switch (a)
                {
                    case "NavLeft": Move(0, -1); break;
                    case "NavRight": Move(0, 1); break;
                    case "NavUp": Move(-1, 0); break;
                    case "NavDown": Move(1, 0); break;
                }
            }
            return true;
        }

        /// <summary>Home/End keys (not game actions, so handled from the plugin's key loop).</summary>
        public static bool HandleKeys(bool ctrl)
        {
            if (!Active)
            {
                return false;
            }
            if (Input.GetKeyDown(KeyCode.Home))
            {
                if (ctrl) Jump(0, 0); else Jump(Row, 0);
                return true;
            }
            if (Input.GetKeyDown(KeyCode.End))
            {
                List<HorizontalLayoutGroup> shelves = Shelves;
                if (shelves == null || shelves.Count == 0) return true;
                int r = ctrl ? shelves.Count - 1 : Mathf.Clamp(Row, 0, shelves.Count - 1);
                Jump(r, shelves[r].transform.childCount - 1);
                return true;
            }
            return false;
        }

        private static bool SelectionShowing()
        {
            RectTransform sel = BookButtonCreator.instance.selection;
            return sel != null && sel.gameObject.activeSelf;
        }

        private static void Move(int dRow, int dCol)
        {
            List<HorizontalLayoutGroup> shelves = Shelves;
            if (shelves == null || shelves.Count == 0)
            {
                return;
            }
            if (!SelectionShowing())
            {
                // First key press just shows (and announces) where you are, like the game does.
                Refresh.Invoke(BookButtonCreator.instance, null);
                return;
            }
            int row = Mathf.Clamp(Row, 0, shelves.Count - 1);
            int col = Col;
            if (dCol != 0)
            {
                int cols = shelves[row].transform.childCount;
                if (col + dCol < 0) { Speech.Say("Start of row."); return; }
                if (col + dCol >= cols) { Speech.Say("End of row."); return; }
                col += dCol;
            }
            else
            {
                if (row + dRow < 0) { Speech.Say("Top row."); return; }
                if (row + dRow >= shelves.Count) { Speech.Say("Bottom row."); return; }
                row += dRow;
                col = Mathf.Min(col, shelves[row].transform.childCount - 1);
            }
            Jump(row, col);
        }

        private static void Jump(int row, int col)
        {
            Row = row;
            Col = Mathf.Max(0, col);
            // The game's own method moves the highlight (and our focus hook announces the book).
            Refresh.Invoke(BookButtonCreator.instance, null);
        }
    }
}
