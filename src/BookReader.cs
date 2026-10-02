using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using Rewired;
using UnityEngine;

namespace FFCAccess
{
    /// <summary>
    /// Runs the reading experience on the book page: owns the SectionDocument text box, works out when the book
    /// page is the active screen, keeps the game from also reacting to our keys, handles page-by-page layout,
    /// and presses the game's buttons when you pick a choice.
    /// </summary>
    internal static class BookReader
    {
        private static readonly SectionDocument doc = new SectionDocument();

        /// <summary>True on frames where the book page is the screen taking input. Recomputed every frame.</summary>
        public static bool Active { get; private set; }

        /// <summary>True while we're flipping pages to reach a choice, so page announcements stay quiet.</summary>
        public static bool AutoTurning { get; private set; }

        private static int blockAllUntilFrame;

        // Game actions we take over on the reading screen. Menu, Cancel, Inventory etc. still reach the game.
        private static readonly HashSet<string> StolenActions = new HashSet<string>
        {
            "NavUp", "NavDown", "NavLeft", "NavRight", "Confirm", "ShoulderLeft", "ShoulderRight"
        };

        private static bool PageMode => ModSettings.Layout.Value == ReadingLayout.PageByPage;

        private static PageTurner Turner => SectionController.instance != null ? SectionController.instance.currentPageTurner : null;

