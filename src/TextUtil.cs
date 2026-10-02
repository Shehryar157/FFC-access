using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FFCAccess
{
    internal static class TextUtil
    {
        private static readonly Regex Tags = new Regex("<[^>]*>", RegexOptions.Compiled);
        private static readonly Regex Spaces = new Regex("[ \\t\\u00A0]+", RegexOptions.Compiled);
        private static readonly Regex Lines = new Regex("\\s*\\n\\s*", RegexOptions.Compiled);
        private static readonly Regex CamelSplit = new Regex("(?<=[a-z])(?=[A-Z])|[_-]+", RegexOptions.Compiled);

        /// <summary>Remove TextMeshPro rich text tags and tidy whitespace.</summary>
        public static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return "";
            }
            s = Tags.Replace(s, "");
            s = s.Replace("­", "").Replace("​", "").Replace("\r", "");
            s = Spaces.Replace(s, " ");
            s = Lines.Replace(s, "\n");
            return s.Trim();
        }

        /// <summary>Turn an object name like "AdventureSheet_Button" into "Adventure Sheet Button".</summary>
        public static string Humanize(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "";
            }
            name = name.Replace("(Clone)", "");
            return Spaces.Replace(CamelSplit.Replace(name, " "), " ").Trim();
        }

        public static bool IsVisible(GameObject go)
        {
            if (go == null || !go.activeInHierarchy)
            {
                return false;
            }
            foreach (CanvasGroup cg in go.GetComponentsInParent<CanvasGroup>())
            {
                if (cg.alpha <= 0.01f)
                {
                    return false;
                }
                if (cg.ignoreParentGroups)
                {
                    break;
                }
            }
            return true;
        }

        /// <summary>All visible text under an object, in hierarchy order, de-duplicated.</summary>
        public static List<string> TextsUnder(GameObject go, bool requireVisible = true)
        {
            List<string> result = new List<string>();
            if (go == null)
            {
                return result;
            }
            foreach (Component c in go.GetComponentsInChildren<Component>(false))
            {
                string t = null;
                if (c is TMP_Text tmp && tmp.enabled)
                {
                    t = tmp.text;
                }
                else if (c is Text ut && ut.enabled)
                {
                    t = ut.text;
                }
                if (t == null)
                {
                    continue;
                }
                if (requireVisible && !IsVisible(c.gameObject))
                {
                    continue;
                }
                t = Clean(t);
                if (t.Length > 0 && !result.Contains(t))
                {
                    result.Add(t);
                }
            }
            return result;
        }

        /// <summary>A spoken label for a UI object: its text, or its humanized name if it has none.</summary>
        public static string LabelFor(GameObject go)
        {
            if (go == null)
            {
                return "";
            }
            List<string> texts = TextsUnder(go);
            if (texts.Count > 0)
            {
                return string.Join(", ", texts.ToArray());
            }
            return Humanize(go.name);
        }

        /// <summary>True while the player is typing in one of the game's text fields (so letter hotkeys must stay quiet).</summary>
        public static bool TypingInField()
        {
            UnityEngine.EventSystems.EventSystem es = UnityEngine.EventSystems.EventSystem.current;
            GameObject go = es != null ? es.currentSelectedGameObject : null;
            TMP_InputField field = go != null ? go.GetComponent<TMP_InputField>() : null;
            return field != null && field.isFocused;
        }

        public static string Join(IEnumerable<string> parts, string sep = ". ")
        {
            StringBuilder sb = new StringBuilder();
            foreach (string p in parts)
            {
                if (string.IsNullOrEmpty(p))
                {
                    continue;
                }
                if (sb.Length > 0)
                {
                    sb.Append(sep);
                }
                sb.Append(p);
            }
            return sb.ToString();
        }
    }
}
