using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using UnityEngine;
using UnityEngine.Networking;

namespace FFCAccess
{
    /// <summary>
    /// Replaceable combat and dice sounds, kept in the "FFCAccess Sounds" folder next to the game.
    /// Each event (you hit, you were hit, dice...) has a folder of sound files; one is picked at random, never the
    /// same twice running, with a slightly varied pitch. Sounds are looked up from most to least specific:
    ///   books/&lt;book id&gt;/&lt;event&gt;  -  sets/&lt;the book's set&gt;/&lt;event&gt;  -  sets/default/&lt;event&gt;  -  the game's own sound.
    /// Car fights use the "vehicles" set and ship fights the "scifi" set automatically.
    /// Everything plays through the game's own sound player, so its Sound FX volume and on/off switch apply.
    /// </summary>
    internal static class SoundPacks
    {
        public const string PlayerHit = "player_hit";
        public const string EnemyHit = "enemy_hit";
        public const string Draw = "draw";
        public const string EnemyDefeated = "enemy_defeated";
        public const string PlayerDefeated = "player_defeated";
        public const string Lucky = "lucky";
        public const string Unlucky = "unlucky";
        public const string Dice = "dice";

        private static readonly string[] Events = { PlayerHit, EnemyHit, Draw, EnemyDefeated, PlayerDefeated, Lucky, Unlucky, Dice };
        private static readonly string[] Sets = { "default", "vehicles", "scifi", "superhero", "horror" };
        private static readonly string[] Extensions = { ".ogg", ".wav", ".mp3" };

        // Folder path (lower case) -> the clips loaded from it.
        private static readonly Dictionary<string, List<AudioClip>> clips = new Dictionary<string, List<AudioClip>>();
        private static readonly Dictionary<string, AudioClip> lastPlayed = new Dictionary<string, AudioClip>();
        private static Dictionary<string, string> bookSets = new Dictionary<string, string>();
        private static readonly System.Random random = new System.Random();

        public static string Root => Path.Combine(Paths.GameRootPath, "FFCAccess Sounds");

        public static void Init()
        {
            try
            {
                EnsureFolders();
                LoadBookSets();
                Plugin.Instance.StartCoroutine(LoadAll());
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Sound packs failed to start: " + e);
            }
        }

        // ---------- Folders and the book list ----------

        /// <summary>Create any missing folders and help files. Never deletes or overwrites anything.</summary>
        private static void EnsureFolders()
        {
            foreach (string set in Sets)
            {
                foreach (string ev in Events)
                {
                    Directory.CreateDirectory(Path.Combine(Path.Combine(Path.Combine(Root, "sets"), set), ev));
                }
            }
            Directory.CreateDirectory(Path.Combine(Root, "books"));
            string help = Path.Combine(Root, "How to add sounds.txt");
            if (!File.Exists(help))
            {
                File.WriteAllText(help, HelpText);
            }
            string list = Path.Combine(Root, "book sets.txt");
            if (!File.Exists(list))
            {
                File.WriteAllText(list, DefaultBookSets);
            }
        }

