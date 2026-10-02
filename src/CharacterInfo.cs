using System.Collections;
using System.Collections.Generic;
using System.Text;
using Mercury.Book;
using Tin;
using UnityEngine.UI;

namespace FFCAccess
{
    /// <summary>
    /// Stats and inventory, read straight from the character data. Each book has an InventoryLayout that says
    /// which stats it has (Skill, Stamina, Luck, Fear...) and how items are grouped on the Adventure Sheet.
    /// </summary>
    internal static class CharacterInfo
    {
        private static Character Player => BBGameController.instance?.character;

        private static InventoryLayout Layout()
        {
            BBGameController gc = BBGameController.instance;
            InventoryLayout layout = gc?.book?.bookData?._InventoryLayout;
            return layout != null ? layout : gc?.InvLayout;
        }

        /// <summary>The inventory key this book uses for Stamina (its layout marks which stat is the Stamina one).</summary>
        public static string StaminaKey()
        {
            InventoryLayout layout = Layout();
            if (layout != null && layout.Stats != null)
            {
                foreach (InventoryLayout.StatConfig s in layout.Stats)
                {
                    if (s != null && s.Type == InventoryLayout.StatType.Stamina && !string.IsNullOrEmpty(s.inventoryKey))
                    {
                        return s.inventoryKey;
                    }
                }
            }
            return "stamina";
        }

        /// <summary>Turn one of the game's text keys (like "stat_skill") into the words shown on screen.</summary>
        private static string Localize(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return "";
            }
            string text = null;
            try { text = Data.Get<string>(key); } catch { }
            if (string.IsNullOrEmpty(text))
            {
                try { text = ConfigManager.instance.stringForKey(key); } catch { }
            }
            if (string.IsNullOrEmpty(text) || text.StartsWith("cant find"))
            {
                text = TextUtil.Humanize(key);
            }
            return TextUtil.Clean(text);
        }

        private static bool Ready()
        {
            if (Player == null || Player.inventory == null || !SectionReader.InBook())
            {
                Speech.Say("No adventure is in progress.");
                return false;
            }
            return true;
        }

        /// <summary>S: a window listing each stat, like "Stamina 18 of 20".</summary>
        public static void ReadStats()
        {
            if (!Ready())
            {
                return;
            }
            Character c = Player;
            List<string> parts = new List<string>();
            InventoryLayout layout = Layout();
            List<InventoryLayout.StatConfig> stats = new List<InventoryLayout.StatConfig>();
            if (layout != null)
            {
                if (layout.Stats != null) stats.AddRange(layout.Stats);
                if (layout.AltSheetStats != null) stats.AddRange(layout.AltSheetStats);
            }
            foreach (InventoryLayout.StatConfig s in stats)
            {
                if (s == null || string.IsNullOrEmpty(s.inventoryKey))
                {
                    continue;
                }
                string name = Localize(s.inventoryConfig);
                if (name.Length == 0)
                {
                    BBInventoryItem item = c.InventoryItem(s.inventoryKey);
                    name = item != null && !string.IsNullOrEmpty(item.name) ? item.name : TextUtil.Humanize(s.inventoryKey);
                }
                string value = c.InventoryItemQuantity(s.inventoryKey).ToString();
                if (!string.IsNullOrWhiteSpace(s.maxKey))
                {
                    value += " of " + c.InventoryItemQuantity(s.maxKey);
                }
                parts.Add(name + " " + value);
            }
            if (parts.Count == 0)
            {
                // No layout: fall back to the classic three.
                foreach (string id in new[] { "skill", "stamina", "luck" })
                {
                    BBInventoryItem item = c.InventoryItem(id);
                    if (item != null)
                    {
                        parts.Add(TextUtil.Humanize(id) + " " + item.quantity);
                    }
                }
            }
            LogInventory(c);
            if (parts.Count == 0)
            {
                parts.Add("No stats found.");
            }
            TextWindow.Show("Stats", parts);
        }

