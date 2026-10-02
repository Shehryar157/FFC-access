using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using Mercury;
using Tin;

namespace FFCAccess
{
    /// <summary>
    /// Reads book sections aloud. The text is rebuilt from the section's token list (the same data the game
    /// lays out), so it works the same in page mode and flow mode and isn't split up by page breaks.
    /// </summary>
    internal static class SectionReader
    {
        public static List<string> Paragraphs = new List<string>();
        public static List<StoryLink> Links = new List<StoryLink>();
        public static int ParagraphIndex = -1;

        private static BBSection lastSpoken;
        private static bool sectionChanged;

        public static void Patch(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(BBGameController), nameof(BBGameController.changeSection), new[] { typeof(int), typeof(bool) }),
                postfix: new HarmonyMethod(typeof(SectionReader), nameof(ChangeSectionPostfix)));
            harmony.Patch(AccessTools.Method(typeof(PageTurner), nameof(PageTurner.EndSection)),
                postfix: new HarmonyMethod(typeof(SectionReader), nameof(EndSectionPostfix)));
            harmony.Patch(AccessTools.Method(typeof(PageTurner), "ChangePage"),
                postfix: new HarmonyMethod(typeof(SectionReader), nameof(ChangePagePostfix)));
            harmony.Patch(AccessTools.Method(typeof(FlowTextChunk), nameof(FlowTextChunk.SetContent)),
                postfix: new HarmonyMethod(typeof(SectionReader), nameof(FlowTextPostfix)));
        }

        public static bool IsFlowStyle()
        {
            try
            {
                return Data.Get<string>("game style") == "flow";
            }
            catch
            {
                return false;
            }
        }

        private static void ChangeSectionPostfix()
        {
            sectionChanged = true;
        }

        private static void EndSectionPostfix(PageTurner __instance)
        {
            try
            {
                if (IsFlowStyle() || !__instance.isActiveAndEnabled)
                {
                    return;
                }
                BBSection section = BBGameController.instance?.section;
                if (section == null || (section == lastSpoken && !sectionChanged))
                {
                    return;
                }
                lastSpoken = section;
                sectionChanged = false;
                NavAnnouncer.Reset();
                ReadSection(section, true);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Section read failed: " + e);
            }
        }

        private static void ChangePagePostfix(PageTurner __instance)
        {
            try
            {
                if (IsFlowStyle())
                {
                    return;
                }
                int total = __instance.pageContentIndex + 1;
                if (total > 1)
                {
                    Speech.SayPriority("Page " + (__instance.pageIndex + 1) + " of " + total, true, 0.8f);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Page announce failed: " + e);
            }
        }

        /// <summary>In flow style the text appears a chunk at a time (pausing for rolls), so speak each chunk as it arrives.</summary>
        private static void FlowTextPostfix(object content)
        {
            if (!IsFlowStyle() || !(content is string s))
            {
                return;
            }
            Speech.Say(s, false);
        }

        /// <summary>Rebuild the section's text and choices, and optionally speak it.</summary>
        public static void ReadSection(BBSection section, bool speak)
        {
            Build(section);
            ParagraphIndex = -1;
            if (!speak)
            {
                return;
            }
            Speech.SayPriority(FullText(section), true, 2.5f);
        }

        public static string FullText(BBSection section)
        {
            StringBuilder sb = new StringBuilder();
            if (section != null && section.displayID > 0)
            {
                sb.Append("Section ").Append(section.displayID).Append(".\n");
            }
            foreach (string p in Paragraphs)
            {
                sb.Append(p).Append('\n');
            }
            string choices = ChoicesSummary();
            if (choices.Length > 0)
            {
                sb.Append(choices);
            }
            return sb.ToString();
        }

        public static string ChoicesSummary()
        {
            if (Links.Count == 0)
            {
                return "";
            }
            List<string> items = new List<string>();
            for (int i = 0; i < Links.Count; i++)
            {
                items.Add((i + 1) + ": " + DescribeLink(Links[i]));
            }
            return (Links.Count == 1 ? "1 choice. " : Links.Count + " choices. ") + string.Join(". ", items.ToArray());
        }

        private static void Build(BBSection section)
        {
            Paragraphs.Clear();
            Links.Clear();
            if (section == null || section.tokenList == null)
            {
                return;
            }
            bool filtered = false;
            StringBuilder para = new StringBuilder();
            Action endPara = () =>
            {
                string t = TextUtil.Clean(para.ToString());
                if (t.Length > 0)
                {
                    Paragraphs.Add(t);
                }
                para.Length = 0;
            };
            Character ch = BBGameController.instance?.character;
            foreach (object token in section.tokenList)
            {
                if (token is Hashtable tag)
                {
                    string type = tag["type"] as string;
                    if (type == "filter")
                    {
                        filtered = MercuryFilter.checkFilter(tag);
                        continue;
                    }
                    if (filtered)
                    {
                        continue;
                    }
                    switch (type)
                    {
                        case "clearline":
                        case "parabreak":
                        case "linebreak":
                        case "pagebreak":
                        case "forcePagebreak":
                            endPara();
                            break;
                        case "showItemName":
                        {
                            BBInventoryItem item = ch?.InventoryItem(tag["gameID"].ToString());
                            para.Append(item != null ? item.name : "").Append(' ');
                            break;
                        }
                        case "showItemQuantity":
                        {
                            BBInventoryItem item = ch?.InventoryItem(tag["gameID"].ToString());
                            para.Append(item != null ? item.quantity.ToString() : "0").Append(' ');
                            break;
                        }
                        case "showvalue":
                        {
                            BBInventoryItem item = ch?.InventoryItem(tag["value"].ToString());
                            para.Append(item != null ? item.quantity.ToString() : "0").Append(' ');
                            break;
                        }
                    }
                    continue;
                }
                if (filtered)
                {
                    continue;
                }
                if (token is string s)
                {
                    para.Append(s).Append(' ');
                }
                else if (token is StoryLink link)
                {
                    para.Append(LinkWords(link)).Append(' ');
                    Links.Add(link);
                }
            }
            endPara();
        }

        public static string LinkWords(StoryLink link)
        {
            if (link == null || link.linkWords == null)
            {
                return "";
            }
            List<string> words = new List<string>();
            foreach (object w in link.linkWords)
            {
                if (w != null)
                {
                    words.Add(w.ToString());
                }
            }
            return TextUtil.Clean(string.Join(" ", words.ToArray()));
        }

        /// <summary>Spoken description of a choice: its words, what kind of choice it is, and whether it's available.</summary>
        public static string DescribeLink(StoryLink link)
        {
            if (link == null)
            {
                return "";
            }
            List<string> parts = new List<string>();
            string words = LinkWords(link);
            parts.Add(words.Length > 0 ? words : "Continue");
            string kind = null;
            try
            {
                switch (link.ActiveLinkType())
                {
                    case LinkType.COMBAT: kind = "fight"; break;
                    case LinkType.ROLL: kind = "dice roll"; break;
                    case LinkType.STAT: kind = "stat test"; break;
                    case LinkType.SKILL: kind = "skill test"; break;
                    case LinkType.RIDDLE: kind = "riddle"; break;
                    case LinkType.TRADE: kind = "trade"; break;
                    case LinkType.ITEM: kind = "item"; break;
                    case LinkType.KEY: kind = "key"; break;
                }
            }
            catch
            {
            }
            if (kind == null && link is LuckRollLink)
            {
                kind = "luck test";
            }
            if (kind != null)
            {
                parts.Add(kind);
            }
            bool blocked = false;
            try
            {
                blocked = link.IsBlock();
            }
            catch
            {
            }
            if (blocked)
            {
                parts.Add("unavailable");
            }
            return string.Join(", ", parts.ToArray());
        }

        public static void RepeatSection()
        {
            BBSection section = BBGameController.instance?.section;
            if (section == null || !InBook())
            {
                Speech.Say("No book section is open.");
                return;
            }
            ReadSection(section, true);
        }

        public static void ReadChoices()
        {
            BBSection section = BBGameController.instance?.section;
            if (section == null || !InBook())
            {
                Speech.Say("No book section is open.");
                return;
            }
            Build(section);
            string c = ChoicesSummary();
            Speech.Say(c.Length > 0 ? c : "No choices in this section.");
        }

        /// <summary>Step through the section a paragraph at a time.</summary>
        public static void StepParagraph(int dir)
        {
            BBSection section = BBGameController.instance?.section;
            if (section == null || !InBook())
            {
                Speech.Say("No book section is open.");
                return;
            }
            if (Paragraphs.Count == 0)
            {
                Build(section);
            }
            if (Paragraphs.Count == 0)
            {
                Speech.Say("No text.");
                return;
            }
            ParagraphIndex = System.Math.Max(0, System.Math.Min(Paragraphs.Count - 1, ParagraphIndex + dir));
            Speech.Say(Paragraphs[ParagraphIndex]);
        }

        public static bool InBook()
        {
            return BBGameController.instance != null && BBGameController.instance.BookIsLoaded && SectionController.instance != null;
        }
    }
}
