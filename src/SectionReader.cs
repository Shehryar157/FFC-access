using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;
using Mercury;
using Tin;

namespace FFCAccess
{
    /// <summary>One choice in a section: the game's link object plus the words leading up to it.</summary>
    internal class Choice
    {
        public StoryLink Link;
        public string Context;
    }

    /// <summary>
    /// Builds a readable version of each book section and speaks it when it opens. The text is rebuilt from
    /// the section's token list (the same data the game lays out), so it works the same in page mode and flow
    /// mode and isn't split up by page breaks.
    /// </summary>
    internal static class SectionReader
    {
        public static List<string> Paragraphs = new List<string>();
        public static List<Choice> Choices = new List<Choice>();

        /// <summary>Raised whenever the section has been rebuilt, so the reader can reset its cursor.</summary>
        public static event Action Rebuilt;

        private static BBSection lastSpoken;
        private static bool sectionChanged;

        // The end of a sentence: . ! ? or … possibly followed by closing quotes or brackets.
        private static readonly Regex SentenceEnd = new Regex("[.!?…][\"'’”)\\]]*\\s", RegexOptions.Compiled);

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

        public static bool InBook()
        {
            return BBGameController.instance != null && BBGameController.instance.BookIsLoaded && SectionController.instance != null;
        }

        public static BBSection Current => BBGameController.instance?.section;

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
                BBSection section = Current;
                if (section == null || (section == lastSpoken && !sectionChanged))
                {
                    return;
                }
                lastSpoken = section;
                sectionChanged = false;
                NavAnnouncer.Reset();
                Build(section);
                if (Plugin.AutoRead)
                {
                    Speech.SayPriority(FullText(section), true, 2.5f);
                }
                else
                {
                    Speech.SayPriority(Heading(section) + " " + CountsText(), true, 1f);
                }
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
                if (IsFlowStyle() || BookReader.AutoTurning)
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
            if (IsFlowStyle() && content is string s)
            {
                Speech.Say(s, false);
            }
        }

        public static string Heading(BBSection section)
        {
            return section != null && section.displayID > 0 ? "Section " + section.displayID + "." : "";
        }

        public static string CountsText()
        {
            if (Choices.Count == 0)
            {
                return "No choices.";
            }
            return Choices.Count == 1 ? "1 choice." : Choices.Count + " choices.";
        }

        /// <summary>The whole section as one block of speech: heading, text, then the choices.</summary>
        public static string FullText(BBSection section)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(Heading(section)).Append('\n');
            foreach (string p in Paragraphs)
            {
                sb.Append(p).Append('\n');
            }
            sb.Append(ChoicesSummary());
            return sb.ToString();
        }

        public static string ChoicesSummary()
        {
            if (Choices.Count == 0)
            {
                return "No choices.";
            }
            List<string> items = new List<string>();
            for (int i = 0; i < Choices.Count; i++)
            {
                items.Add(DescribeChoice(i));
            }
            return CountsText() + " " + string.Join(". ", items.ToArray());
        }

        /// <summary>Rebuild the section's paragraphs and choices from its tokens.</summary>
        public static void Build(BBSection section)
        {
            Paragraphs.Clear();
            Choices.Clear();
            if (section != null && section.tokenList != null)
            {
                BuildFrom(section);
            }
            Rebuilt?.Invoke();
        }

        private static void BuildFrom(BBSection section)
        {
            bool filtered = false;
            StringBuilder para = new StringBuilder();
            // Where the last choice in this paragraph ended, so the next choice's context starts after it.
            int lastLinkEnd = 0;
            Action endPara = () =>
            {
                string t = TextUtil.Clean(para.ToString());
                if (t.Length > 0)
                {
                    Paragraphs.Add(t);
                }
                para.Length = 0;
                lastLinkEnd = 0;
            };
            Character ch = BBGameController.instance?.character;
            foreach (object token in section.tokenList)
            {
                if (token is Hashtable tag)
                {
                    string type = tag["type"] as string;
                    if (type == "filter")
                    {
                        // A filter tag hides the text after it unless its condition is met; this only evaluates, it changes nothing.
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
                    string before = para.ToString();
                    int start = lastLinkEnd;
                    foreach (Match m in SentenceEnd.Matches(before))
                    {
                        if (m.Index + m.Length > start)
                        {
                            start = m.Index + m.Length;
                        }
                    }
                    string words = LinkWords(link);
                    string context = TextUtil.Clean(before.Substring(System.Math.Min(start, before.Length)) + " " + words);
                    context = context.TrimStart(',', ';', ':', ' ', '-');
                    para.Append(words).Append(' ');
                    lastLinkEnd = para.Length;
                    Choices.Add(new Choice { Link = link, Context = context.Length > 0 ? context : words });
                }
            }
            endPara();
        }

        /// <summary>Split a paragraph into sentences, the reader's "lines".</summary>
        public static List<string> Sentences(string paragraph)
        {
            List<string> result = new List<string>();
            int start = 0;
            foreach (Match m in SentenceEnd.Matches(paragraph + " "))
            {
                int end = System.Math.Min(m.Index + m.Length, paragraph.Length);
                string s = paragraph.Substring(start, end - start).Trim();
                if (s.Length > 0)
                {
                    result.Add(s);
                }
                start = end;
            }
            if (start < paragraph.Length)
            {
                string rest = paragraph.Substring(start).Trim();
                if (rest.Length > 0)
                {
                    result.Add(rest);
                }
            }
            return result;
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

        /// <summary>"Choice 2 of 3: if you go left, turn to 12, fight, unavailable". Availability is checked live.</summary>
        public static string DescribeChoice(int index)
        {
            Choice c = Choices[index];
            return "Choice " + (index + 1) + " of " + Choices.Count + ": " + c.Context + LinkSuffix(c.Link);
        }

        /// <summary>A choice's words plus its kind, used when the game's own highlight lands on it.</summary>
        public static string DescribeLink(StoryLink link)
        {
            if (link == null)
            {
                return "";
            }
            string words = LinkWords(link);
            return (words.Length > 0 ? words : "Continue") + LinkSuffix(link);
        }

        private static string LinkSuffix(StoryLink link)
        {
            StringBuilder sb = new StringBuilder();
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
                sb.Append(", ").Append(kind);
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
                sb.Append(", unavailable");
            }
            return sb.ToString();
        }

        /// <summary>F2: read the whole section again from the top.</summary>
        public static void RepeatSection()
        {
            BBSection section = Current;
            if (section == null || !InBook())
            {
                Speech.Say("No book section is open.");
                return;
            }
            Build(section);
            Speech.SayPriority(FullText(section), true, 2.5f);
        }

        /// <summary>F3: list the choices.</summary>
        public static void ReadChoices()
        {
            BBSection section = Current;
            if (section == null || !InBook())
            {
                Speech.Say("No book section is open.");
                return;
            }
            if (Choices.Count == 0 && Paragraphs.Count == 0)
            {
                Build(section);
            }
            Speech.Say(ChoicesSummary());
        }
    }
}
