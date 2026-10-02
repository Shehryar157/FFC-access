using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace FFCAccess
{
    /// <summary>
    /// Every game screen has a private "RefreshNavSelection" method that moves its highlight box onto the
    /// item chosen with keyboard or gamepad. We patch all of them and speak whatever ended up highlighted.
    /// </summary>
    internal static class NavAnnouncer
    {
        private static object lastScreen;
        private static string lastKey;

        public static void PatchAll(Harmony harmony)
        {
            MethodInfo postfixResult = AccessTools.Method(typeof(NavAnnouncer), nameof(PostfixWithResult));
            MethodInfo postfixVoid = AccessTools.Method(typeof(NavAnnouncer), nameof(PostfixVoid));
            foreach (Type t in AccessTools.GetTypesFromAssembly(typeof(BBGameController).Assembly))
            {
                MethodInfo m;
                try
                {
                    m = t.GetMethod("RefreshNavSelection", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                }
                catch
                {
                    continue;
                }
                if (m == null || m.IsAbstract)
                {
                    continue;
                }
                try
                {
                    harmony.Patch(m, postfix: new HarmonyMethod(m.ReturnType == typeof(void) ? postfixVoid : postfixResult));
                    Plugin.Log.LogInfo("Focus hook: " + t.Name + ".RefreshNavSelection -> " + m.ReturnType.Name);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError("Could not patch " + t.Name + ".RefreshNavSelection: " + e.Message);
                }
            }
        }

        /// <summary>Called every frame: once a screen's highlight disappears, forget it so re-entering re-announces.</summary>
        public static void Tick()
        {
            if (lastScreen == null)
            {
                return;
            }
            RectTransform sel = SelectionOf(lastScreen);
            if (sel == null || !sel.gameObject.activeInHierarchy)
            {
                lastScreen = null;
                lastKey = null;
            }
        }

        /// <summary>Record an announcement made elsewhere for this screen, so it isn't repeated on the next refresh.</summary>
        public static void Remember(object screen, GameObject target, string text)
        {
            lastScreen = screen;
            lastKey = (target != null ? target.GetInstanceID().ToString() : "-") + "|" + text;
        }

        public static void Reset()
        {
            lastScreen = null;
            lastKey = null;
        }

        private static RectTransform SelectionOf(object screen)
        {
            if (screen is UnityEngine.Object uo && uo == null)
            {
                return null;
            }
            try
            {
                return Traverse.Create(screen).Field("selection").GetValue<RectTransform>();
            }
            catch
            {
                return null;
            }
        }

        private static void PostfixWithResult(object __instance, object __result)
        {
            try
            {
                Handle(__instance, __result);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Focus announce failed on " + __instance?.GetType().Name + ": " + e);
            }
        }

        private static void PostfixVoid(object __instance)
        {
            try
            {
                Handle(__instance, null);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Focus announce failed on " + __instance?.GetType().Name + ": " + e);
            }
        }

        private static void Handle(object screen, object result)
        {
            RectTransform sel = SelectionOf(screen);
            if (sel != null && !sel.gameObject.activeSelf)
            {
                return;
            }
            GameObject target;
            string text = Describe(screen, result, out target);
            if (string.IsNullOrEmpty(text))
            {
                return;
            }
            string key = (target != null ? target.GetInstanceID().ToString() : "-") + "|" + text;
            if (screen == lastScreen && key == lastKey)
            {
                return;
            }
            lastScreen = screen;
            lastKey = key;
            Speech.SayFocus(text);
        }

        private static string Describe(object screen, object result, out GameObject target)
        {
            target = null;
            switch (screen)
            {
                case PageTurner pt:
                    return DescribePageTurner(pt, out target);
                case CombatButtonsMenu cbm:
                    return DescribeCombatMenu(cbm, out target);
                case GalleryMenu gm:
                    return DescribeGallery(gm, result as Component, out target);
                case BookButtonCreator _:
                    target = (result as Component)?.gameObject;
                    return DescribeBook(target);
                case OptionsMenu om:
                    return DescribeOptions(om, result, out target);
                case PopupPanel pp when result == null:
                    // Focus is on the popup's text field rather than a button.
                    if (pp.inputField != null && pp.inputField.gameObject.activeInHierarchy)
                    {
                        target = pp.inputField.gameObject;
                        string value = pp.inputField.text;
                        return TextEntry.LabelFor(pp.inputField) + ", edit, " + (string.IsNullOrEmpty(value) ? "blank" : value);
                    }
                    return null;
            }
            if (result is OptionsNav.OptionsItem item)
            {
                return DescribeOptionItem(item, out target);
            }
            if (result is Component comp)
            {
                target = comp.gameObject;
                return DescribeControl(target);
            }
            return null;
        }

        /// <summary>The picture key of the gallery picture that has focus, for D. Null when none.</summary>
        public static string GalleryPicture;

        private static string DescribeGallery(GalleryMenu gm, Component result, out GameObject target)
        {
            target = result != null ? result.gameObject : null;
            GalleryPicture = null;
            if (target == null)
            {
                return null;
            }
            List<GameObject> images = Traverse.Create(gm).Field("imageobjects").GetValue<List<GameObject>>();
            int index = images != null ? images.IndexOf(target) : -1;
            if (index < 0)
            {
                return DescribeControl(target); // the close or download button
            }
            string position = ", " + (index + 1) + " of " + images.Count;
            RawImage raw = target.GetComponent<RawImage>();
            if (raw == null || raw.texture == null)
            {
                return "Locked picture" + position;
            }
            GalleryPicture = raw.texture.name;
            Descriptions.Entry e = Descriptions.Find(GalleryPicture);
            string text = e != null && !string.IsNullOrEmpty(e.Short) ? e.Short : "no description yet";
            return "Picture" + position + ": " + text;
        }

        /// <summary>A generic button or control: its text plus disabled state.</summary>
        public static string DescribeControl(GameObject go)
        {
            if (go == null)
            {
                return null;
            }
            string label = TextUtil.LabelFor(go);
            Button b = go.GetComponent<Button>();
            if (b != null && !b.interactable)
            {
                label += ", unavailable";
            }
            return label;
        }

        private static string DescribePageTurner(PageTurner pt, out GameObject target)
        {
            target = null;
            int navIndex = Traverse.Create(pt).Field("navIndex").GetValue<int>();
            Page page = pt.ThisPage;
            if (navIndex < 0 && page.raw_image != null && page.raw_image.gameObject.activeInHierarchy)
            {
                target = page.raw_image.gameObject;
                return "Illustration";
            }
            List<StoryLinkAction> list = new List<StoryLinkAction>();
            list.AddRange(page.leftButtons);
            list.AddRange(page.rightButtons);
            if (navIndex < 0 || navIndex >= list.Count)
            {
                return null;
            }
            StoryLinkAction sla = list[navIndex];
            target = sla.gameObject;
            return SectionReader.DescribeLink(sla.storyLink) + ", " + (navIndex + 1) + " of " + list.Count;
        }

        private static string DescribeCombatMenu(CombatButtonsMenu cbm, out GameObject target)
        {
            target = null;
            int navIndex = Traverse.Create(cbm).Field("navIndex").GetValue<int>();
            List<GameObject> list = new List<GameObject>();
            foreach (Transform item in cbm.buttonsContainer)
            {
                if (item.gameObject.activeSelf)
                {
                    list.Add(item.gameObject);
                }
            }
            if (navIndex < 0 || navIndex >= list.Count)
            {
                return null;
            }
            target = list[navIndex];
            return DescribeControl(target) + ", " + (navIndex + 1) + " of " + list.Count;
        }

        private static int lastShelfRow = -1;

        public static string DescribeBook(GameObject shelfSlot)
        {
            if (shelfSlot == null)
            {
                return null;
            }
            OpenBookButton obb = shelfSlot.GetComponentInChildren<OpenBookButton>();
            if (obb == null || obb.bookData == null)
            {
                return DescribeControl(shelfSlot);
            }
            List<string> parts = new List<string>();
            MercuryCloudBook data = obb.bookData;
            string title = data.Title;
            if (data.FFClassicsNumber > 0)
            {
                title = data.FFClassicsNumber + ". " + title;
            }
            parts.Add(title);
            if (!data.IsPurchased())
            {
                parts.Add("not owned");
            }
            if (obb.bookmark != null && obb.bookmark.gameObject.activeSelf)
            {
                parts.Add("adventure in progress");
            }
            string text = string.Join(", ", parts.ToArray());
            int row, rows, col, cols;
            if (ShelfNav.Position(out row, out rows, out col, out cols))
            {
                text += ", " + (col + 1) + " of " + cols;
                // Say the row only when it changes, so moving along a row stays short.
                if (row != lastShelfRow)
                {
                    text = "Row " + (row + 1) + " of " + rows + ". " + text;
                    lastShelfRow = row;
                }
            }
            return text;
        }

        private static string DescribeOptions(OptionsMenu om, object result, out GameObject target)
        {
            target = null;
            int navIndex = Traverse.Create(om).Field("navIndex").GetValue<int>();
            if (navIndex < 0)
            {
                // Focus is on the row of tabs at the top.
                int tabIndex = Traverse.Create(om).Field("navTabIndex").GetValue<int>();
                List<string> tabs = om.ActiveTabs();
                Dictionary<string, GameObject> buttons = Traverse.Create(om).Field("windowbuttons").GetValue<Dictionary<string, GameObject>>();
                if (tabs == null || tabIndex < 0 || tabIndex >= tabs.Count)
                {
                    return null;
                }
                GameObject go;
                buttons.TryGetValue(tabs[tabIndex], out go);
                target = go;
                string label = go != null ? TextUtil.LabelFor(go) : TextUtil.Humanize(tabs[tabIndex]);
                return label + " tab, " + (tabIndex + 1) + " of " + tabs.Count;
            }
            if (result is OptionsNav.OptionsItem item)
            {
                return DescribeOptionItem(item, out target);
            }
            return null;
        }

        public static string DescribeOptionItem(OptionsNav.OptionsItem item, out GameObject target)
        {
            target = null;
            if (item.slider != null)
            {
                target = item.slider.transform.parent.gameObject;
                string label = RowLabel(target, item.slider.gameObject);
                return label + ", slider, " + Mathf.RoundToInt(item.slider.normalizedValue * 100f) + " percent";
            }
            if (item.toggle != null)
            {
                target = item.toggle.gameObject;
                string label = TextUtil.LabelFor(target);
                if (TextUtil.TextsUnder(target).Count == 0 && target.transform.parent != null)
                {
                    label = RowLabel(target.transform.parent.gameObject, null);
                }
                return label + ", " + (item.toggle.toggle ? "on" : "off");
            }
            if (item.inputField != null)
            {
                target = item.inputField.transform.parent.gameObject;
                string label = RowLabel(target, item.inputField.gameObject);
                string value = item.inputField.text;
                return label + ", edit, " + (string.IsNullOrEmpty(value) ? "blank" : value);
            }
            if (item.buttons != null && item.buttons.Count > 0)
            {
                if (item.buttons.Count == 1)
                {
                    target = item.buttons[0].gameObject;
                    return DescribeControl(target);
                }
                target = item.buttons[0].transform.parent.gameObject;
                // Rows like "Mood" or "Library sorting" have arrow buttons either side of the value; the row's
                // own text already says the value, so only mention buttons that have real labels.
                List<string> labels = new List<string>();
                List<string> arrows = new List<string> { "left", "right", "<", ">", "previous", "next" };
                foreach (Button b in item.buttons)
                {
                    if (b != null && b.gameObject.activeInHierarchy)
                    {
                        string l = TextUtil.LabelFor(b.gameObject);
                        if (!arrows.Contains(l.ToLowerInvariant()))
                        {
                            labels.Add(l);
                        }
                    }
                }
                string row = RoundLongNumbers(RowLabel(target, null));
                return labels.Count == 0 ? row : TextUtil.Join(new[] { row, "buttons: " + string.Join(", ", labels.ToArray()) }, ", ");
            }
            return TextUtil.Humanize(item.id);
        }

        /// <summary>"60.0562228469205Hz" reads badly; round numbers with long decimals.</summary>
        private static string RoundLongNumbers(string s)
        {
            return System.Text.RegularExpressions.Regex.Replace(s, @"\d+\.\d{3,}", m =>
                System.Math.Round(double.Parse(m.Value, System.Globalization.CultureInfo.InvariantCulture)).ToString());
        }

        /// <summary>Text of a settings row, skipping the text inside the control itself.</summary>
        private static string RowLabel(GameObject row, GameObject exclude)
        {
            List<string> texts = TextUtil.TextsUnder(row);
            if (exclude != null)
            {
                foreach (string t in TextUtil.TextsUnder(exclude))
                {
                    texts.Remove(t);
                }
            }
            return texts.Count > 0 ? string.Join(", ", texts.ToArray()) : TextUtil.Humanize(row.name);
        }
    }
}
