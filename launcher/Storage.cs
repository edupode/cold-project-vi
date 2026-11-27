// Julia's Launcher: where VIctory keeps its big files. By default under %LOCALAPPDATA%\VIctory on C:; the
// player can pick a folder on another drive (their fastest SSD, one Windows is not on). Everything that grows
// goes there: the shader cache, the game's data and saves, Belobug's AMD engine, and the build output of a
// build from source (CARGO_TARGET_DIR). launcher.ini keeps it as storage=<folder>; the files already made are
// moved with robocopy (Windows' own copier), the game closed.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace VIctoryLauncher
{
    partial class LauncherForm
    {
        // null: the default places (%LOCALAPPDATA%\VIctory).
        string StorageRoot
        {
            get
            {
                var s = ReadSetting("storage");
                return string.IsNullOrEmpty(s) ? null : s;
            }
        }

        // Where cargo puts a build of `folder`: its own target folder, or the storage folder's "build".
        string BuildTarget(string folder)
        {
            var s = StorageRoot;
            return s == null ? Path.Combine(folder, "target") : Path.Combine(s, "build", Path.GetFileName(folder.TrimEnd('\\')));
        }

        Label storageLabel;

        // On the Shaders page, under the cache's buttons.
        int StorageSection(Panel page, int X, int y, int W)
        {
            page.Controls.Add(new Heading(L("Where VIctory keeps its files", "Onde o VIctory guarda os ficheiros", "Où VIctory garde ses fichiers")) { Location = new Point(X, y), Width = W });
            y += 32;
            storageLabel = new Label { Location = new Point(X, y + 6), Size = new Size(W - 230, 40), ForeColor = Theme.Text, BackColor = Theme.Back, Font = Theme.Mono(9f), AutoEllipsis = true };
            page.Controls.Add(storageLabel);
            var change = new Pill { Text = L("Move to another drive...", "Mudar para outro disco...", "Déplacer vers un autre disque..."), Location = new Point(X + W - 220, y), Size = new Size(220, 36) };
            change.Click += (s, e) => MoveStorage();
            page.Controls.Add(change);
            y += 46;
            page.Controls.Add(new Label { Location = new Point(X, y), Size = new Size(W, 36), ForeColor = Theme.Dim, BackColor = Theme.Back, Font = Theme.Ui(9f),
                Text = L("The shader cache (10-20 GB), the game's data and saves, the AMD engine and builds from source. Your fastest SSD is best, even if Windows is not on it.",
                         "A cache de shaders (10-20 GB), os dados e saves do jogo, o motor AMD e as compilacoes a partir do codigo. O teu SSD mais rapido e o melhor, mesmo sem o Windows nele.",
                         "Le cache de shaders (10-20 Go), les données et sauvegardes du jeu, le moteur AMD et les compilations depuis le code. Ton SSD le plus rapide est le mieux, même sans Windows dessus.") });
            y += 42;
            ShowStorage();
            return y;
        }

        void ShowStorage()
        {
            if (storageLabel == null) return;
            var at = StorageRoot ?? Path.GetDirectoryName(dataRoot);
            long free = -1;
            try { free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(at))).AvailableFreeSpace; } catch (Exception) { }
            storageLabel.Text = at + (free >= 0 ? string.Format(L("   ({0:0} GB free)", "   ({0:0} GB livres)", "   ({0:0} Go libres)"), free / 1073741824.0) : "");
        }

        void MoveStorage()
        {
            if (State() != GameState.None || busyNow || setupBusy)
            {
                MessageBox.Show(this, L("Close the game and wait for the current task first.", "Fecha o jogo e espera pela tarefa atual primeiro.", "Ferme le jeu et attends la fin de la tâche en cours."), "Julia's Launcher");
                return;
            }
            string target;
            using (var pick = new FolderBrowserDialog { Description = L("Pick a folder on the drive for VIctory's files (a new folder named VIctory is made in it).", "Escolhe uma pasta no disco para os ficheiros do VIctory (e criada la uma pasta VIctory).", "Choisis un dossier sur le disque pour les fichiers de VIctory (un dossier VIctory y est créé)."), ShowNewFolderButton = true })
            {
                if (pick.ShowDialog(this) != DialogResult.OK) return;
                target = pick.SelectedPath;
            }
            if (!string.Equals(Path.GetFileName(target.TrimEnd('\\')), "VIctory", StringComparison.OrdinalIgnoreCase)) target = Path.Combine(target, "VIctory");
            // The engine and its tools take plain paths only (accented ones broke the cache, 26/11/2026).
            if (!target.All(c => c < 128))
            {
                MessageBox.Show(this, L("Pick a folder whose path has no accents or symbols, for example D:\\Games.", "Escolhe uma pasta cujo caminho nao tenha acentos nem simbolos, por exemplo D:\\Games.", "Choisis un dossier dont le chemin n'a ni accents ni symboles, par exemple D:\\Games."), "Julia's Launcher");
                return;
            }
            var from = new[] {
                Tuple.Create(cacheDir, Path.Combine(target, "cache")),
                Tuple.Create(dataDir, Path.Combine(target, "data")),
                Tuple.Create(Path.Combine(Path.GetDirectoryName(dataRoot), "engines"), Path.Combine(target, "engines")),
            }.Where(t => !string.Equals(Path.GetFullPath(t.Item1).TrimEnd('\\'), Path.GetFullPath(t.Item2).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)).ToList();
            long bytes = 0;
            foreach (var t in from) bytes += FolderBytes(t.Item1);
            long free = -1;
            try { Directory.CreateDirectory(target); free = new DriveInfo(Path.GetPathRoot(target)).AvailableFreeSpace; } catch (Exception e) { MessageBox.Show(this, e.Message, "Julia's Launcher"); return; }
            if (free >= 0 && free < bytes + (2L << 30))
            {
                MessageBox.Show(this, string.Format(L("That drive has {0:0.0} GB free; VIctory's files take {1:0.0} GB. Free some space or pick another drive.", "Esse disco tem {0:0.0} GB livres; os ficheiros do VIctory ocupam {1:0.0} GB. Liberta espaco ou escolhe outro disco.", "Ce disque a {0:0.0} Go libres ; les fichiers de VIctory font {1:0.0} Go. Libère de la place ou choisis un autre disque."), free / 1073741824.0, bytes / 1073741824.0), "Julia's Launcher");
                return;
            }
            if (MessageBox.Show(this, string.Format(L("Move VIctory's files ({0:0.0} GB: shader cache, game data and saves, AMD engine) to {1}?\r\n\r\nYour saves are copied first and removed from the old place only when the copy is complete.",
                                                       "Mover os ficheiros do VIctory ({0:0.0} GB: cache de shaders, dados e saves do jogo, motor AMD) para {1}?\r\n\r\nOs saves sao copiados primeiro e so saem do sitio antigo quando a copia estiver completa.",
                                                       "Déplacer les fichiers de VIctory ({0:0.0} Go : cache de shaders, données et sauvegardes du jeu, moteur AMD) vers {1} ?\r\n\r\nLes sauvegardes sont d'abord copiées et ne quittent l'ancien emplacement qu'une fois la copie terminée."), bytes / 1073741824.0, target),
                "Julia's Launcher", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            Busy(true);
            new Thread(() => {
                bool ok = true;
                var movedAll = new List<string>();
                try
                {
                foreach (var t in from)
                {
                    if (!Directory.Exists(t.Item1)) continue;
                    Progress(string.Format(L("Moving {0}...", "A mover {0}...", "Déplacement de {0}..."), Path.GetFileName(t.Item2)), 0.1f + 0.8f * from.IndexOf(t) / Math.Max(1, from.Count));
                    // /MOVE: each file leaves the old place once it is copied; a code of 8 or more is a failure.
                    int code = RunWait("robocopy", "\"" + t.Item1.TrimEnd('\\') + "\" \"" + t.Item2 + "\" /E /MOVE /R:1 /W:1 /NP /NFL /NDL /MT:8", false);
                    if (code >= 8) { ok = false; SafeLog(string.Format(L("Moving {0} stopped (robocopy code {1}); what was not moved is still in {2}.", "A mudanca de {0} parou (codigo {1} do robocopy); o que nao foi movido continua em {2}.", "Le déplacement de {0} s'est arrêté (code robocopy {1}) ; ce qui n'a pas été déplacé est encore dans {2}."), t.Item1, code, t.Item1)); break; }
                    movedAll.Add(t.Item1);
                }
                }
                catch (Exception ex) { ok = false; SafeLog(ex.Message); }
                try
                {
                    BeginInvoke(new Action(() => {
                        // A folder already moved whole when a later one failed: the settings follow it, so the launcher
                        // does not look for it (empty) in the old place.
                        if (!ok)
                        {
                            if (movedAll.Contains(cacheDir)) { WriteSetting("cache", Path.Combine(target, "cache")); cacheDir = Path.Combine(target, "cache"); }
                            if (movedAll.Contains(dataDir)) { WriteSetting("data", Path.Combine(target, "data")); dataDir = Path.Combine(target, "data"); }
                        }
                        if (ok)
                        {
                            WriteSetting("storage", target);
                            WriteSetting("cache", Path.Combine(target, "cache"));
                            WriteSetting("data", Path.Combine(target, "data"));
                            cacheDir = Path.Combine(target, "cache");
                            dataDir = Path.Combine(target, "data");
                            Log(L("VIctory's files are now in ", "Os ficheiros do VIctory estao agora em ", "Les fichiers de VIctory sont maintenant dans ") + target);
                        }
                        Progress("", 0f);
                        Busy(false);
                        ShowStorage(); Refresh2(); RefreshShaders();
                    }));
                }
                catch (Exception)
                {
                    // The window is gone: the move is still finished on disk, so the settings are written anyway.
                    if (ok) { WriteSetting("storage", target); WriteSetting("cache", Path.Combine(target, "cache")); WriteSetting("data", Path.Combine(target, "data")); }
                }
            }) { IsBackground = true }.Start();
        }

        static long FolderBytes(string dir)
        {
            long n = 0;
            try
            {
                if (!Directory.Exists(dir)) return 0;
                foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories))
                    try { n += f.Length; } catch (Exception) { }
            }
            catch (Exception) { }
            return n;
        }
    }
}
