using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using Rewired;
using UnityEngine;

namespace FFCAccess
{
    /// <summary>
    /// Lets you review the current section like a document in NVDA's browse mode:
    /// Up/Down move by sentence, Ctrl+Up/Down by paragraph, Tab/Shift+Tab jump between choices,
    /// Enter picks the choice you're on. Only active on the book page itself, not in menus or popups.
    /// </summary>
    internal static class BookReader
    {
        private class Line
        {
            public string Text;
            public int Para;
            public int Choice = -1;
        }

        private static readonly List<Line> lines = new List<Line>();
        private static int cursor = -1;

        /// <summary>True on frames where the book page is the screen taking input. Recomputed every frame.</summary>
        public static bool Active { get; private set; }

        /// <summary>True while we're flipping pages to reach a choice, so page announcements stay quiet.</summary>
        public static bool AutoTurning { get; private set; }

        // Game actions we take over on the reading screen. Everything else (menu, page turning, etc.) still reaches the game.
        private static readonly HashSet<string> StolenActions = new HashSet<string> { "NavUp", "NavDown", "Confirm" };

        public static void Patch(Harmony harmony)
        {
            SectionReader.Rebuilt += Rebuild;
            harmony.Patch(AccessTools.Method(typeof(InputLayerManager), "OnInputUpdate"),
                prefix: new HarmonyMethod(typeof(BookReader), nameof(InputPrefix)));
        }

        /// <summary>
        /// Runs before the game handles each input action. Returning false skips the game's handler,
        /// so on the reading screen the game never sees the keys we use.
        /// </summary>
        private static bool InputPrefix(InputActionEventData eventData)
        {
            return !(Active && StolenActions.Contains(eventData.actionName));
        }

        /// <summary>Called once per frame by the plugin.</summary>
        public static void Tick()
        {
            bool now = ComputeActive();
            if (now != Active)
            {
                Plugin.Log.LogInfo("Reading mode " + (now ? "on" : "off"));
            }
            Active = now;
        }

        /// <summary>
        /// The game stacks "input layers": each screen adds one and the top blocking layer gets the keys.
        /// The book page's layer is named "Book-...". We're active when it's the top layer receiving input.
        /// </summary>
        private static bool ComputeActive()
        {
            if (!SectionReader.InBook() || SectionReader.IsFlowStyle())
            {
                return false;
            }
            InputLayerManager ilm = InputLayerManager.instance;
            if (ilm == null || ilm.inputLayers == null)
            {
                return false;
            }
            try
            {
                if (PopupPanel.instance != null && PopupPanel.visible)
                {
                    return false;
                }
            }
            catch
            {
            }
            for (int i = ilm.inputLayers.Count - 1; i >= 0; i--)
            {
                InputLayer layer = ilm.inputLayers[i];
                if (layer == null)
                {
                    continue;
                }
                if (layer.id != null && layer.id.StartsWith("Book-"))
                {
                    return true;
                }
                if (layer.blockInputs)
                {
                    return false;
                }
            }
            return false;
        }

        /// <summary>Turn the section's paragraphs and choices into lines: one per sentence, one per choice.</summary>
        private static void Rebuild()
        {
            lines.Clear();
            cursor = -1;
            int p = 0;
            foreach (string para in SectionReader.Paragraphs)
            {
                foreach (string s in SectionReader.Sentences(para))
                {
                    lines.Add(new Line { Text = s, Para = p });
                }
                p++;
            }
            for (int i = 0; i < SectionReader.Choices.Count; i++)
            {
                lines.Add(new Line { Para = p + i, Choice = i });
            }
        }

        private static string Speak(Line line)
        {
            return line.Choice >= 0 ? SectionReader.DescribeChoice(line.Choice) : line.Text;
        }

