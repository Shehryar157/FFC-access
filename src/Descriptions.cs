using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;

namespace FFCAccess
{
    /// <summary>
    /// Picture descriptions, one JSON file per book in the plugin's "descriptions" folder, named after the book's ID
    /// (for example ffclassic.warlockoffiretopmountain.json). A file may be incomplete; pictures without an entry
    /// are announced as having no description yet. Format:
    /// { "book": "...", "images": { "section_01": { "short": "one line", "long": "full description" } } }
    /// </summary>
    internal static class Descriptions
    {
        internal class Entry
        {
            public string Short;
            public string Long;
        }

        // Book ID -> (picture name, lower case -> entry). Loaded the first time a book's pictures are needed.
        private static readonly Dictionary<string, Dictionary<string, Entry>> books = new Dictionary<string, Dictionary<string, Entry>>();

        private static string CurrentBookId => BBGameController.instance?.book?.ID;

        /// <summary>The game's picture tags may carry a suffix like ".png" or "_color"; it strips them, so we do too.</summary>
        public static string Normalize(string imageKey)
        {
            return string.IsNullOrEmpty(imageKey) ? "" : Tin.String.RemoveImageSuffixes(imageKey).Trim().ToLowerInvariant();
        }

        public static Entry Find(string imageKey)
        {
            string book = CurrentBookId;
            if (book == null || string.IsNullOrEmpty(imageKey))
            {
                return null;
            }
            Dictionary<string, Entry> table;
            if (!books.TryGetValue(book, out table))
            {
                table = Load(book);
                books[book] = table;
            }
            Entry e;
            table.TryGetValue(Normalize(imageKey), out e);
            return e;
        }

        private static Dictionary<string, Entry> Load(string bookId)
        {
            Dictionary<string, Entry> table = new Dictionary<string, Entry>();
            string path = Path.Combine(Path.Combine(Plugin.PluginDir, "descriptions"), bookId + ".json");
            if (!File.Exists(path))
            {
                Plugin.Log.LogInfo("No picture descriptions for " + bookId);
                return table;
            }
            try
            {
                JObject root = JObject.Parse(File.ReadAllText(path));
                JObject images = root["images"] as JObject;
                if (images != null)
                {
                    foreach (JProperty p in images.Properties())
                    {
                        table[Normalize(p.Name)] = new Entry
                        {
                            Short = (string)p.Value["short"],
                            Long = (string)p.Value["long"]
                        };
                    }
                }
                Plugin.Log.LogInfo("Loaded " + table.Count + " picture descriptions for " + bookId);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not read " + path + ": " + e.Message);
            }
            return table;
        }

        /// <summary>The line spoken in the reader where a picture appears.</summary>
        public static string Line(string imageKey)
        {
            Entry e = Find(imageKey);
            if (e == null || string.IsNullOrEmpty(e.Short))
            {
                return "Illustration, no description yet.";
            }
            return "Illustration: " + e.Short + " Press D for more.";
        }

        /// <summary>D: open the full description in a text window.</summary>
        public static void ShowFull(string imageKey)
        {
            Entry e = Find(imageKey);
            if (e == null)
            {
                Speech.Say("This illustration has no description yet.");
                Plugin.Log.LogInfo("Missing description for picture: " + Normalize(imageKey) + " in " + CurrentBookId);
                return;
            }
            string text = string.IsNullOrEmpty(e.Long) ? e.Short : e.Long;
            TextWindow.ShowText("Illustration", text);
        }
    }
}
