using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace FFCAccessInstaller
{
    /// <summary>
    /// Installs FFC Access into Fighting Fantasy Classics, and updates it when run again.
    /// It's a plain console program so screen readers read every line, and it only needs Enter and Escape.
    /// Steps: find the game, see which version is installed, find the newest version (a zip next to this program,
    /// or the newest release on GitHub), ask, then copy the files in, without touching the player's own files.
    /// </summary>
    internal static class Program
    {
        private const string Repo = "Shehryar157/FFC-access";
        private const string GameExe = "Fighting Fantasy Classics.exe";
        private const string GameProcess = "Fighting Fantasy Classics";
        private static readonly string PluginFolder = Path.Combine("BepInEx", "plugins", "FFCAccess");

        private class Release
        {
            public Version Version;
            public string ZipUrl;
            public string ZipName;
        }

        private static int Main(string[] args)
        {
            try
            {
                return Run(args);
            }
            catch (Exception e)
            {
                Say("Something went wrong: " + e.Message);
                Pause();
                return 1;
            }
        }

        private static int Run(string[] args)
        {
            Console.Title = "FFC Access installer";
            Say("FFC Access installer.");
            CleanUpOldInstaller();

            // 1. Find the game.
            string game = args.Length > 0 && IsGameFolder(args[0]) ? args[0] : FindGame() ?? AskForGame();
            if (game == null)
            {
                Say("Cancelled.");
                Pause();
                return 1;
            }
            Say("Game folder: " + game);

            // 2. What's installed now?
            Version installed = InstalledVersion(game);
            if (installed == null) Say("The mod is not installed yet.");
            else if (installed.Major == 0 && installed.Minor == 0) Say("An older version of the mod is installed.");
            else Say("Installed version: " + installed + ".");

            // 3. What's available: a zip next to this program, or the newest release online.
            string localZip = FindLocalZip();
            Release online = null;
            Version available;
            if (localZip != null)
            {
                available = VersionFromName(localZip) ?? new Version(0, 0, 1);
                Say("Found " + Path.GetFileName(localZip) + " next to the installer.");
            }
            else
            {
                Say("Checking for the newest version online.");
                online = NewestRelease();
                if (online == null)
                {
                    Pause();
                    return 1;
                }
                available = online.Version;
            }
            Say("Newest version: " + available + ".");

            if (installed != null && available <= installed)
            {
                Say("You already have the newest version. Nothing to do.");
                Pause();
                return 0;
            }

            // 4. Ask, make sure the game is closed, then install.
            string verb = installed == null ? "Install" : "Update to";
            if (!Confirm(verb + " version " + available + "? Press Enter to continue, or Escape to cancel."))
            {
                Say("Cancelled. Nothing was changed.");
                Pause();
                return 0;
            }
            WaitForGameClosed();
            string zip = localZip ?? Download(online);
            if (zip == null)
            {
                Pause();
                return 1;
            }
            Install(zip, game);
            Say("Done. Version " + available + " is installed. Start the game from Steam as usual.");
            Pause();
            return 0;
        }

        // ---------- Finding the game ----------

        private static bool IsGameFolder(string folder)
        {
            return !string.IsNullOrEmpty(folder) && File.Exists(Path.Combine(folder, GameExe));
        }

        /// <summary>
        /// Steam keeps a list of its library folders (one per drive you install games on) in libraryfolders.vdf.
        /// Look for the game in each of them.
        /// </summary>
        private static string FindGame()
        {
            List<string> steamRoots = new List<string>();
            foreach (string key in new[] { @"HKEY_CURRENT_USER\Software\Valve\Steam", @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam" })
            {
                foreach (string name in new[] { "SteamPath", "InstallPath" })
                {
                    if (Registry.GetValue(key, name, null) is string p && Directory.Exists(p))
                    {
                        steamRoots.Add(Path.GetFullPath(p));
                    }
                }
            }
            HashSet<string> libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string root in steamRoots)
            {
                libraries.Add(root);
                string vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(vdf)) continue;
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                {
                    libraries.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
                }
            }
            foreach (string lib in libraries)
            {
                string folder = Path.Combine(lib, "steamapps", "common", "Fighting Fantasy Classics");
                if (IsGameFolder(folder))
                {
                    return folder;
                }
            }
            return null;
        }

        private static string AskForGame()
        {
            Say("I couldn't find Fighting Fantasy Classics.");
            while (true)
            {
                Say("Paste the game folder and press Enter, or just press Enter to cancel.");
                Say("To find it in Steam: open the game's context menu, choose Manage, then Browse local files.");
                string line = (Console.ReadLine() ?? "").Trim().Trim('"');
                if (line.Length == 0) return null;
                if (IsGameFolder(line)) return line;
                Say("That folder doesn't contain " + GameExe + ".");
            }
        }

        // ---------- Versions ----------

        /// <summary>The release zip writes version.txt into the plugin folder. Older installs without it count as 0.0.</summary>
        private static Version InstalledVersion(string game)
        {
            string plugin = Path.Combine(game, PluginFolder);
            string file = Path.Combine(plugin, "version.txt");
            if (File.Exists(file) && Version.TryParse(File.ReadAllText(file).Trim(), out Version v))
            {
                return v;
            }
            return File.Exists(Path.Combine(plugin, "FFCAccess.dll")) ? new Version(0, 0) : null;
        }

        /// <summary>"FFCAccess-0.8.0.zip" or "v0.8.0" becomes version 0.8.0.</summary>
        private static Version VersionFromName(string name)
        {
            Match m = Regex.Match(Path.GetFileName(name), @"(\d+\.\d+(\.\d+)?)");
            return m.Success && Version.TryParse(m.Groups[1].Value, out Version v) ? v : null;
        }

        // ---------- Where the new version comes from ----------

        private static string FindLocalZip()
        {
            string here = AppDomain.CurrentDomain.BaseDirectory;
            return Directory.GetFiles(here, "FFCAccess-*.zip")
                .OrderByDescending(f => VersionFromName(f) ?? new Version(0, 0))
                .FirstOrDefault();
        }

        private static HttpClient NewClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("FFCAccess-Installer");
            return client;
        }

        /// <summary>Ask GitHub for the project's releases and pick the newest one that has a zip.</summary>
        private static Release NewestRelease()
        {
            using (HttpClient client = NewClient())
            {
                HttpResponseMessage response = client.GetAsync("https://api.github.com/repos/" + Repo + "/releases").Result;
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    Say("The download page couldn't be found. It may not be public yet.");
                    return null;
                }
                if (!response.IsSuccessStatusCode)
                {
                    Say("Couldn't check for updates: " + (int)response.StatusCode + " " + response.ReasonPhrase + ".");
                    return null;
                }
                object[] releases = new JavaScriptSerializer().DeserializeObject(response.Content.ReadAsStringAsync().Result) as object[];
                Release best = null;
                foreach (Dictionary<string, object> r in (releases ?? new object[0]).OfType<Dictionary<string, object>>())
                {
                    if (r.TryGetValue("draft", out object draft) && draft is bool d && d) continue;
                    Version v = VersionFromName(r["tag_name"] as string ?? "");
                    if (v == null || (best != null && v <= best.Version)) continue;
                    foreach (Dictionary<string, object> a in ((object[])r["assets"]).OfType<Dictionary<string, object>>())
                    {
                        string name = a["name"] as string ?? "";
                        if (name.StartsWith("FFCAccess-") && name.EndsWith(".zip"))
                        {
                            best = new Release { Version = v, ZipName = name, ZipUrl = a["browser_download_url"] as string };
                        }
                    }
                }
                if (best == null) Say("No downloadable release was found.");
                return best;
            }
        }

        private static string Download(Release release)
        {
            Say("Downloading " + release.ZipName + ". Please wait.");
            string path = Path.Combine(Path.GetTempPath(), release.ZipName);
            using (HttpClient client = NewClient())
            using (HttpResponseMessage response = client.GetAsync(release.ZipUrl).Result)
            {
                if (!response.IsSuccessStatusCode)
                {
                    Say("The download failed: " + (int)response.StatusCode + " " + response.ReasonPhrase + ".");
                    return null;
                }
                using (FileStream file = File.Create(path))
                {
                    response.Content.CopyToAsync(file).Wait();
                }
            }
            Say("Downloaded.");
            return path;
        }

        // ---------- Installing ----------

        private static void WaitForGameClosed()
        {
            while (Process.GetProcessesByName(GameProcess).Length > 0)
            {
                Say("Fighting Fantasy Classics is running. Please close it, then press Enter.");
                Console.ReadLine();
            }
        }

        /// <summary>
        /// Copy everything from the zip into the game folder, except:
        /// the player's BepInEx settings (kept, with the one setting the mod needs switched on), and edited picture
        /// descriptions (backed up as .bak before being replaced). The sounds folder and the mod's settings file
        /// aren't in the zip, so they're never touched.
        /// </summary>
        private static void Install(string zip, string game)
        {
            Say("Installing.");
            string gameRoot = Path.GetFullPath(game).TrimEnd('\\') + "\\";
            string self = Path.GetFullPath(Process.GetCurrentProcess().MainModule.FileName);
            int count = 0;
            using (ZipArchive archive = ZipFile.OpenRead(zip))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (entry.FullName.EndsWith("/")) continue;
                    string relative = entry.FullName.Replace('/', '\\');
                    string dest = Path.GetFullPath(Path.Combine(game, relative));
                    if (!dest.StartsWith(gameRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        continue; // never write outside the game folder
                    }
                    if (relative.Equals(@"BepInEx\config\BepInEx.cfg", StringComparison.OrdinalIgnoreCase) && File.Exists(dest))
                    {
                        string cfg = File.ReadAllText(dest);
                        File.WriteAllText(dest, Regex.Replace(cfg, @"HideManagerGameObject = \w+", "HideManagerGameObject = true"));
                        continue;
                    }
                    if (relative.StartsWith(PluginFolder + @"\descriptions\", StringComparison.OrdinalIgnoreCase) && File.Exists(dest))
                    {
                        using (StreamReader r = new StreamReader(entry.Open()))
                        {
                            if (r.ReadToEnd() != File.ReadAllText(dest))
                            {
                                File.Copy(dest, dest + ".bak", true);
                            }
                        }
                    }
                    if (dest.Equals(self, StringComparison.OrdinalIgnoreCase))
                    {
                        // Windows won't overwrite a running program, but it will let it be renamed out of the way.
                        string old = self + ".old";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(self, old);
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    entry.ExtractToFile(dest, true);
                    count++;
                }
            }
            Say("Copied " + count + " files.");
        }

        /// <summary>Remove the copy of the installer left behind by an earlier self-update.</summary>
        private static void CleanUpOldInstaller()
        {
            try
            {
                string old = Process.GetCurrentProcess().MainModule.FileName + ".old";
                if (File.Exists(old)) File.Delete(old);
            }
            catch
            {
            }
        }

        // ---------- Talking to the player ----------

        private static void Say(string text)
        {
            Console.WriteLine(text);
        }

        private static bool Confirm(string question)
        {
            Say(question);
            if (Console.IsInputRedirected)
            {
                // Not a keyboard (e.g. a script): a blank line means yes, no input at all means no.
                string line = Console.ReadLine();
                return line != null && line.Trim().Length == 0;
            }
            while (true)
            {
                ConsoleKey key = Console.ReadKey(true).Key;
                if (key == ConsoleKey.Enter) return true;
                if (key == ConsoleKey.Escape) return false;
            }
        }

        private static void Pause()
        {
            Say("Press Enter to close.");
            Console.ReadLine();
        }
    }
}
