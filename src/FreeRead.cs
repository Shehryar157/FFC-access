using System.Collections;
using Tin;
using UnityEngine;

namespace FFCAccess
{
    /// <summary>
    /// The game's "read it like a normal book" helpers, which normally sit on screen as buttons:
    /// Go Back (previous section), Free Choice (unlock every choice in this section) and Heal Me (restore Stamina).
    /// We press the game's own buttons, so its rules and confirmation popups still apply.
    /// The game switches these off at times (for example during combat) through Data flags, which we respect.
    /// </summary>
    internal static class FreeRead
    {
        /// <summary>The copy of a button component that lives in the loaded scene (not the prefab template).</summary>
        private static T FindInScene<T>() where T : MonoBehaviour
        {
            T fallback = null;
            foreach (T t in Resources.FindObjectsOfTypeAll<T>())
            {
                if (t == null || !t.gameObject.scene.IsValid())
                {
                    continue;
                }
                if (t.gameObject.activeInHierarchy)
                {
                    return t;
                }
                fallback = t;
            }
            return fallback;
        }

        private static bool Allowed(string flag)
        {
            try
            {
                return Data.Get(flag, _default: true);
            }
            catch
            {
                return true;
            }
        }

        public static void GoBack()
        {
            if (!Allowed("freeread_back"))
            {
                Speech.Say("Going back isn't available right now.");
                return;
            }
            GoBackButton b = FindInScene<GoBackButton>();
            if (b == null)
            {
                Speech.Say("Couldn't find the go back button.");
                return;
            }
            b.OnClick();
        }

        public static void FreeChoice()
        {
            if (!Allowed("freeread_choice"))
            {
                Speech.Say("Free choice isn't available right now.");
                return;
            }
            FreeChoiceButton b = FindInScene<FreeChoiceButton>();
            if (b != null)
            {
                b.OnClick();
                return;
            }
            // No button in this scene: do what the button does. Unlock every choice for this section and redraw it.
            Hashtable entries = new Hashtable();
            entries["freeReadModeActive"] = "YES";
            ConfigManager.instance.addTempConfigEntries(entries);
            BBMasterLayout.instance.setSectionNeedsReflow();
            Speech.Say("All choices unlocked for this section.");
        }

        public static void Heal()
        {
            if (!Allowed("freeread_heal"))
            {
                Speech.Say("Healing isn't available right now.");
                return;
            }
            HealMeButton b = FindInScene<HealMeButton>();
            if (b == null)
            {
                Speech.Say("Couldn't find the heal button.");
                return;
            }
            b.OnClick();
        }
    }
}
