// Julia's Launcher: older copies of the launcher go away. After an update the replaced program
// ("Julia's Launcher.old.exe") is deleted at the next start; other copies of an older version in Downloads,
// on the Desktop or in Documents (a player had four) are listed and, if the player agrees, moved to the
// Recycle Bin. Settings, shaders and saves are not in those files (they live in %LOCALAPPDATA%\VIctory), so
// nothing of them is lost; launcher.ini is read the same way by every version, and a value this launcher does
// not know is left as it is.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace VIctoryLauncher
{
    partial class LauncherForm
    {
        void CleanupOldLaunchers()
        {
            var me = Path.GetFullPath(Application.ExecutablePath);
            // The program an update replaced.
            try
            {
                var old = Path.Combine(root, "Julia's Launcher.old.exe");
                if (File.Exists(old)) File.Delete(old);
            }
            catch (Exception) { }
            var copies = OlderCopies(me);
            if (copies.Count == 0) return;
            // Asked again only when the list changes.
            var key = string.Join("|", copies.Select(c => c.ToLowerInvariant()).OrderBy(c => c).ToArray()).GetHashCode().ToString("x");
            if (ReadSetting("cleanup_asked") == key) return;
            WriteSetting("cleanup_asked", key);
            var list = string.Join("\r\n", copies.Select(c => "   •  " + Scrub(c)).ToArray());
            var ask = string.Format(L("Older copies of Julia's Launcher are on this PC:\r\n\r\n{0}\r\n\r\nMove them to the Recycle Bin? Your settings, shaders and saves stay (they are not in these files). From now on, use this one: v{1}.",
                                      "Ha copias antigas do Julia's Launcher neste PC:\r\n\r\n{0}\r\n\r\nMove-las para a Reciclagem? As tuas definicoes, shaders e saves ficam (nao estao nestes ficheiros). A partir de agora usa este: v{1}.",
                                      "Il y a d'anciennes copies de Julia's Launcher sur ce PC :\r\n\r\n{0}\r\n\r\nLes mettre à la Corbeille ? Tes réglages, shaders et sauvegardes restent (ils ne sont pas dans ces fichiers). Désormais, utilise celui-ci : v{1}."),
                                      list, Updater.Version);
            if (MessageBox.Show(this, ask, "Julia's Launcher", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            int moved = 0;
            foreach (var c in copies)
            {
                try
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(c, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                    moved++;
                }
                catch (Exception) { }
            }
            Log(string.Format(L("{0} older launcher copies moved to the Recycle Bin.", "{0} copias antigas do launcher movidas para a Reciclagem.", "{0} anciennes copies du launcher mises à la Corbeille."), moved));
        }

        // Copies of the launcher, older than this one, in the places downloads end up. Not this program, not
        // one that is running, not one in a folder that holds a victory.exe of its own (a separate install).
        List<string> OlderCopies(string me)
        {
            var found = new List<string>();
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var places = new[] { Path.Combine(home, "Downloads"), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) };
            var mine = Updater.Number(Updater.Version);
            var running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var p in Process.GetProcesses())
                {
                    try { if (p.ProcessName.IndexOf("Launcher", StringComparison.OrdinalIgnoreCase) >= 0) running.Add(p.MainModule.FileName); } catch (Exception) { }
                    finally { p.Dispose(); }
                }
            }
            catch (Exception) { }
            foreach (var place in places.Distinct())
            {
                try
                {
                    if (!Directory.Exists(place)) continue;
                    foreach (var f in Directory.EnumerateFiles(place, "*.exe", SearchOption.TopDirectoryOnly))
                    {
                        var name = Path.GetFileName(f);
                        if (!(name.StartsWith("Julia", StringComparison.OrdinalIgnoreCase) && name.IndexOf("Launcher", StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                        var full = Path.GetFullPath(f);
                        if (string.Equals(full, me, StringComparison.OrdinalIgnoreCase) || running.Contains(full)) continue;
                        if (File.Exists(Path.Combine(Path.GetDirectoryName(full), "victory.exe"))) continue;
                        var info = FileVersionInfo.GetVersionInfo(full);
                        // Our launchers only: the ones before v0.3.6 carry no product name, later ones do.
                        if (!string.IsNullOrEmpty(info.ProductName) && info.ProductName != "Julia's Launcher") continue;
                        if (Updater.Number(info.FileVersion ?? "0") >= mine) continue;
                        found.Add(full);
                    }
                }
                catch (Exception) { }
            }
            return found;
        }
    }
}
