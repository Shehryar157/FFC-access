using System;
using System.IO;
using System.Text;
using Rewired;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FFCAccess
{
    /// <summary>Developer helpers: dumps of the key bindings and of the visible UI, for working on the mod.</summary>
    internal static class Diagnostics
    {
        private static bool loggedBindings;

        public static void TryLogBindings()
        {
            if (loggedBindings || !ReInput.isReady)
            {
                return;
            }
            Player player = ReInput.players.GetPlayer(0);
            if (player == null)
            {
                return;
            }
            loggedBindings = true;
            StringBuilder sb = new StringBuilder("Game key bindings:\n");
            try
            {
                foreach (ControllerMap map in player.controllers.maps.GetAllMaps(ControllerType.Keyboard))
                {
                    foreach (ActionElementMap aem in map.AllMaps)
                    {
                        InputAction action = ReInput.mapping.GetAction(aem.actionId);
                        sb.Append("  ").Append(action != null ? action.name : aem.actionId.ToString())
                          .Append(" = ").Append(aem.elementIdentifierName)
                          .Append(aem.modifierKey1 != ModifierKey.None ? " +" + aem.modifierKey1 : "")
                          .Append(map.enabled ? "" : " (map disabled)").Append('\n');
                    }
                }
            }
            catch (Exception e)
            {
                sb.Append("  failed: ").Append(e.Message);
            }
            Plugin.Log.LogInfo(sb.ToString());
        }

        /// <summary>Write every active object in loaded scenes, with components and text, to a file.</summary>
        public static void DumpScene()
        {
            string dir = Path.Combine(Plugin.PluginDir, "dumps");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "ui_" + DateTime.Now.ToString("HHmmss") + ".txt");
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                sb.Append("=== Scene ").Append(s.name).Append('\n');
                foreach (GameObject root in s.GetRootGameObjects())
                {
                    Dump(root.transform, 0, sb);
                }
            }
            // Objects marked DontDestroyOnLoad live in a hidden scene; reach it through one of them.
            if (InputLayerManager.instance != null)
            {
                Scene dd = InputLayerManager.instance.gameObject.scene;
                sb.Append("=== Scene ").Append(dd.name).Append('\n');
                foreach (GameObject root in dd.GetRootGameObjects())
                {
                    Dump(root.transform, 0, sb);
                }
                sb.Append("=== Input layers (top last)\n");
                foreach (InputLayer l in InputLayerManager.instance.inputLayers)
                {
                    if (l != null)
                    {
                        sb.Append("  ").Append(l.id).Append(l.blockInputs ? " [blocks]" : "").Append('\n');
                    }
                }
            }
            File.WriteAllText(file, sb.ToString());
            Plugin.Log.LogInfo("Wrote " + file);
            Speech.Say("Screen dumped.");
        }

        private static void Dump(Transform t, int depth, StringBuilder sb)
        {
            if (!t.gameObject.activeInHierarchy)
            {
                return;
            }
            sb.Append(' ', depth * 2).Append(t.name);
            foreach (Component c in t.GetComponents<Component>())
            {
                if (c == null || c is Transform)
                {
                    continue;
                }
                sb.Append(" [").Append(c.GetType().Name);
                if (c is TMP_Text tmp)
                {
                    sb.Append(": \"").Append(Short(tmp.text)).Append('"');
                }
                else if (c is Text ut)
                {
                    sb.Append(": \"").Append(Short(ut.text)).Append('"');
                }
                else if (c is CanvasGroup cg)
                {
                    sb.Append(" a=").Append(cg.alpha.ToString("0.##"));
                }
                else if (c is Selectable sel)
                {
                    sb.Append(sel.interactable ? "" : " disabled");
                }
                sb.Append(']');
            }
            sb.Append('\n');
            foreach (Transform child in t)
            {
                Dump(child, depth + 1, sb);
            }
        }

        private static string Short(string s)
        {
            if (s == null)
            {
                return "";
            }
            s = s.Replace("\n", "\\n");
            return s.Length > 160 ? s.Substring(0, 160) + "..." : s;
        }
    }
}