        /// <summary>Handle the reading keys. Returns true if a key was used.</summary>
        public static bool HandleKeys(bool ctrl, bool shift)
        {
            if (!Active)
            {
                return false;
            }
            if (lines.Count == 0 && SectionReader.Current != null)
            {
                SectionReader.Build(SectionReader.Current);
            }
            if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                if (ctrl) MoveParagraph(1); else MoveLine(1);
                return true;
            }
            if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                if (ctrl) MoveParagraph(-1); else MoveLine(-1);
                return true;
            }
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                MoveChoice(shift ? -1 : 1);
                return true;
            }
            if (ctrl && Input.GetKeyDown(KeyCode.Home))
            {
                Jump(0, "Top");
                return true;
            }
            if (ctrl && Input.GetKeyDown(KeyCode.End))
            {
                Jump(lines.Count - 1, "Bottom");
                return true;
            }
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space))
            {
                ActivateCurrent();
                return true;
            }
            return false;
        }

        private static void MoveLine(int dir)
        {
            if (lines.Count == 0)
            {
                Speech.Say("Nothing to read.");
                return;
            }
            int next = cursor + dir;
            if (next < 0 || next >= lines.Count)
            {
                // Like NVDA: say we hit the edge, then repeat where we are.
                cursor = Mathf.Clamp(cursor, 0, lines.Count - 1);
                Speech.Say((dir < 0 ? "Top. " : "Bottom. ") + Speak(lines[cursor]));
                return;
            }
            cursor = next;
            Speech.Say(Speak(lines[cursor]));
        }

        private static void MoveParagraph(int dir)
        {
            if (lines.Count == 0)
            {
                Speech.Say("Nothing to read.");
                return;
            }
            int currentPara = cursor >= 0 ? lines[Mathf.Min(cursor, lines.Count - 1)].Para : -1;
            int targetPara = currentPara + dir;
            int maxPara = lines[lines.Count - 1].Para;
            if (targetPara < 0 || targetPara > maxPara)
            {
                Speech.Say(dir < 0 ? "Top." : "Bottom.");
                return;
            }
            // Put the cursor on the paragraph's first line and read the whole paragraph.
            cursor = lines.FindIndex(l => l.Para == targetPara);
            List<string> parts = new List<string>();
            foreach (Line l in lines)
            {
                if (l.Para == targetPara)
                {
                    parts.Add(Speak(l));
                }
            }
            Speech.Say(string.Join(" ", parts.ToArray()));
        }

        private static void MoveChoice(int dir)
        {
            if (SectionReader.Choices.Count == 0)
            {
                Speech.Say("No choices in this section.");
                return;
            }
            int i = cursor;
            for (int step = 0; step < lines.Count; step++)
            {
                i += dir;
                if (i < 0 || i >= lines.Count)
                {
                    break;
                }
                if (lines[i].Choice >= 0)
                {
                    cursor = i;
                    Speech.Say(Speak(lines[i]));
                    return;
                }
            }
            Speech.Say(dir > 0 ? "No more choices." : "No previous choices.");
        }

        private static void Jump(int index, string edge)
        {
            if (lines.Count == 0)
            {
                Speech.Say("Nothing to read.");
                return;
            }
            cursor = index;
            Speech.Say(edge + ". " + Speak(lines[cursor]));
        }

        private static void ActivateCurrent()
        {
            if (cursor < 0 || cursor >= lines.Count || lines[cursor].Choice < 0)
            {
                Speech.Say("Not on a choice. Press Tab to go to the choices.");
                return;
            }
            StoryLink link = SectionReader.Choices[lines[cursor].Choice].Link;
            bool blocked = false;
            try { blocked = link.IsBlock(); } catch { }
            if (blocked)
            {
                Speech.Say("That choice is unavailable.");
                return;
            }
            Plugin.Instance.StartCoroutine(Activate(link));
        }

        /// <summary>
        /// Press the game's own button for this choice. The book spreads a section over several pages and only
        /// the visible page's buttons work, so first flip to the page that holds it.
        /// </summary>
        private static IEnumerator Activate(StoryLink link)
        {
            PageTurner turner = SectionController.instance != null ? SectionController.instance.currentPageTurner : null;
            if (turner == null)
            {
                Speech.Say("Can't find the book page.");
                yield break;
            }
            int pageIdx = -1;
            StoryLinkAction button = null;
            for (int p = 0; p < turner.pages.Count && button == null; p++)
            {
                Page page = turner.pages[p];
                foreach (StoryLinkAction sla in Both(page))
                {
                    if (sla != null && sla.storyLink == link)
                    {
                        button = sla;
                        pageIdx = p;
                        break;
                    }
                }
            }
            if (button == null)
            {
                Plugin.Log.LogWarning("No button found for choice: " + SectionReader.LinkWords(link));
                Speech.Say("Can't find that choice's button.");
                yield break;
            }
            AutoTurning = true;
            float deadline = Time.unscaledTime + 10f;
            while (turner.pageIndex != pageIdx && Time.unscaledTime < deadline)
            {
                if (!IsTurning(turner))
                {
                    int before = turner.pageIndex;
                    if (pageIdx > turner.pageIndex) turner.TurnLeft(); else turner.TurnRight();
                    if (!IsTurning(turner) && turner.pageIndex == before)
                    {
                        break; // The game refused to turn.
                    }
                }
                yield return null;
            }
            while (IsTurning(turner) && Time.unscaledTime < deadline)
            {
                yield return null;
            }
            AutoTurning = false;
            if (turner.pageIndex != pageIdx)
            {
                Plugin.Log.LogWarning("Could not turn to page " + pageIdx + " (at " + turner.pageIndex + ")");
            }
            Plugin.Log.LogInfo("Activating choice: " + SectionReader.LinkWords(link));
            button.OnClick();
        }

        private static IEnumerable<StoryLinkAction> Both(Page page)
        {
            foreach (StoryLinkAction s in page.leftButtons) yield return s;
            foreach (StoryLinkAction s in page.rightButtons) yield return s;
        }

        private static bool IsTurning(PageTurner turner)
        {
            return Traverse.Create(turner).Field("isTurning").GetValue<bool>();
        }
    }
}
