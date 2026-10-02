using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;
using Mercury;
using Tin;
using Math = System.Math;

namespace FFCAccess
{
    /// <summary>One choice in a section: the game's link object plus the words leading up to it.</summary>
    internal class Choice
    {
        public StoryLink Link;
        public string Context;
    }

    internal enum BlockKind
    {
        Text,
        PageBreak,
        Image
    }

    /// <summary>A paragraph of text, an authored page break, or an illustration.</summary>
    internal class Block
    {
        public BlockKind Kind;
        public string Text;
        public string ImageKey;
    }

    /// <summary>The readable content of a section (or of one page of it).</summary>
    internal class SectionContent
    {
        public List<Block> Blocks = new List<Block>();
        public List<Choice> Choices = new List<Choice>();
    }

    /// <summary>
    /// Turns a book section into readable content, and hooks the game so we know when sections and pages change.
    /// The text is rebuilt from the section's token list (the same data the game lays out), so it doesn't depend
    /// on how the game happened to split it across pages.
    /// </summary>
    internal static class SectionReader
    {
        private static BBSection lastSpoken;
        private static bool sectionChanged;

        // The end of a sentence: . ! ? or … possibly followed by closing quotes or brackets, then a space.
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
                BookReader.OnNewSection(section);
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
                BookReader.OnPageChanged(__instance);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Page change failed: " + e);
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

        public static string CountsText(SectionContent c)
        {
            if (c.Choices.Count == 0)
            {
                return "No choices.";
            }
            return c.Choices.Count == 1 ? "1 choice." : c.Choices.Count + " choices.";
        }

        /// <summary>Everything as one block of speech: text, page breaks, illustrations, then the choices.</summary>
        public static string FullText(SectionContent c)
        {
            StringBuilder sb = new StringBuilder();
            foreach (Block b in c.Blocks)
            {
                sb.Append(BlockSpeech(b)).Append('\n');
            }
            sb.Append(ChoicesSummary(c));
            return sb.ToString();
        }

        public static string BlockSpeech(Block b)
        {
            switch (b.Kind)
            {
                case BlockKind.PageBreak: return "Page break.";
                case BlockKind.Image: return Descriptions.Line(b.ImageKey);
                default: return b.Text;
            }
        }

        public static string ChoicesSummary(SectionContent c)
        {
            if (c.Choices.Count == 0)
            {
                return "No choices.";
            }
            List<string> items = new List<string>();
            for (int i = 0; i < c.Choices.Count; i++)
            {
                items.Add(DescribeChoice(c, i));
            }
            return CountsText(c) + " " + string.Join(". ", items.ToArray());
        }

        /// <summary>
        /// Build the readable content for tokens [from, to) of a section. Filters (conditional text) are tracked from
        /// the very first token, so a page that starts inside a hidden stretch is handled correctly.
        /// </summary>
        public static SectionContent Build(BBSection section, int from = 0, int to = int.MaxValue, bool pageBreaks = true)
        {
            SectionContent result = new SectionContent();
            if (section == null || section.tokenList == null)
            {
                return result;
            }
            bool filtered = false;
            StringBuilder para = new StringBuilder();
            // Where the last choice in this paragraph ended, so the next choice's context starts after it.
            int lastLinkEnd = 0;
            Action endPara = () =>
            {
                string t = TextUtil.Clean(para.ToString());
                if (t.Length > 0)
                {
                    result.Blocks.Add(new Block { Kind = BlockKind.Text, Text = t });
                }
                para.Length = 0;
                lastLinkEnd = 0;
            };
            Character ch = BBGameController.instance?.character;
            int count = Math.Min(section.tokenList.Count, to);
            for (int i = 0; i < count; i++)
            {
                object token = section.tokenList[i];
                bool inRange = i >= from;
                if (token is Hashtable tag)
                {
                    string type = tag["type"] as string;
                    if (type == "filter")
                    {
                        // A filter hides what follows unless its condition is met. Checking it changes nothing in the game.
                        filtered = MercuryFilter.checkFilter(tag);
                        continue;
                    }
                    if (filtered || !inRange)
                    {
                        continue;
                    }
                    switch (type)
                    {
                        case "clearline":
                        case "parabreak":
                        case "linebreak":
                            endPara();
                            break;
                        case "pagebreak":
                        case "forcePagebreak":
                            endPara();
                            if (pageBreaks)
                            {
                                result.Blocks.Add(new Block { Kind = BlockKind.PageBreak });
                            }
                            break;
                        case "image":
                            endPara();
                            result.Blocks.Add(new Block { Kind = BlockKind.Image, ImageKey = tag["value"] as string });
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
                if (filtered || !inRange)
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
                    string context = TextUtil.Clean(before.Substring(Math.Min(start, before.Length)) + " " + words);
                    context = context.TrimStart(',', ';', ':', ' ', '-');
                    para.Append(words).Append(' ');
                    lastLinkEnd = para.Length;
                    result.Choices.Add(new Choice { Link = link, Context = context.Length > 0 ? context : words });
                }
            }
            endPara();
            return result;
        }

        /// <summary>Split a paragraph into sentences, the reader's "lines".</summary>
        public static List<string> Sentences(string paragraph)
        {
            List<string> result = new List<string>();
            int start = 0;
            foreach (Match m in SentenceEnd.Matches(paragraph + " "))
            {
                int end = Math.Min(m.Index + m.Length, paragraph.Length);
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
        public static string DescribeChoice(SectionContent c, int index)
        {
            Choice ch = c.Choices[index];
            return "Choice " + (index + 1) + " of " + c.Choices.Count + ": " + ch.Context + LinkSuffix(ch.Link);
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
    }
}