        /// <summary>I (or the game's own Inventory key): a window of items, grouped like the Adventure Sheet. Ctrl+Down jumps between groups.</summary>
        public static void ReadInventory()
        {
            if (!Ready())
            {
                return;
            }
            Character c = Player;
            InventoryLayout layout = Layout();
            List<string> lines = new List<string>();
            List<int> paragraphs = new List<int>();
            List<BBInventoryItem> lineItems = new List<BBInventoryItem>();
            List<bool> lineUsable = new List<bool>();
            int group = 0;
            HashSet<string> seen = new HashSet<string>();
            if (layout != null && layout.InventoryGroups != null)
            {
                foreach (InventoryLayout.InventoryConfig g in layout.InventoryGroups)
                {
                    if (g == null || g.IsStat || g.categories == null || g.categories.Length == 0)
                    {
                        continue;
                    }
                    List<BBInventoryItem> items = new List<BBInventoryItem>();
                    foreach (BBInventoryItem item in c.allItemsOfTypes(g.categories))
                    {
                        if (item == null || item.quantity <= 0 || item.type == "hidden" || !seen.Add(item.gameID))
                        {
                            continue;
                        }
                        items.Add(item);
                    }
                    string title = Localize(g.configTitle);
                    if (items.Count == 0 && !(g.ShowWhenEmpty && title.Length > 0))
                    {
                        continue;
                    }
                    // A heading line for the group, then one line per item, all in one paragraph.
                    lines.Add((title.Length > 0 ? title : "Items") + ", " + (items.Count == 0 ? "empty" : items.Count == 1 ? "1 item" : items.Count + " items"));
                    paragraphs.Add(group);
                    lineItems.Add(null);
                    lineUsable.Add(false);
                    foreach (BBInventoryItem it in items)
                    {
                        // Groups marked "activatable" in the book's layout hold items you can use (Provisions, potions).
                        lines.Add(DescribeItem(it) + (g.ActivatableItems ? ", usable" : ""));
                        paragraphs.Add(group);
                        lineItems.Add(it);
                        lineUsable.Add(g.ActivatableItems);
                    }
                    group++;
                }
            }
            LogInventory(c);
            if (lines.Count == 0)
            {
                lines.Add("Your inventory is empty.");
                paragraphs.Add(0);
                lineItems.Add(null);
                lineUsable.Add(false);
            }
            InventoryBox box = new InventoryBox();
            box.Load(lines, paragraphs, lineItems, lineUsable);
            TextWindow.ShowBox("Inventory", box);
        }

        private static string DescribeItem(BBInventoryItem item)
        {
            StringBuilder sb = new StringBuilder(TextUtil.Clean(item.name));
            if (item.quantity > 1)
            {
                sb.Append(' ').Append(item.quantity);
            }
            if (item.equipped)
            {
                sb.Append(", equipped");
            }
            return sb.ToString();
        }

        /// <summary>F6: open the game's own Adventure Sheet, the same way its bottom-menu button does.</summary>
        public static void OpenAdventureSheet()
        {
            if (!SectionReader.InBook() || BBGameController.instance.navButtons == null)
            {
                Speech.Say("No adventure is in progress.");
                return;
            }
            foreach (Button b in BBGameController.instance.navButtons)
            {
                if (b != null && b.name == "AdventureSheet")
                {
                    if (!b.interactable)
                    {
                        Speech.Say("The Adventure Sheet isn't available right now.");
                        return;
                    }
                    b.onClick.Invoke();
                    Speech.SayPriority("Adventure Sheet", true, 1f);
                    return;
                }
            }
            Speech.Say("Couldn't find the Adventure Sheet button.");
        }

        /// <summary>Write the raw inventory to the log, so the mod's developer can learn each book's data.</summary>
        private static void LogInventory(Character c)
        {
            StringBuilder sb = new StringBuilder("Inventory dump:\n");
            foreach (DictionaryEntry e in c.inventory)
            {
                if (e.Value is BBInventoryItem it)
                {
                    sb.Append("  ").Append(it.gameID).Append(" type=").Append(it.type).Append(" name=").Append(it.name)
                      .Append(" qty=").Append(it.quantity).Append(" bonus=").Append(it.bonusQuantity)
                      .Append(" equipped=").Append(it.equipped).Append('\n');
                }
            }
            Plugin.Log.LogInfo(sb.ToString());
        }
    }
}
