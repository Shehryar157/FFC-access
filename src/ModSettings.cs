using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace FFCAccess
{
    internal enum ReadingLayout
    {
        WholeSection,
        PageByPage
    }

    /// <summary>
    /// The mod's settings. BepInEx stores them in BepInEx\config\ffcaccess.screenreader.cfg and saves
    /// automatically whenever one changes, so the in-game menu and the file always agree.
    /// </summary>
    internal static class ModSettings
    {
        public static ConfigEntry<ReadingLayout> Layout;
        public static ConfigEntry<bool> AutoRead;
        public static ConfigEntry<bool> AnnouncePageBreaks;
        public static ConfigEntry<bool> PreferSapi;

        public static void Bind(ConfigFile config)
        {
            Layout = config.Bind("Reading", "Layout", ReadingLayout.WholeSection,
                "WholeSection reads a section as one document. PageByPage shows one book page at a time; Page Up and Page Down turn pages.");
            AutoRead = config.Bind("Reading", "AutoRead", true,
                "Read each new section (or page) aloud as soon as it opens.");
            AnnouncePageBreaks = config.Bind("Reading", "AnnouncePageBreaks", true,
                "In whole-section layout, mark the page breaks the book's authors placed on purpose.");
            PreferSapi = config.Bind("Speech", "PreferSAPI", false,
                "Speak with Windows SAPI voices even when a screen reader (NVDA, JAWS) is running.");
        }
    }

    /// <summary>
    /// The mod's own settings menu (F9). Up and Down move, Left, Right, Enter or Space change a value,
    /// Escape or F9 close. While it's open the game receives no input.
    /// </summary>
    internal static class SettingsMenu
    {
        private class Item
        {
            public string Name;
            public Func<string> Value;
            public Action<int> Change;
        }

        public static bool Open { get; private set; }

        private static int index;
        private static List<Item> items;

        private static List<Item> Items()
        {
            if (items != null)
            {
                return items;
            }
            items = new List<Item>
            {
                new Item
                {
                    Name = "Reading layout",
                    Value = () => ModSettings.Layout.Value == ReadingLayout.PageByPage ? "page by page" : "whole section",
                    Change = dir =>
                    {
                        ModSettings.Layout.Value = ModSettings.Layout.Value == ReadingLayout.PageByPage ? ReadingLayout.WholeSection : ReadingLayout.PageByPage;
                        BookReader.Reload(false);
                    }
                },
                Toggle("Read new sections automatically", ModSettings.AutoRead),
                Toggle("Announce page breaks", ModSettings.AnnouncePageBreaks, () => BookReader.Reload(true)),
                Toggle("Use Windows voices even with a screen reader", ModSettings.PreferSapi, () => Speech.SetPreferSapi(ModSettings.PreferSapi.Value)),
            };
            return items;
        }

        private static Item Toggle(string name, ConfigEntry<bool> entry, Action after = null)
        {
            return new Item
            {
                Name = name,
                Value = () => entry.Value ? "on" : "off",
                Change = dir =>
                {
                    entry.Value = !entry.Value;
                    after?.Invoke();
                }
            };
        }

        private static string Describe(int i)
        {
            Item it = Items()[i];
            return it.Name + ", " + it.Value() + ", " + (i + 1) + " of " + Items().Count;
        }

        public static void Toggle()
        {
            if (Open)
            {
                Close();
                return;
            }
            Open = true;
            index = Mathf.Clamp(index, 0, Items().Count - 1);
            Speech.SayPriority("Mod settings. Up and down to move, left, right or enter to change, escape to close. " + Describe(index), true, 1f);
        }

        private static void Close()
        {
            Open = false;
            BookReader.BlockGameInputBriefly();
            Speech.Say("Settings closed.");
        }

        /// <summary>While open, the menu uses every key, so nothing else sees them.</summary>
        public static bool HandleKeys()
        {
            if (!Open)
            {
                return false;
            }
            List<Item> list = Items();
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.F9))
            {
                Close();
            }
            else if (KeyRepeat.Pressed(KeyCode.DownArrow))
            {
                index = Mathf.Min(index + 1, list.Count - 1);
                Speech.Say(Describe(index));
            }
            else if (KeyRepeat.Pressed(KeyCode.UpArrow))
            {
                index = Mathf.Max(index - 1, 0);
                Speech.Say(Describe(index));
            }
            else if (Input.GetKeyDown(KeyCode.Home))
            {
                index = 0;
                Speech.Say(Describe(index));
            }
            else if (Input.GetKeyDown(KeyCode.End))
            {
                index = list.Count - 1;
                Speech.Say(Describe(index));
            }
            else if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.RightArrow) ||
                     Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space))
            {
                list[index].Change(Input.GetKeyDown(KeyCode.LeftArrow) ? -1 : 1);
                Speech.Say(list[index].Value());
            }
            return true;
        }
    }
}