        public static void Patch(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(InputLayerManager), "OnInputUpdate"),
                prefix: new HarmonyMethod(typeof(BookReader), nameof(InputPrefix)));
        }

        /// <summary>
        /// Runs before the game handles each input action. Returning false skips the game's handler,
        /// so the game never sees keys that the mod is using.
        /// </summary>
        private static bool InputPrefix(InputActionEventData eventData)
        {
            if (SettingsMenu.Open || TextWindow.Open || Time.frameCount <= blockAllUntilFrame)
            {
                return false;
            }
            if (OptionsTab.InterceptInput(eventData))
            {
                return false;
            }
            if (Active && eventData.actionName == "Inventory")
            {
                // Borrow the game's own Inventory key: on the book page it opens our inventory window instead.
                if (eventData.GetButtonDown())
                {
                    InventoryRequested = true;
                }
                return false;
            }
            return !(Active && StolenActions.Contains(eventData.actionName));
        }

        /// <summary>Set when the game's Inventory key was pressed on the book page; the plugin opens our inventory.</summary>
        public static bool InventoryRequested;

        /// <summary>Keep all keys from the game for a couple of frames (used when closing our own menu with Escape).</summary>
        public static void BlockGameInputBriefly()
        {
            blockAllUntilFrame = Time.frameCount + 2;
        }

        /// <summary>Called once per frame by the plugin.</summary>
        public static void Tick()
        {
            bool now = ComputeActive();
            if (now != Active)
            {
                Plugin.Log.LogInfo("Reading mode " + (now ? "on" : "off"));
                if (now)
                {
                    // Coming back from a popup or menu: things may have changed (a roll unlocked a choice), so refresh.
                    Reload(true);
                }
            }
            Active = now;
        }

        /// <summary>
        /// The game stacks "input layers": each screen adds one and the top blocking layer gets the keys.
        /// The book page's layer is named "Book-...". We're active when it's the top layer receiving input.
        /// </summary>
        private static bool ComputeActive()
        {
            if (SettingsMenu.Open || TextWindow.Open || !SectionReader.InBook() || SectionReader.IsFlowStyle())
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

        // ---------- Building the document ----------

        private static SectionContent BuildContent()
        {
            BBSection section = SectionReader.Current;
            PageTurner turner = Turner;
            if (PageMode && turner != null && turner.pages != null && turner.pageIndex < turner.pages.Count)
            {
                // Each page remembers which token it starts at, so a page is the tokens up to where the next one starts.
                int pi = turner.pageIndex;
                int from = turner.pages[pi].tokenIndex;
                int to = pi < turner.pageContentIndex && pi + 1 < turner.pages.Count ? turner.pages[pi + 1].tokenIndex : int.MaxValue;
                return SectionReader.Build(section, from, to, false);
            }
            return SectionReader.Build(section, 0, int.MaxValue, ModSettings.AnnouncePageBreaks.Value);
        }

        /// <summary>Rebuild the document, e.g. after a setting changed or a popup closed.</summary>
        public static void Reload(bool keepPosition)
        {
            if (!SectionReader.InBook() || SectionReader.Current == null)
            {
                return;
            }
            doc.Load(BuildContent(), keepPosition);
        }

        private static string PageLabel()
        {
            PageTurner turner = Turner;
            if (turner == null)
            {
                return "";
            }
            int total = turner.pageContentIndex + 1;
            return total > 1 ? "Page " + (turner.pageIndex + 1) + " of " + total + "." : "";
        }

        /// <summary>The text to read aloud for the current document.</summary>
        private static string ReadAloudText(bool includeHeading)
        {
            List<string> parts = new List<string>();
            if (includeHeading)
            {
                parts.Add(SectionReader.Heading(SectionReader.Current));
            }
            if (PageMode)
            {
                parts.Add(PageLabel());
            }
            SectionContent c = doc.Content;
            if (ModSettings.AutoRead.Value || !includeHeading)
            {
                foreach (Block b in c.Blocks)
                {
                    parts.Add(SectionReader.BlockSpeech(b));
                }
                // Mid-book pages often have no choices; only say "No choices" when there's nothing else to do.
                if (c.Choices.Count > 0 || !PageMode)
                {
                    parts.Add(SectionReader.ChoicesSummary(c));
                }
            }
            else
            {
                parts.Add(SectionReader.CountsText(c));
            }
            return TextUtil.Join(parts, "\n");
        }

        public static void OnNewSection(BBSection section)
        {
            doc.Load(BuildContent(), false);
            Speech.SayEvent(ReadAloudText(true), 2.5f);
        }

        public static void OnPageChanged(PageTurner turner)
        {
            if (!PageMode)
            {
                string label = PageLabel();
                if (label.Length > 0)
                {
                    Speech.SayPriority(label, true, 0.8f);
                }
                return;
            }
            doc.Load(BuildContent(), false);
            Speech.SayPriority(ModSettings.AutoRead.Value ? ReadAloudText(false) : PageLabel() + " " + SectionReader.CountsText(doc.Content), true, 1.5f);
        }

        // ---------- Keys ----------

        /// <summary>Handle the reading keys. Returns true if a key was used.</summary>
        public static bool HandleKeys(bool ctrl, bool shift)
        {
            if (!Active)
            {
                return false;
            }
            if (doc.LineCount <= 1 && doc.Text.Length == 0)
            {
                Reload(false);
            }
            // The game's free-read helpers: go back, free choice, heal.
            if (!ctrl && Input.GetKeyDown(KeyCode.B))
            {
                FreeRead.GoBack();
                return true;
            }
            if (!ctrl && Input.GetKeyDown(KeyCode.F))
            {
                FreeRead.FreeChoice();
                return true;
            }
            if (!ctrl && Input.GetKeyDown(KeyCode.H))
            {
                FreeRead.Heal();
                return true;
            }
            if (PageMode && Input.GetKeyDown(KeyCode.PageDown))
            {
                TurnPage(1);
                return true;
            }
            if (PageMode && Input.GetKeyDown(KeyCode.PageUp))
            {
                TurnPage(-1);
                return true;
            }
            return doc.HandleKeys(ctrl, shift);
        }

        private static void TurnPage(int dir)
        {
            PageTurner turner = Turner;
            if (turner == null || IsTurning(turner))
            {
                return;
            }
            if (dir > 0 && !turner.CanTurnLeft())
            {
                Speech.Say("Last page.");
                return;
            }
            if (dir < 0 && turner.pageIndex == 0)
            {
                Speech.Say("First page.");
                return;
            }
            // In this game "turn left" flips forward (the page moves to the left) and "turn right" flips back.
            if (dir > 0) turner.TurnLeft(); else turner.TurnRight();
        }

        /// <summary>F2: read the whole section (or, in page layout, the current page) from the top.</summary>
        public static void ReadAll()
        {
            if (!SectionReader.InBook() || SectionReader.Current == null)
            {
                Speech.Say("No book section is open.");
                return;
            }
            Reload(false);
            Speech.SayPriority(SectionReader.Heading(SectionReader.Current) + "\n" + ReadAloudText(false), true, 2.5f);
        }

        // ---------- Picking a choice ----------

        public static void ActivateChoice(StoryLink link)
        {
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
            PageTurner turner = Turner;
            if (turner == null)
            {
                Speech.Say("Can't find the book page.");
                yield break;
            }
            int pageIdx = -1;
            StoryLinkAction button = null;
            for (int p = 0; p < turner.pages.Count && button == null; p++)
            {
                foreach (StoryLinkAction sla in Both(turner.pages[p]))
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
