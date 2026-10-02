using System.Collections.Generic;
using Tin;

namespace FFCAccess
{
    /// <summary>
    /// The inventory window: a read-only TextBox that remembers which item is on each line.
    /// Enter on a usable item (Provisions, potions...) uses it the same way the game's Adventure Sheet does.
    /// </summary>
    internal class InventoryBox : TextBox
    {
        private readonly List<BBInventoryItem> itemOfLine = new List<BBInventoryItem>();
        private readonly List<bool> usableOfLine = new List<bool>();

        public void Load(List<string> lines, List<int> paragraphs, List<BBInventoryItem> items, List<bool> usable)
        {
            itemOfLine.Clear();
            itemOfLine.AddRange(items);
            usableOfLine.Clear();
            usableOfLine.AddRange(usable);
            SetLines(lines, paragraphs);
        }

        protected override void OnEnter()
        {
            int line = CurrentLine;
            BBInventoryItem item = line < itemOfLine.Count ? itemOfLine[line] : null;
            if (item == null)
            {
                Say("Not an item.");
                return;
            }
            if (!usableOfLine[line])
            {
                Say(TextUtil.Clean(item.name) + " can't be used from the inventory.");
                return;
            }
            bool sheetEnabled = true;
            try { sheetEnabled = Data.Get("enable_adventuresheet", _default: true); } catch { }
            if (!sheetEnabled)
            {
                Say("You can't use items right now.");
                return;
            }
            // The game's confirmation popup needs the keyboard, so step out of our window first.
            TextWindow.Close(false);
            ItemUse.Ask(item);
        }
    }

    /// <summary>Mirrors AdventureSheetInventoryObject.OnClick/UseItem from the game, which only exist while the sheet is built.</summary>
    internal static class ItemUse
    {
        public static void Ask(BBInventoryItem item)
        {
            if (item.tags != null && item.tags.Contains("no_popup"))
            {
                Use(item);
                return;
            }
            if (item.type == "healing" && ConfigManager.Get<bool>("ff player disable healing items"))
            {
                PopupPanel.instance.SetPopupContents(ConfigManager.Get<string>("cannot use item").Replace("<insert>", item.name));
                PopupPanel.instance.buttonCount = 1;
                PopupPanel.instance.SetButtonLabel(0, ConfigManager.instance.stringForKey("popup_ok"));
                PopupPanel.instance.SetButtonAction(0, null);
                PopupPanel.instance.ShowPopup();
                return;
            }
            PopupPanel.instance.SetPopupContents(ConfigManager.Get<string>("use item").Replace("<insert>", item.name));
            PopupPanel.instance.buttonCount = 2;
            PopupPanel.instance.SetButtonLabel(0, ConfigManager.instance.stringForKey("use item yes"));
            PopupPanel.instance.SetButtonAction(0, () => Use(item));
            PopupPanel.instance.SetButtonLabel(1, ConfigManager.instance.stringForKey("use item no"));
            PopupPanel.instance.SetButtonAction(1, null);
            PopupPanel.instance.ShowPopup();
        }

        private static void Use(BBInventoryItem item)
        {
            Character c = BBGameController.instance.character;
            string staminaKey = CharacterInfo.StaminaKey();
            int before = c.InventoryItemQuantity(staminaKey);
            if (item.action != null)
            {
                foreach (object a in item.action)
                {
                    if (MercuryMouseActions.doChunk(a as System.Collections.Hashtable))
                    {
                        c.dropItemWithID(item.gameID, 1);
                    }
                }
            }
            else
            {
                c.useItem(item);
            }
            BBNotificationServer.instance.postNotification("BBItemWasUsedNotification");
            int after = c.InventoryItemQuantity(staminaKey);
            string result = "Used " + TextUtil.Clean(item.name) + ".";
            if (after != before)
            {
                result += " Stamina " + after + ".";
            }
            Speech.SayEvent(result);
        }
    }
}
