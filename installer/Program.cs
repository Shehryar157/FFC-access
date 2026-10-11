using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace FFCAccessInstaller
{
    /// <summary>
    /// FFC Access installer, updater and uninstaller: one file the player runs and waits for.
    ///
    ///   1. It asks GitHub whether a newer installer has been released. If so it shows the new version and what's
    ///      new, and on Update downloads that one and hands over to it (Skip installs the version this file carries).
    ///   2. It finds Fighting Fantasy Classics through Steam (or asks for the folder).
    ///   3. It copies the mod's files, which travel inside this program (an embedded "payload" zip), into the game's
    ///      folder. The player's own settings and edited picture descriptions are kept.
    ///
    /// Running it again later is how you update. A copy named "Uninstall FFC Access.exe" in the game's folder removes
    /// the mod again. Messages are standard Windows message boxes; downloading and installing show a progress window
    /// with Cancel (Ui.cs), and the end plays the Windows alert sound and says "Done. Updated FFC Access from X to Y."
    ///
    ///   --no-update     skip step 1 (the newer installer is started with it)
    ///   --game "dir"    install into this folder instead of looking for the game (a bare folder path works too)
    ///   --uninstall     remove the mod
    ///   --silent        print messages instead of showing them, answer yes (testing)
    /// </summary>
    internal static class Program
    {
        private const string Repo = "Shehryar157/FFC-access";
        private const string GameName = "Fighting Fantasy Classics";
        private const string GameExe = "Fighting Fantasy Classics.exe";
        private const string GameProcess = "Fighting Fantasy Classics";
        private const string InstallerName = "FFCAccess Installer.exe";
        private const string UninstallerName = "Uninstall FFC Access.exe";
        private const string StagingFolder = "FFCAccess.new";
        private const string ModConfig = @"BepInEx\config\ffcaccess.screenreader.cfg"; // the mod's settings
        private static readonly string PluginFolder = Path.Combine("BepInEx", "plugins", "FFCAccess");
        private static readonly string[] AudioExtensions = { ".ogg", ".wav", ".mp3" };
        private static readonly string[] LoaderFiles = { "winhttp.dll", "doorstop_config.ini", ".doorstop_version" };
        private static readonly Version Unknown = new Version(0, 0, 0); // installed before versions were recorded

        private static string[] arguments;

        private class Release
        {
            public Version Version;
            public string Url;
            public string Notes;
        }

        [STAThread]
        private static int Main(string[] args)
        {
            arguments = args;
            Ui.Silent = args.Contains("--silent");
            Application.EnableVisualStyles();
            try
            {
                CleanUpOldInstaller();
                if (args.Contains("--uninstall") || Path.GetFileName(Self()).StartsWith("Uninstall", StringComparison.OrdinalIgnoreCase))
                {
                    Uninstall();
                }
                else if (args.Contains("--no-update") || !Update())
                {
                    Install();
                }
                return 0;
            }
            catch (CancelledException)
            {
                return 0; // the player confirmed Cancel: nothing was changed, nothing to say
            }
            catch (UnauthorizedAccessException e)
            {
                Ui.Message("Windows refused to change a file:\n" + e.Message + "\n\nTry running the installer again as administrator.", Ui.Ok | Ui.IconError);
                return 1;
            }
            catch (Exception e) // show the problem rather than vanish silently
            {
                Ui.Message("Setup failed:\n" + e.GetBaseException().Message, Ui.Ok | Ui.IconError);
                return 1;
            }
        }

        private static string Self()
        {
            return Path.GetFullPath(Process.GetCurrentProcess().MainModule.FileName);
        }

        // ---------- Updating the installer itself ----------

        /// <summary>
        /// Offer a newer installer if there is one, and hand over to it if the player says Update. True = it has
        /// taken over. Skip installs the version we carry; the offer comes back the next time the installer is run.
        /// </summary>
        private static bool Update()
        {
            string game = GameArgument() ?? FindGame(); // quietly: never a dialog here
            Version installed = game != null ? InstalledVersion(game) : null;
            Version carried = PayloadVersion();
            if (carried == null)
            {
                return false; // Install() reports it
            }
            Version have = installed != null && installed > carried ? installed : carried;
            Release online = NewestRelease();
            if (online == null || online.Version <= have)
            {
                return false;
            }
            string haveText = installed != null && installed > Unknown ? "You have " + installed + "." : "This installer is " + carried + ".";
            if (Ui.Silent)
            {
                Console.WriteLine("update found: " + online.Version + ". " + haveText + "\n" + online.Notes);
            }
            else if (!Ui.AskUpdate(online.Version.ToString(), online.Notes, haveText))
            {
                return false;
            }
            string target = Path.Combine(Path.GetTempPath(), "FFCAccess Installer " + online.Version + ".exe");
            try
            {
                new Progress("Downloading FFC Access " + online.Version, "Stop downloading the update? Nothing has been changed.")
                    .Run(p => Fetch(online.Url, target + ".part", p));
                File.Delete(target);
                File.Move(target + ".part", target);
                List<string> pass = new List<string> { "--no-update" };
                if (game != null) pass.Add("--game \"" + game.TrimEnd('\\') + "\"");
                if (Ui.Silent) pass.Add("--silent");
                Process.Start(target, string.Join(" ", pass));
                return true;
            }
            catch (CancelledException)
            {
                throw; // stop, nothing changed
            }
            catch (Exception)
            {
                return false; // the download failed: install the version we carry
            }
        }

        /// <summary>Ask GitHub for the project's releases and pick the newest one that has an installer.</summary>
        private static Release NewestRelease()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                string json;
                using (WebClient client = new WebClient())
                {
                    client.Headers[HttpRequestHeader.UserAgent] = "FFCAccess-Installer";
                    client.Encoding = System.Text.Encoding.UTF8;
                    json = client.DownloadString("https://api.github.com/repos/" + Repo + "/releases");
                }
                object[] releases = new JavaScriptSerializer().DeserializeObject(json) as object[];
                Release best = null;
                foreach (Dictionary<string, object> r in (releases ?? new object[0]).OfType<Dictionary<string, object>>())
                {
                    if (r.TryGetValue("draft", out object draft) && draft is bool d && d) continue;
                    Version v = ParseVersion(r["tag_name"] as string ?? "");
                    if (v == null || (best != null && v <= best.Version)) continue;
                    foreach (Dictionary<string, object> a in ((object[])r["assets"]).OfType<Dictionary<string, object>>())
                    {
                        // GitHub turns the space in "FFCAccess Installer.exe" into a dot.
                        string name = a["name"] as string ?? "";
                        if (name.StartsWith("FFCAccess") && name.EndsWith("Installer.exe"))
                        {
                            r.TryGetValue("body", out object body);
                            best = new Release { Version = v, Url = a["browser_download_url"] as string, Notes = (body as string ?? "").TrimStart('\uFEFF') };
                        }
                    }
                }
                return best;
            }
            catch (Exception)
            {
                return null; // no internet, or GitHub is busy: install what we carry
            }
        }

        /// <summary>
        /// Download a file in pieces, reporting each piece to the progress window and stopping at Cancel.
        /// Either way, no half file is left behind.
        /// </summary>
        private static void Fetch(string address, string path, Progress progress)
        {
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(address);
                request.UserAgent = "FFCAccess-Installer";
                using (WebResponse response = request.GetResponse())
                using (Stream source = response.GetResponseStream())
                using (FileStream file = File.Create(path))
                {
                    long total = response.ContentLength, got = 0;
                    byte[] piece = new byte[65536];
                    int n;
                    while ((n = source.Read(piece, 0, piece.Length)) > 0)
                    {
                        progress.Check();
                        file.Write(piece, 0, n);
                        got += n;
                        string megabytes = (got / 1e6).ToString("0.0") + (total > 0 ? " of " + (total / 1e6).ToString("0.0") : "");
                        progress.Update(total > 0 ? (double?)got / total : null, "Downloaded " + megabytes + " MB");
                    }
                }
            }
            catch
            {
                try { File.Delete(path); } catch { }
                throw;
            }
        }

        // ---------- Finding the game ----------

        private static bool IsGameFolder(string folder)
        {
            return !string.IsNullOrEmpty(folder) && File.Exists(Path.Combine(folder, GameExe));
        }

        /// <summary>The folder given on the command line: after --game, or as a bare path (older installers pass it so).</summary>
        private static string GameArgument()
        {
            int at = Array.IndexOf(arguments, "--game");
            if (at >= 0 && at + 1 < arguments.Length)
            {
                return Path.GetFullPath(arguments[at + 1]);
            }
            string bare = arguments.FirstOrDefault(a => !a.StartsWith("--") && IsGameFolder(a));
            return bare != null ? Path.GetFullPath(bare) : null;
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

        /// <summary>Windows' standard "Browse for folder" tree, until the folder chosen holds the game (or Cancel).</summary>
        private static string AskForGame()
        {
            if (Ui.Silent) return null;
            Ui.Message(GameName + " wasn't found through Steam.\n\nNext, choose the game's folder: the one that contains " + GameExe + ".");
            while (true)
            {
                using (FolderBrowserDialog dialog = new FolderBrowserDialog { Description = GameName + " folder", ShowNewFolderButton = false })
                {
                    if (dialog.ShowDialog() != DialogResult.OK) return null;
                    if (IsGameFolder(dialog.SelectedPath)) return dialog.SelectedPath;
                }
                if (Ui.Message(GameExe + " isn't in that folder. Choose again?", Ui.YesNo | Ui.IconWarning) != Ui.IdYes) return null;
            }
        }

        private static bool WaitForGameClosed()
        {
            while (Process.GetProcessesByName(GameProcess).Length > 0)
            {
                if (Ui.Silent) return false;
                if (Ui.Message(GameName + " is running. Please close it, then press OK.\n\nCancel stops the setup.", Ui.OkCancel | Ui.IconWarning) != Ui.IdOk)
                {
                    return false;
                }
            }
            return true;
        }

        // ---------- Versions and the embedded files ----------

        /// <summary>"0.9.0", "v0.9.0" or "FFCAccess-0.9.zip" becomes version 0.9.0 (always three numbers, so they compare).</summary>
        private static Version ParseVersion(string text)
        {
            Match m = Regex.Match(text ?? "", @"(\d+)\.(\d+)(?:\.(\d+))?");
            if (!m.Success) return null;
            return new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0);
        }

        /// <summary>Each install writes version.txt into the plugin folder. Null = not installed; Unknown = an old install without it.</summary>
        private static Version InstalledVersion(string game)
        {
            string plugin = Path.Combine(game, PluginFolder);
            string file = Path.Combine(plugin, "version.txt");
            Version v = File.Exists(file) ? ParseVersion(File.ReadAllText(file)) : null;
            if (v != null) return v;
            return File.Exists(Path.Combine(plugin, "FFCAccess.dll")) ? Unknown : null;
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
                    return ParseVersion(r.ReadToEnd());
                }
            }
        }

        // ---------- Installing ----------

        private static void Install()
        {
            Version carried = PayloadVersion();
            if (carried == null)
            {
                Ui.Message("This installer doesn't contain the mod's files. Please download it again.", Ui.Ok | Ui.IconError);
                return;
            }
            string game = GameArgument() ?? FindGame() ?? AskForGame();
            if (game == null)
            {
                Ui.Message("Setup stopped: the game's folder wasn't chosen.", Ui.Ok | Ui.IconWarning);
                return;
            }
            if (!WaitForGameClosed())
            {
                return;
            }
            Version old = InstalledVersion(game);
            bool updating = old != null, known = updating && old > Unknown;
            if (known && old > carried)
            {
                if (Ui.Message("FFC Access " + old + " is installed, and this installer is " + carried + ", an older version.\n\nInstall " + carried + " anyway?",
                        Ui.YesNo | Ui.IconWarning | Ui.SecondButtonDefault) != Ui.IdYes) return;
            }
            else if (known && old == carried)
            {
                if (Ui.Message("FFC Access " + carried + " is already installed and up to date.\n\nInstall it again?",
                        Ui.YesNo | Ui.IconQuestion | Ui.SecondButtonDefault) != Ui.IdYes) return;
            }
            new Progress("Installing FFC Access " + carried, "Stop installing? Nothing has been changed yet.").Run(p => PutInPlace(p, game));
            Ui.Alert();
            if (updating)
            {
                string done;
                if (!known) done = "Done. Updated FFC Access to " + carried + ".";
                else if (old == carried) done = "Done. FFC Access " + carried + " was installed again.";
                else if (old > carried) done = "Done. FFC Access went back from " + old + " to " + carried + ".";
                else done = "Done. Updated FFC Access from " + old + " to " + carried + ".";
                Ui.Message(done, Ui.Ok); // no icon: the alert above was its sound
                return;
            }
            Ui.Message("Done. FFC Access " + carried + " is installed.\n\n"
                + "Start " + GameName + " from Steam as usual. The mod says \"Fighting Fantasy Classics accessibility loaded\" "
                + "when it's ready; press F1 in the game for help.\n\n"
                + "To update later, run this installer again; a copy is kept in the game's folder.\n"
                + "To remove the mod, run \"" + UninstallerName + "\" in:\n" + game, Ui.Ok);
        }

        /// <summary>
        /// Unpack the mod beside the game's files (in "FFCAccess.new"): the slow part, which Cancel can stop without
        /// harm, because the half-unpacked folder is simply deleted. Then move the new files into place: a moment's
        /// work that Cancel can't break. Progress is counted in bytes, so a big file moves the bar as much as it should.
        ///
        /// Moving into place keeps the player's own BepInEx settings (with the one setting the mod needs switched on)
        /// and backs up edited picture descriptions as .bak. Then it removes files an earlier version installed that
        /// this one doesn't have, using the list each install leaves behind (installed-files.txt).
        /// </summary>
        private static void PutInPlace(Progress progress, string game)
        {
            string gameRoot = Path.GetFullPath(game).TrimEnd('\\') + "\\";
            string staging = Path.Combine(game, StagingFolder);
            List<string> staged = new List<string>();
            DeleteFolder(staging);
            try
            {
                using (ZipArchive archive = OpenPayload())
                {
                    List<ZipArchiveEntry> entries = archive.Entries.Where(e => !e.FullName.EndsWith("/")).ToList();
                    long total = Math.Max(entries.Sum(e => e.Length), 1), copied = 0;
                    byte[] piece = new byte[65536];
                    for (int i = 0; i < entries.Count; i++)
                    {
                        string relative = entries[i].FullName.Replace('/', '\\');
                        string dest = Path.GetFullPath(Path.Combine(staging, relative));
                        if (!Path.GetFullPath(Path.Combine(game, relative)).StartsWith(gameRoot, StringComparison.OrdinalIgnoreCase))
                        {
                            continue; // never write outside the game folder
                        }
                        string status = "Copying files: " + (i + 1) + " of " + entries.Count;
                        Directory.CreateDirectory(Path.GetDirectoryName(dest));
                        using (Stream source = entries[i].Open())
                        using (FileStream file = File.Create(dest))
                        {
                            int n;
                            while ((n = source.Read(piece, 0, piece.Length)) > 0)
                            {
                                file.Write(piece, 0, n);
                                copied += n;
                                progress.Update(0.95 * copied / total, status);
                                progress.Check();
                            }
                        }
                        staged.Add(relative);
                        progress.Update(0.95 * copied / total, status);
                        progress.Check();
                    }
                }
                progress.Finishing("Finishing");
            }
            catch
            {
                DeleteFolder(staging);
                throw;
            }

            string manifest = Path.Combine(game, PluginFolder, "installed-files.txt");
            HashSet<string> before = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(manifest))
            {
                before.UnionWith(File.ReadAllLines(manifest).Where(l => l.Trim().Length > 0));
            }
            HashSet<string> now = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { InstallerName, UninstallerName };
            foreach (string relative in staged)
            {
                string source = Path.Combine(staging, relative), dest = Path.Combine(game, relative);
                now.Add(relative);
                if (relative.Equals(@"BepInEx\config\BepInEx.cfg", StringComparison.OrdinalIgnoreCase) && File.Exists(dest))
                {
                    string cfg = File.ReadAllText(dest);
                    File.WriteAllText(dest, Regex.Replace(cfg, @"HideManagerGameObject = \w+", "HideManagerGameObject = true"));
                    continue;
                }
                if (relative.StartsWith(PluginFolder + @"\descriptions\", StringComparison.OrdinalIgnoreCase) && File.Exists(dest)
                    && File.ReadAllText(source) != File.ReadAllText(dest))
                {
                    File.Copy(dest, dest + ".bak", true);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                File.Copy(source, dest, true);
            }
            DeleteFolder(staging);

            // Files the previous version installed that this one doesn't: remove them, but only the mod's own.
            foreach (string old in before.Except(now, StringComparer.OrdinalIgnoreCase))
            {
                if (!IsModFile(old)) continue;
                string path = Path.GetFullPath(Path.Combine(game, old));
                if (path.StartsWith(gameRoot, StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            File.WriteAllLines(manifest, now.OrderBy(f => f));
            TidyOldSoundsFolder(game);
            CopySelfToGame(game, InstallerName);
            CopySelfToGame(game, UninstallerName);
            progress.Update(1.0, "Done");
        }

        private static void DeleteFolder(string folder)
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
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
        /// FFC Access 0.8 created an "FFCAccess Sounds" folder; this version doesn't use it. Remove it only if it holds
        /// nothing but the help files 0.8 wrote. If the player put sound files in it, leave it exactly as it is.
        /// </summary>
        private static void TidyOldSoundsFolder(string game)
        {
            string old = Path.Combine(game, "FFCAccess Sounds");
            if (!Directory.Exists(old)) return;
            bool hasSounds = Directory.GetFiles(old, "*", SearchOption.AllDirectories)
                .Any(f => AudioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
            if (!hasSounds)
            {
                Directory.Delete(old, true);
            }
        }

        /// <summary>
        /// Keep a copy of this program in the game folder under the given name: as the installer, to update from
        /// there later, and as "Uninstall FFC Access.exe", which removes the mod when run.
        /// </summary>
        private static void CopySelfToGame(string game, string name)
        {
            string self = Self();
            string dest = Path.GetFullPath(Path.Combine(game, name));
            if (self.Equals(dest, StringComparison.OrdinalIgnoreCase)) return;
            if (File.Exists(dest))
            {
                // It may be an older installer that's still running; Windows allows renaming even a running program.
                string old = dest + ".old";
                if (File.Exists(old)) File.Delete(old);
                File.Move(dest, old);
            }
            File.Copy(self, dest);
            try { File.Delete(dest + ".old"); } catch { } // still running: cleaned up the next time
        }

        /// <summary>Remove the copy of the installer left behind by an earlier update.</summary>
        private static void CleanUpOldInstaller()
        {
            try
            {
                string old = Self() + ".old";
                if (File.Exists(old)) File.Delete(old);
            }
            catch
            {
            }
        }

        // ---------- Uninstalling ----------

        /// <summary>
        /// Remove the mod: its plugin folder, its settings, its readme, and the installer copies. BepInEx (the mod
        /// loader the installer brought along) goes too, unless another mod in the game is using it.
        /// </summary>
        private static void Uninstall()
        {
            string here = Path.GetDirectoryName(Self());
            string game = GameArgument() ?? (IsGameFolder(here) ? here : FindGame());
            if (game == null)
            {
                Ui.Message("The game's folder wasn't found, so nothing was removed.", Ui.Ok | Ui.IconWarning);
                return;
            }
            if (Ui.Message("Remove FFC Access from " + GameName + "?\n\nYour FFC Access settings are removed too.", Ui.YesNo | Ui.IconQuestion) != Ui.IdYes)
            {
                return;
            }
            if (!WaitForGameClosed())
            {
                return;
            }
            DeleteFolder(Path.Combine(game, PluginFolder));
            DeleteFolder(Path.Combine(game, StagingFolder));
            foreach (string name in new[] { ModConfig, "FFCAccess-Readme.txt" })
            {
                DeleteFile(Path.Combine(game, name));
            }
            bool othersNeedLoader = HasEntries(Path.Combine(game, "BepInEx", "plugins")) || HasEntries(Path.Combine(game, "BepInEx", "patchers"));
            if (!othersNeedLoader)
            {
                DeleteFolder(Path.Combine(game, "BepInEx"));
                foreach (string name in LoaderFiles)
                {
                    DeleteFile(Path.Combine(game, name));
                }
            }
            Ui.Message("FFC Access has been removed." + (othersNeedLoader
                ? "\n\nBepInEx, the mod loader, was left in place because another mod in this game uses it." : ""));
            // This program may be one of the files to delete: let cmd remove them a moment after we exit.
            string files = string.Join(" ", new[] { InstallerName, UninstallerName, InstallerName + ".old", UninstallerName + ".old" }
                .Select(n => "\"" + Path.Combine(game, n) + "\""));
            Process.Start(new ProcessStartInfo("cmd", "/c ping -n 3 127.0.0.1 >nul & del /q " + files)
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }

        private static void DeleteFile(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private static bool HasEntries(string folder)
        {
            return Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any();
        }
    }
}
