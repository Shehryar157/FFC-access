using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace FFCAccessInstaller
{
    /// <summary>
    /// Installs FFC Access into Fighting Fantasy Classics, and updates it when run again.
    /// The mod's files travel inside this program (an embedded "payload" zip), so there's only one thing to download.
    /// When run, it checks GitHub for a newer installer; if there is one, it downloads and starts that instead.
    /// It's a plain console program so screen readers read every line, and it only needs Enter and Escape.
    /// </summary>
    internal static class Program
    {
        private const string Repo = "Shehryar157/FFC-access";
        private const string GameExe = "Fighting Fantasy Classics.exe";
        private const string GameProcess = "Fighting Fantasy Classics";
        private const string InstallerName = "FFCAccess Installer.exe";
        private static readonly string PluginFolder = Path.Combine("BepInEx", "plugins", "FFCAccess");
        private static readonly string[] AudioExtensions = { ".ogg", ".wav", ".mp3" };

        private class Release
        {
            public Version Version;
            public string Url;
            public string Name;
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

            // 3. What this installer carries: a zip next to it (for testing), or its own embedded files.
            string localZip = FindLocalZip();
            Version carried = localZip != null ? VersionFromName(localZip) : PayloadVersion();
            if (carried == null)
            {
                Say("This installer doesn't contain the mod's files. Please download it again.");
                Pause();
                return 1;
            }

            // 4. Is there a newer installer online? If so, hand over to it.
            if (localZip == null)
            {
                Say("Checking for a newer version online.");
                Release online = NewestRelease();
                if (online != null && online.Version > carried)
                {
                    Say("Version " + online.Version + " is available online. This installer has version " + carried + ".");
                    if (Confirm("Download the newer version and continue with it? Press Enter for yes, or Escape to use version " + carried + " instead."))
                    {
                        string newer = Download(online);
                        if (newer != null)
                        {
                            Say("Starting the newer installer.");
                            Process.Start(newer, "\"" + game + "\"");
                            return 0;
                        }
                    }
                }
            }
            Say("Version available: " + carried + ".");

            if (installed != null && carried <= installed)
            {
                Say("You already have the newest version. Nothing to do.");
                Pause();
                return 0;
            }

            // 5. Ask, make sure the game is closed, then install.
            string verb = installed == null ? "Install" : "Update to";
            if (!Confirm(verb + " version " + carried + "? Press Enter to continue, or Escape to cancel."))
            {
                Say("Cancelled. Nothing was changed.");
                Pause();
                return 0;
            }
            WaitForGameClosed();
            using (ZipArchive archive = localZip != null ? ZipFile.OpenRead(localZip) : OpenPayload())
            {
                Install(archive, game);
            }
            TidyOldSoundsFolder(game);
            CopySelfToGame(game);
            Say("Done. Version " + carried + " is installed. Start the game from Steam as usual.");
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

        // ---------- Versions and the embedded files ----------

        /// <summary>Each install writes version.txt into the plugin folder. Older installs without it count as 0.0.</summary>
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

        /// <summary>"FFCAccess-0.9.0.zip" or "v0.9.0" becomes version 0.9.0.</summary>
        private static Version VersionFromName(string name)
        {
            Match m = Regex.Match(Path.GetFileName(name), @"(\d+\.\d+(\.\d+)?)");
            return m.Success && Version.TryParse(m.Groups[1].Value, out Version v) ? v : null;
        }

        /// <summary>The mod's files, packed inside this program when it was built.</summary>
        private static ZipArchive OpenPayload()
        {
            Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip");
            return s == null ? null : new ZipArchive(s, ZipArchiveMode.Read);
        }

        private static Version PayloadVersion()
        {
            using (ZipArchive payload = OpenPayload())
            {
                ZipArchiveEntry entry = payload?.GetEntry("BepInEx/plugins/FFCAccess/version.txt");
                if (entry == null) return null;
                using (StreamReader r = new StreamReader(entry.Open()))
                {
                    return Version.TryParse(r.ReadToEnd().Trim(), out Version v) ? v : null;
                }
            }
        }

        private static string FindLocalZip()
        {
            string here = AppDomain.CurrentDomain.BaseDirectory;
            return Directory.GetFiles(here, "FFCAccess-*.zip")
                .OrderByDescending(f => VersionFromName(f) ?? new Version(0, 0))
                .FirstOrDefault();
        }

        // ---------- Checking online ----------

        private static HttpClient NewClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("FFCAccess-Installer");
            return client;
        }

        /// <summary>Ask GitHub for the project's releases and pick the newest one that has an installer.</summary>
        private static Release NewestRelease()
        {
            try
            {
                using (HttpClient client = NewClient())
                {
                    HttpResponseMessage response = client.GetAsync("https://api.github.com/repos/" + Repo + "/releases").Result;
                    if (!response.IsSuccessStatusCode)
                    {
                        Say("Couldn't check online (" + (int)response.StatusCode + "), so using the version in this installer.");
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
                            // GitHub turns the space in "FFCAccess Installer.exe" into a dot.
                            string name = a["name"] as string ?? "";
                            if (name.StartsWith("FFCAccess") && name.EndsWith("Installer.exe"))
                            {
                                best = new Release { Version = v, Name = name, Url = a["browser_download_url"] as string };
                            }
                        }
                    }
                    return best;
                }
            }
            catch (Exception e)
            {
                Say("Couldn't check online (" + e.GetBaseException().Message + "), so using the version in this installer.");
                return null;
            }
        }

        private static string Download(Release release)
        {
            Say("Downloading version " + release.Version + ". Please wait.");
            string path = Path.Combine(Path.GetTempPath(), "FFCAccess Installer " + release.Version + ".exe");
            try
            {
                using (HttpClient client = NewClient())
                using (HttpResponseMessage response = client.GetAsync(release.Url).Result)
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        Say("The download failed (" + (int)response.StatusCode + ").");
                        return null;
                    }
                    using (FileStream file = File.Create(path))
                    {
                        response.Content.CopyToAsync(file).Wait();
                    }
                }
            }
            catch (Exception e)
            {
                Say("The download failed: " + e.GetBaseException().Message);
                return null;
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
        /// Copy the files into the game folder, keeping the player's own BepInEx settings (with the one setting the mod
        /// needs switched on) and backing up edited picture descriptions as .bak. Then remove files an earlier version
        /// installed that this one doesn't have, using the list each install leaves behind (installed-files.txt).
        /// </summary>
        private static void Install(ZipArchive archive, string game)
        {
            Say("Installing.");
            string gameRoot = Path.GetFullPath(game).TrimEnd('\\') + "\\";
            string manifest = Path.Combine(game, PluginFolder, "installed-files.txt");
            HashSet<string> before = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(manifest))
            {
                before.UnionWith(File.ReadAllLines(manifest).Where(l => l.Trim().Length > 0));
            }
            HashSet<string> now = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { InstallerName };
            int count = 0;
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (entry.FullName.EndsWith("/")) continue;
                string relative = entry.FullName.Replace('/', '\\');
                string dest = Path.GetFullPath(Path.Combine(game, relative));
                if (!dest.StartsWith(gameRoot, StringComparison.OrdinalIgnoreCase))
                {
                    continue; // never write outside the game folder
                }
                now.Add(relative);
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
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                entry.ExtractToFile(dest, true);
                count++;
            }
            Say("Copied " + count + " files.");

            // Files the previous version installed that this one doesn't: remove them, but only the mod's own.
            int removed = 0;
            foreach (string old in before.Except(now, StringComparer.OrdinalIgnoreCase))
            {
                if (!IsModFile(old)) continue;
                string path = Path.GetFullPath(Path.Combine(game, old));
                if (path.StartsWith(gameRoot, StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                {
                    File.Delete(path);
                    removed++;
                }
            }
            if (removed > 0) Say("Removed " + removed + " files that are no longer part of the mod.");
            File.WriteAllLines(manifest, now.OrderBy(f => f));
        }

        /// <summary>
        /// Only the mod's own files may be removed: those in its plugin folder, and its readme and installer next to the
        /// game. Never BepInEx itself (other mods may need it) and never the player's own files (dumps, .bak backups).
        /// </summary>
        private static bool IsModFile(string relative)
        {
            if (relative.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)) return false;
            if (relative.StartsWith(PluginFolder + @"\dumps\", StringComparison.OrdinalIgnoreCase)) return false;
            return relative.StartsWith(PluginFolder + @"\", StringComparison.OrdinalIgnoreCase)
                || (!relative.Contains("\\") && relative.StartsWith("FFCAccess", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Version 0.8 kept custom sounds in "FFCAccess Sounds". They now belong to the separate FFC Sounds mod, which
        /// uses a folder called "Sounds". Move the old folder if it has sounds in it; remove it if it only has our help files.
        /// </summary>
        private static void TidyOldSoundsFolder(string game)
        {
            string old = Path.Combine(game, "FFCAccess Sounds");
            if (!Directory.Exists(old)) return;
            bool hasSounds = Directory.GetFiles(old, "*", SearchOption.AllDirectories)
                .Any(f => AudioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
            string target = Path.Combine(game, "Sounds");
            if (!hasSounds)
            {
                Directory.Delete(old, true);
                Say("Removed the old, empty FFCAccess Sounds folder. Custom sounds are now part of the FFC Sounds mod.");
            }
            else if (!Directory.Exists(target))
            {
                Directory.Move(old, target);
                Say("Moved your sounds from FFCAccess Sounds to the Sounds folder, used by the FFC Sounds mod.");
            }
            else
            {
                Say("Your old FFCAccess Sounds folder has sounds in it, and a Sounds folder already exists, so I left both alone.");
            }
        }

        /// <summary>Keep a copy of the installer in the game folder, so it can be run from there to update later.</summary>
        private static void CopySelfToGame(string game)
        {
            string self = Path.GetFullPath(Process.GetCurrentProcess().MainModule.FileName);
            string dest = Path.GetFullPath(Path.Combine(game, InstallerName));
            if (self.Equals(dest, StringComparison.OrdinalIgnoreCase)) return;
            if (File.Exists(dest))
            {
                // It may be an older installer that's still around; Windows allows renaming even a running program.
                string old = dest + ".old";
                if (File.Exists(old)) File.Delete(old);
                File.Move(dest, old);
            }
            File.Copy(self, dest);
        }

        /// <summary>Remove the copy of the installer left behind by an earlier update.</summary>
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