        /// <summary>Read "book sets.txt": lines like "ffhub.houseofhell = horror". Lines starting with # are notes.</summary>
        private static void LoadBookSets()
        {
            bookSets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in File.ReadAllLines(Path.Combine(Root, "book sets.txt")))
            {
                string line = raw.Trim();
                int eq = line.IndexOf('=');
                if (line.Length == 0 || line.StartsWith("#") || eq <= 0)
                {
                    continue;
                }
                bookSets[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
        }

        // ---------- Loading ----------

        private static IEnumerator LoadAll()
        {
            int count = 0;
            List<string> folders = new List<string>();
            foreach (string set in Directory.GetDirectories(Path.Combine(Root, "sets")))
            {
                folders.AddRange(Directory.GetDirectories(set));
            }
            foreach (string book in Directory.GetDirectories(Path.Combine(Root, "books")))
            {
                folders.AddRange(Directory.GetDirectories(book));
            }
            foreach (string folder in folders)
            {
                List<AudioClip> list = new List<AudioClip>();
                foreach (string file in Directory.GetFiles(folder))
                {
                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    if (Array.IndexOf(Extensions, ext) < 0)
                    {
                        continue;
                    }
                    AudioClip clip = null;
                    if (ext == ".wav")
                    {
                        clip = WavLoader.Load(file);
                    }
                    else
                    {
                        // Unity's own loader handles compressed formats. It works on a "file:///" web address.
                        using (UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip(new Uri(file).AbsoluteUri,
                                   ext == ".ogg" ? AudioType.OGGVORBIS : AudioType.MPEG))
                        {
                            yield return req.SendWebRequest();
                            if (req.result == UnityWebRequest.Result.Success)
                            {
                                clip = DownloadHandlerAudioClip.GetContent(req);
                            }
                            else
                            {
                                Plugin.Log.LogWarning("Could not load sound " + file + ": " + req.error);
                            }
                        }
                    }
                    if (clip != null)
                    {
                        clip.name = Path.GetFileNameWithoutExtension(file);
                        list.Add(clip);
                        count++;
                    }
                }
                if (list.Count > 0)
                {
                    clips[folder.ToLowerInvariant()] = list;
                }
            }
            Plugin.Log.LogInfo("Loaded " + count + " custom sounds from " + Root);
        }

        // ---------- Choosing and playing ----------

        /// <summary>The folders to try for an event, most specific first.</summary>
        private static IEnumerable<string> Candidates(string ev)
        {
            string book = BBGameController.instance?.book?.ID;
            if (!string.IsNullOrEmpty(book))
            {
                yield return Path.Combine(Path.Combine(Path.Combine(Root, "books"), book), ev);
            }
            string set = null;
            CombatController cc = CombatController.instance;
            if (cc != null && cc.inCombat && cc.carCombat) set = "vehicles";
            else if (cc != null && cc.inCombat && cc.shipCombat) set = "scifi";
            else if (!string.IsNullOrEmpty(book)) bookSets.TryGetValue(book, out set);
            if (!string.IsNullOrEmpty(set))
            {
                yield return Path.Combine(Path.Combine(Path.Combine(Root, "sets"), set), ev);
            }
            yield return Path.Combine(Path.Combine(Path.Combine(Root, "sets"), "default"), ev);
        }

        private static List<AudioClip> Find(string ev)
        {
            foreach (string folder in Candidates(ev))
            {
                List<AudioClip> list;
                if (clips.TryGetValue(folder.ToLowerInvariant(), out list) && list.Count > 0)
                {
                    return list;
                }
            }
            return null;
        }

        public static bool Enabled => ModSettings.CustomSounds.Value;

        /// <summary>Is there a custom sound for this event right now (in this book, this kind of fight)?</summary>
        public static bool Has(string ev)
        {
            return Enabled && Find(ev) != null;
        }

        /// <summary>Play a random sound for the event, never the same one twice in a row. Returns false if there is none.</summary>
        public static bool Play(string ev)
        {
            if (!Enabled)
            {
                return false;
            }
            List<AudioClip> list = Find(ev);
            BBSoundFX player = BBGameController.instance != null ? BBGameController.instance.SoundFX : null;
            if (list == null || player == null)
            {
                return false;
            }
            AudioClip last;
            lastPlayed.TryGetValue(ev, out last);
            AudioClip pick = list[random.Next(list.Count)];
            if (list.Count > 1)
            {
                while (pick == last)
                {
                    pick = list[random.Next(list.Count)];
                }
            }
            lastPlayed[ev] = pick;
            // A slightly different pitch each time keeps repeats from sounding identical.
            float pitch = 0.94f + (float)random.NextDouble() * 0.12f;
            player.playClipWithPitch(pick, pitch);
            return true;
        }

        // ---------- Help files written into the folder ----------

        private const string HelpText =
@"FFC Access sounds
=================

Put sound files (.ogg, .wav or .mp3) into these folders to give fights and dice rolls their own sounds.
Each folder can hold as many files as you like; one is picked at random each time, never the same one twice
in a row, with a slightly different pitch. File names don't matter.

The events
----------
  player_hit        you wound the enemy
  enemy_hit         the enemy wounds you
  draw              a round where nobody is hurt
  enemy_defeated    the enemy is beaten
  player_defeated   you are beaten
  lucky             a Luck test in combat succeeds
  unlucky           a Luck test in combat fails
  dice              dice are thrown (replaces the game's dice sound)

Where sounds are looked for, in order
-------------------------------------
  1. books\<book id>\<event>       sounds for one particular book
  2. sets\<set>\<event>            the set that book uses (see book sets.txt)
  3. sets\default\<event>          the fallback for every book
  4. the game's own sound          when none of the above has files

Car fights (Freeway Fighter) automatically use the ""vehicles"" set, and ship fights (Starship Traveller)
the ""scifi"" set. To give one book its own sounds, make a folder inside ""books"" named after the book's id
(the ids are listed in book sets.txt), with event folders inside it like the ones in sets.

Which set each book uses is in book sets.txt; change a line to move a book to another set. You can also add
your own set: make a new folder inside ""sets"" with the event folders inside it, and name it in book sets.txt.

Sounds play through the game, so its Sound FX volume and on/off switch apply. Custom sounds can be turned
off in the mod settings (F9). Changes to the files are picked up the next time the game starts.

If you share these sounds with other people, only use sounds you are allowed to share, for example from
public domain (CC0) libraries.
";

        private const string DefaultBookSets =
@"# Which sound set each book uses. Format:  book id = set name
# Sets are the folders inside ""sets"". Books not listed here use ""default"".
# Car and ship fights always use ""vehicles"" and ""scifi"", whatever is written here.

ffclassic.warlockoffiretopmountain = default
ffhub.citadelofchaos = default
ffhub.forestofdoom = default
ffhub.starshiptraveller = scifi
ffhub.cityofthieves = default
ffhub.deathtrapdungeon = default
ffhub.islandofthelizardking = default
ffhub.scorpionswamp = default
ffhub.cavernsofthesnowwitch = default
ffhub.houseofhell = horror
ffhub.freewayfighter = vehicles
ffhub.templeofterror = default
ffhub.appointmentwithfear = superhero
ffhub.trialofchampions = default
ffhub.creatureofhavoc = default
ffhub.cryptofthesorcerer = default
ffhub.armiesofdeath = default
ffhub.returntofiretopmountain = default
ffhub.eyeofthedragon = default
ffhub.bloodbones = default
ffhub.portofperil = default
ffhub.assassinsofallansia = default
";
    }

    /// <summary>
    /// Reads uncompressed .wav files into an AudioClip. A .wav is a small header followed by raw samples;
    /// we turn the samples into numbers between -1 and 1, which is what Unity wants.
    /// </summary>
    internal static class WavLoader
    {
        public static AudioClip Load(string path)
        {
            try
            {
                byte[] b = File.ReadAllBytes(path);
                int channels = 0, rate = 0, bits = 0, format = 0, dataStart = -1, dataLength = 0;
                // The file is a list of "chunks": a 4-letter name, a length, then the content.
                int pos = 12;
                while (pos + 8 <= b.Length)
                {
                    string id = System.Text.Encoding.ASCII.GetString(b, pos, 4);
                    int len = BitConverter.ToInt32(b, pos + 4);
                    if (id == "fmt ")
                    {
                        format = BitConverter.ToInt16(b, pos + 8);
                        channels = BitConverter.ToInt16(b, pos + 10);
                        rate = BitConverter.ToInt32(b, pos + 12);
                        bits = BitConverter.ToInt16(b, pos + 22);
                    }
                    else if (id == "data")
                    {
                        dataStart = pos + 8;
                        dataLength = Math.Min(len, b.Length - dataStart);
                        break;
                    }
                    pos += 8 + len + (len & 1);
                }
                if (dataStart < 0 || channels <= 0 || rate <= 0)
                {
                    Plugin.Log.LogWarning("Not a readable .wav file: " + path);
                    return null;
                }
                int bytesPer = bits / 8;
                int count = dataLength / bytesPer;
                float[] samples = new float[count];
                for (int i = 0; i < count; i++)
                {
                    int o = dataStart + i * bytesPer;
                    switch (bits)
                    {
                        case 8: samples[i] = (b[o] - 128) / 128f; break;
                        case 16: samples[i] = BitConverter.ToInt16(b, o) / 32768f; break;
                        case 24: samples[i] = ((b[o] | b[o + 1] << 8 | (sbyte)b[o + 2] << 16)) / 8388608f; break;
                        case 32: samples[i] = format == 3 ? BitConverter.ToSingle(b, o) : BitConverter.ToInt32(b, o) / 2147483648f; break;
                        default:
                            Plugin.Log.LogWarning("Unsupported .wav bit depth " + bits + ": " + path);
                            return null;
                    }
                }
                AudioClip clip = AudioClip.Create(Path.GetFileNameWithoutExtension(path), count / channels, channels, rate, false);
                clip.SetData(samples, 0);
                return clip;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not read .wav " + path + ": " + e.Message);
                return null;
            }
        }
    }
}
