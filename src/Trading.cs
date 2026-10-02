using HarmonyLib;
using UnityEngine;

namespace FFCAccess
{
    /// <summary>
    /// Trade and bet popups have plus/minus buttons for the amounts that only work with a mouse.
    /// While one is open: Left/Right change the first amount, Shift+Left/Right the second. T reads both.
    /// </summary>
    internal static class Trading
    {
        private static TradePopup ActivePopup()
        {
            PopupPanel p = PopupPanel.instance;
            if (p == null || !PopupPanel.visible)
            {
                return null;
            }
            if (p.Trader != null && p.Trader.gameObject.activeInHierarchy) return p.Trader;
            if (p.Better != null && p.Better.gameObject.activeInHierarchy) return p.Better;
            return null;
        }

        public static bool HandleKeys(bool shift)
        {
            TradePopup t = ActivePopup();
            if (t == null)
            {
                return false;
            }
            int dir = 0;
            if (KeyRepeat.Pressed(KeyCode.RightArrow)) dir = 1;
            else if (KeyRepeat.Pressed(KeyCode.LeftArrow)) dir = -1;
            if (dir != 0)
            {
                bool second = shift;
                // If only one side exists, both key sets change it.
                if (second && !t.TradeOutObject.activeSelf) second = false;
                if (!second && !t.TradeInObject.activeSelf) second = true;
                if (second) t.ChangeTradeOut(dir); else t.ChangeTradeIn(dir);
                Speech.Say(Side(t, second));
                return true;
            }
            if (Input.GetKeyDown(KeyCode.T))
            {
                Speech.Say(Describe(t));
                return true;
            }
            return false;
        }

        private static string Side(TradePopup t, bool second)
        {
            Traverse tr = Traverse.Create(t);
            BBInventoryItem item = tr.Field(second ? "TradeOut" : "TradeIn").GetValue<BBInventoryItem>();
            int value = tr.Field(second ? "current_TradeOut" : "current_TradeIn").GetValue<int>();
            if (item == null) return "";
            return TextUtil.Clean(item.name) + ", " + value + " of " + item.quantity;
        }

        public static string Describe(TradePopup t)
        {
            string a = t.TradeInObject.activeSelf ? Side(t, false) : "";
            string b = t.TradeOutObject.activeSelf ? Side(t, true) : "";
            return TextUtil.Join(new[] { a, b }, ". ");
        }
    }
}
