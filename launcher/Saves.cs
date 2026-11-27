// Game saves to share: the title's save folder (data\7952987A\saves, a few hundred KB) exported to a zip on the
// Desktop, or a friend's zip imported over it. The emulated console user is the same on every PC, so a save
// made on one plays on another. Before an import the current saves are kept in a zip of their own
// (%LOCALAPPDATA%\VIctory\save-backups), and nothing is touched while the game runs.
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace VIctoryLauncher
{
    partial class LauncherForm
    {
        const string TitleId = "7952987A";

        string SavesDir { get { return Path.Combine(dataDir, TitleId, "saves"); } }
        string SaveBackups { get { return Path.Combine(Path.GetDirectoryName(dataRoot), "save-backups"); } }

        // The section on the Hardware & ID page; returns the y below it.
        int SavesSection(Control page, int X, int y, int W)
        {
            page.Controls.Add(new Heading(L("Game saves", "Saves do jogo", "Sauvegardes du jeu")) { Location = new Point(X, y), Width = W });
            y += 34;
            var card = new RoundCard { Location = new Point(X, y), Width = W };
            int cy = 16;
            var intro = Kit.Text(L("Share your progress or play a friend's: export makes a zip on your Desktop; import puts a friend's zip in place (your own saves are kept in a backup first).",
                         "Partilha o teu progresso ou joga o de um amigo: exportar cria um zip no Ambiente de trabalho; importar poe o zip de um amigo no lugar (os teus saves ficam primeiro numa copia de seguranca).",
                         "Partage ta progression ou joue celle d'un ami : exporter crée un zip sur ton Bureau ; importer met le zip d'un ami à la place (tes sauvegardes sont d'abord gardées dans une copie de sécurité)."),
                         Theme.Ui(9.25f), Theme.Soft, Theme.Card, 20, cy, W - 40);
            card.Controls.Add(intro);
            cy += intro.Height + 10;
            // What is there now (read again each time the page is shown).
            var mark = new StatusMark { Location = new Point(20, cy + 2), Size = new Size(20, 20), Kind = 3 };
            var status = new Label { Location = new Point(50, cy), Size = new Size(W - 70, 24), Font = Theme.UiBold(9.25f), BackColor = Theme.Card, ForeColor = Theme.Dim, UseMnemonic = false, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
            card.Controls.Add(mark); card.Controls.Add(status);
            Action showSaves = () => {
                DateTime last = DateTime.MinValue;
                try { if (HasFiles(SavesDir)) last = Directory.EnumerateFiles(SavesDir, "*", SearchOption.AllDirectories).Select(f => File.GetLastWriteTime(f)).Max(); } catch (Exception) { }
                if (last == DateTime.MinValue)
                {
                    mark.Set(3, "");
                    status.ForeColor = Theme.Dim;
                    status.Text = L("No save yet: play until the game saves once.", "Ainda nao ha save: joga ate o jogo gravar uma vez.", "Pas encore de sauvegarde : joue jusqu'à ce que le jeu sauvegarde une fois.");
                }
                else
                {
                    mark.Set(1, "");
                    status.ForeColor = Theme.Text;
                    status.Text = string.Format(L("Your save: last written {0}", "O teu save: ultima gravacao {0}", "Ta sauvegarde : dernière écriture {0}"), last.ToString("g"));
                }
            };
            showSaves();
            card.VisibleChanged += (s, e) => { if (card.Visible) showSaves(); };
            cy += 24 + 14;
            var export = new Pill { Text = L("Export my save", "Exportar o meu save", "Exporter ma sauvegarde"), Size = new Size(200, 38) };
            export.Click += (s, e) => ExportSave();
            var import = new Pill { Text = L("Import a save...", "Importar um save...", "Importer une sauvegarde..."), Size = new Size(200, 38) };
            import.Click += (s, e) => ImportSave();
            var open = new Pill { Text = L("Open the saves folder", "Abrir a pasta dos saves", "Ouvrir le dossier"), Size = new Size(200, 38) };
            open.Click += (s, e) => { Directory.CreateDirectory(SavesDir); try { Process.Start("explorer.exe", "\"" + SavesDir + "\""); } catch (Exception) { } };
            var share = new Pill { Text = L("Share in the gallery", "Partilhar na galeria", "Partager dans la galerie"), Size = new Size(200, 38) };
            share.Click += (s, e) => ShareSave();
            var gallery = new Pill { Text = L("Open the save gallery", "Abrir a galeria de saves", "Ouvrir la galerie"), Size = new Size(200, 38) };
            gallery.Click += (s, e) => OpenUrl(GalleryUrl);
            var restore = new Pill { Text = L("Restore a backup...", "Restaurar uma copia...", "Restaurer une copie..."), Size = new Size(200, 38) };
            restore.Click += (s, e) => RestoreBackup();
            cy = Kit.Row(card, 20, cy, W - 40, export, import, open, share, gallery, restore);
            card.Height = cy + 18;
            page.Controls.Add(card);
            return y + card.Height + 16;
        }

        // The public save gallery (server/hardware-worker): a shared save waits until edupode approves it.
        const string GalleryUrl = "https://vi-hardware.edupodept.workers.dev/saves";
        const string GalleryEndpoint = "https://vi-hardware.edupodept.workers.dev/v1/saves";
        const int MaxSaveUpload = 1024 * 1024;

        void ShareSave()
        {
            if (!HasFiles(SavesDir)) { MessageBox.Show(this, L("There is no save yet: play until the game saves once.", "Ainda nao ha save: joga ate o jogo gravar uma vez.", "Pas encore de sauvegarde : joue jusqu'à ce que le jeu sauvegarde une fois."), "Julia's Launcher"); return; }
            string title, note;
            using (var f = Dialog(L("Share in the gallery", "Partilhar na galeria", "Partager dans la galerie"), 580, 400))
            {
                const int pad = 24, fw = 532;
                int y = 20;
                var intro = Kit.Text(L("Your save goes to the public gallery once edupode has approved it. Everyone can download it.",
                             "O teu save vai para a galeria publica depois de o edupode o aprovar. Qualquer pessoa o pode descarregar.",
                             "Ta sauvegarde arrive dans la galerie publique une fois qu'edupode l'a validée. Tout le monde pourra la télécharger."),
                             Theme.Ui(9.5f), Theme.Soft, Theme.Back, pad, y, fw);
                f.Controls.Add(intro);
                y += intro.Height + 14;
                var l1 = Kit.Text(L("Title (where you are in the story)", "Titulo (onde estas na historia)", "Titre (où tu en es dans l'histoire)"), Theme.UiBold(9.5f), Theme.Text, Theme.Back, pad, y, fw);
                f.Controls.Add(l1);
                y += l1.Height + 4;
                var t = new TextBox { MaxLength = 80, ForeColor = Theme.Text, Font = Theme.Ui(10f) };
                Kit.Field(f, t, pad, y, fw);
                y += 36 + 16;
                var l2 = Kit.Text(L("Note (optional)", "Nota (opcional)", "Note (facultatif)"), Theme.UiBold(9.5f), Theme.Text, Theme.Back, pad, y, fw);
                f.Controls.Add(l2);
                y += l2.Height + 4;
                var n = new TextBox { MaxLength = 300, Multiline = true, ForeColor = Theme.Text, Font = Theme.Ui(10f) };
                Kit.Field(f, n, pad, y, fw, 92);
                y += 92 + 12;
                // Why Share does nothing yet: the title needs three letters.
                var hint = Kit.Text(L("The title needs at least 3 letters.", "O titulo precisa de pelo menos 3 letras.", "Le titre doit avoir au moins 3 lettres."), Theme.Ui(9f), Theme.Dim, Theme.Back, pad, y, fw);
                f.Controls.Add(hint);
                y += hint.Height + 12;
                var ok = new Pill { Main = true, Text = L("Share", "Partilhar", "Partager"), Size = new Size(180, 40) };
                var cancel = new Pill { Text = L("Cancel", "Cancelar", "Annuler"), Size = new Size(150, 40) };
                ok.Click += (s, e) => { if (t.Text.Trim().Length >= 3) { f.DialogResult = DialogResult.OK; f.Close(); } else { hint.ForeColor = Theme.Warn; t.Focus(); } };
                cancel.Click += (s, e) => f.Close();
                y = Kit.Row(f, pad, y, fw, ok, cancel);
                f.ClientSize = new Size(580, y + 22);
                if (f.ShowDialog(this) != DialogResult.OK) return;
                title = t.Text.Trim(); note = n.Text.Trim();
            }
            var zip = Path.Combine(Path.GetTempPath(), "vi-save-share.zip");
            try
            {
                if (File.Exists(zip)) File.Delete(zip);
                ZipFile.CreateFromDirectory(SavesDir, zip, CompressionLevel.Optimal, false);
                var data = File.ReadAllBytes(zip);
                if (data.Length > MaxSaveUpload)
                {
                    MessageBox.Show(this, L("This save is too big for the gallery (over 1 MB).", "Este save e grande demais para a galeria (mais de 1 MB).", "Cette sauvegarde est trop grosse pour la galerie (plus de 1 Mo)."), "Julia's Launcher");
                    return;
                }
                System.Net.ServicePointManager.SecurityProtocol |= (System.Net.SecurityProtocolType)3072;
                var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(GalleryEndpoint);
                req.Method = "POST";
                req.ContentType = "application/zip";
                req.UserAgent = "Julias-Launcher";
                req.Timeout = 60000;
                req.Headers["X-VI-Id"] = (hw ?? ReadHardware()).Id;
                req.Headers["X-VI-Title"] = Uri.EscapeDataString(title);
                req.Headers["X-VI-Note"] = Uri.EscapeDataString(note);
                req.ContentLength = data.Length;
                using (var s = req.GetRequestStream()) s.Write(data, 0, data.Length);
                using (var r = (System.Net.HttpWebResponse)req.GetResponse()) { }
                Log(L("Save shared: it shows in the gallery once approved.", "Save partilhado: aparece na galeria depois de aprovado.", "Sauvegarde partagée : elle apparaîtra dans la galerie une fois validée."));
                MessageBox.Show(this, L("Save shared: it shows in the gallery once approved.", "Save partilhado: aparece na galeria depois de aprovado.", "Sauvegarde partagée : elle apparaîtra dans la galerie une fois validée."), "Julia's Launcher");
            }
            catch (System.Net.WebException e)
            {
                var r = e.Response as System.Net.HttpWebResponse;
                MessageBox.Show(this, r != null && (int)r.StatusCode == 429
                    ? L("Too many saves shared this hour: try again later.", "Demasiados saves partilhados nesta hora: tenta mais tarde.", "Trop de sauvegardes partagées cette heure-ci : réessaie plus tard.")
                    : L("The save could not be shared: ", "Nao deu para partilhar o save: ", "Impossible de partager la sauvegarde : ") + e.Message, "Julia's Launcher");
            }
            catch (Exception ex) { MessageBox.Show(this, L("The save could not be shared: ", "Nao deu para partilhar o save: ", "Impossible de partager la sauvegarde : ") + ex.Message, "Julia's Launcher"); }
            finally { try { File.Delete(zip); } catch (Exception) { } }
        }

        static bool HasFiles(string dir) { return Directory.Exists(dir) && Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Any(); }

        // ---------------------------------------------------------------- what a save may hold
        // Exactly the layout the emulator's save module writes (crates\victory-hle\src\modules\xgamesave.rs):
        //   <console user, 16 hex>/<title's GUID>/<container name~16 hex>/v<version>/<0, 1, 2...| manifest>
        // Nothing else is exported or imported: no other name, extension or folder, nothing outside the
        // saves folder, no program or script whatever its name, nothing large. A shared zip with anything
        // more is refused whole, so a save from a stranger cannot bring a virus along (27/11/2026).
        static readonly System.Text.RegularExpressions.Regex SavePath = new System.Text.RegularExpressions.Regex(
            @"^[0-9A-Fa-f]{16}/[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}/[A-Za-z0-9_]{1,64}~[0-9A-Fa-f]{16}/v[0-9]{1,5}/([0-9]{1,4}|manifest)$");
        const string SaveHashes = "vi-save.sha256";
        const long MaxSaveFile = 32L << 20, MaxSaveTotal = 96L << 20;
        const int MaxSaveFiles = 400;

        // Why these bytes cannot be a save file, or null. Programs and scripts are refused by their content,
        // whatever their name: Windows programs (MZ), zips (PK), Linux programs, shortcuts, scripts.
        static string NotSaveBytes(string name, byte[] head, long length)
        {
            if (length > (name.EndsWith("manifest") ? 64 * 1024 : MaxSaveFile)) return "too large";
            if (head.Length >= 2 && head[0] == (byte)'M' && head[1] == (byte)'Z') return "a Windows program";
            if (head.Length >= 2 && head[0] == (byte)'P' && head[1] == (byte)'K') return "an archive";
            if (head.Length >= 4 && head[0] == 0x7F && head[1] == (byte)'E' && head[2] == (byte)'L' && head[3] == (byte)'F') return "a program";
            if (head.Length >= 4 && head[0] == 0x4C && head[1] == 0 && head[2] == 0 && head[3] == 0) return "a shortcut";
            if (head.Length >= 2 && head[0] == (byte)'#' && head[1] == (byte)'!') return "a script";
            var start = System.Text.Encoding.ASCII.GetString(head).TrimStart().ToLowerInvariant();
            foreach (var s in new[] { "@echo", "<script", "<html", "powershell", "function ", "set ws", "createobject(" })
                if (start.StartsWith(s)) return "a script";
            return null;
        }

        static byte[] Head(Stream s, int n)
        {
            var b = new byte[n];
            int got = 0, r;
            while (got < n && (r = s.Read(b, got, n - got)) > 0) got += r;
            Array.Resize(ref b, got);
            return b;
        }

        static string Sha256Of(Stream s)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return string.Concat(sha.ComputeHash(s).Select(x => x.ToString("x2")).ToArray());
        }

        void ExportSave()
        {
            if (!HasFiles(SavesDir)) { MessageBox.Show(this, L("There is no save yet: play until the game saves once.", "Ainda nao ha save: joga ate o jogo gravar uma vez.", "Pas encore de sauvegarde : joue jusqu'à ce que le jeu sauvegarde une fois."), "Julia's Launcher"); return; }
            var zip = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "VI-save-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".zip");
            try
            {
                if (File.Exists(zip)) File.Delete(zip);
                var hashes = new System.Text.StringBuilder();
                int n = 0;
                using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
                {
                    foreach (var f in Directory.EnumerateFiles(SavesDir, "*", SearchOption.AllDirectories))
                    {
                        var rel = f.Substring(SavesDir.Length).TrimStart('\\').Replace('\\', '/');
                        // Only the save module's own files go out (a stray file in the folder stays home).
                        if (!SavePath.IsMatch(rel)) continue;
                        z.CreateEntryFromFile(f, rel, CompressionLevel.Optimal);
                        using (var s = File.OpenRead(f)) hashes.Append(Sha256Of(s)).Append("  ").Append(rel).Append('\n');
                        n++;
                    }
                    var e = z.CreateEntry(SaveHashes);
                    using (var w = new StreamWriter(e.Open())) w.Write(hashes.ToString());
                }
                if (n == 0) { File.Delete(zip); MessageBox.Show(this, L("There is no save yet: play until the game saves once.", "Ainda nao ha save: joga ate o jogo gravar uma vez.", "Pas encore de sauvegarde : joue jusqu'à ce que le jeu sauvegarde une fois."), "Julia's Launcher"); return; }
                Log(L("Save exported: ", "Save exportado: ", "Sauvegarde exportée : ") + zip);
                try { Process.Start("explorer.exe", "/select,\"" + zip + "\""); } catch (Exception) { }
            }
            catch (Exception ex) { MessageBox.Show(this, L("The save could not be exported: ", "Nao deu para exportar o save: ", "Impossible d'exporter la sauvegarde : ") + ex.Message, "Julia's Launcher"); }
        }

        void ImportSave()
        {
            if (State() != GameState.None) { MessageBox.Show(this, L("Close the game first.", "Fecha o jogo primeiro.", "Ferme d'abord le jeu."), "Julia's Launcher"); return; }
            string zip;
            using (var d = new OpenFileDialog { Filter = "Zip (*.zip)|*.zip", Title = L("A save exported by the launcher", "Um save exportado pelo launcher", "Une sauvegarde exportée par le launcher") })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                zip = d.FileName;
            }
            Directory.CreateDirectory(SaveBackups);
            var incoming = Path.Combine(SaveBackups, "incoming-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            try
            {
                // Every entry checked before anything is written where the game reads.
                string refused = null;
                using (var z = ZipFile.OpenRead(zip))
                {
                    var files = z.Entries.Where(e => !e.FullName.EndsWith("/")).ToList();
                    if (files.Count == 0 || files.Count > MaxSaveFiles + 1) refused = L("it holds no save, or far too many files", "nao tem save, ou tem ficheiros a mais", "il ne contient pas de sauvegarde, ou beaucoup trop de fichiers");
                    else if (files.Sum(e => e.Length) > MaxSaveTotal) refused = L("it is far larger than a save", "e muito maior do que um save", "il est bien plus gros qu'une sauvegarde");
                    Dictionary<string, string> listed = null;
                    var hashEntry = files.FirstOrDefault(e => e.FullName == SaveHashes);
                    if (refused == null && hashEntry != null)
                    {
                        listed = new Dictionary<string, string>(StringComparer.Ordinal);
                        using (var r = new StreamReader(hashEntry.Open()))
                            foreach (var line in r.ReadToEnd().Split('\n'))
                            {
                                var parts = line.Trim().Split(new[] { "  " }, 2, StringSplitOptions.None);
                                if (parts.Length == 2) listed[parts[1]] = parts[0];
                            }
                    }
                    var full = Path.GetFullPath(incoming) + Path.DirectorySeparatorChar;
                    foreach (var e in files)
                    {
                        if (refused != null) break;
                        if (e == hashEntry) continue;
                        var rel = e.FullName.Replace('\\', '/');
                        if (!SavePath.IsMatch(rel)) { refused = string.Format(L("\"{0}\" is not a save file", "\"{0}\" nao e um ficheiro de save", "\"{0}\" n'est pas un fichier de sauvegarde"), rel); break; }
                        string why;
                        using (var s = e.Open()) why = NotSaveBytes(rel, Head(s, 64), e.Length);
                        if (why != null) { refused = string.Format(L("\"{0}\" is {1}", "\"{0}\" e {1}", "\"{0}\" est {1}"), rel, why); break; }
                        var to = Path.GetFullPath(Path.Combine(incoming, rel.Replace('/', Path.DirectorySeparatorChar)));
                        if (!to.StartsWith(full, StringComparison.OrdinalIgnoreCase)) { refused = rel; break; }
                        Directory.CreateDirectory(Path.GetDirectoryName(to));
                        e.ExtractToFile(to, false);
                        if (listed != null)
                        {
                            string want, have;
                            using (var s = File.OpenRead(to)) have = Sha256Of(s);
                            if (!listed.TryGetValue(rel, out want) || want != have) { refused = string.Format(L("\"{0}\" was changed after it was exported", "\"{0}\" foi alterado depois de exportado", "\"{0}\" a été modifié après l'export"), rel); break; }
                        }
                    }
                    if (refused == null && !Directory.Exists(incoming)) refused = L("it holds no save", "nao tem save", "il ne contient pas de sauvegarde");
                }
                if (refused != null)
                {
                    try { if (Directory.Exists(incoming)) Directory.Delete(incoming, true); } catch (Exception) { }
                    Log(L("Save refused: ", "Save recusado: ", "Sauvegarde refusée : ") + refused);
                    MessageBox.Show(this, L("This zip was refused, nothing was changed: ", "Este zip foi recusado, nada foi mudado: ", "Ce zip a été refusé, rien n'a changé : ") + refused + ".\r\n\r\n"
                        + L("Only saves exported by Julia's Launcher are taken.", "So sao aceites saves exportados pelo Julia's Launcher.", "Seules les sauvegardes exportées par Julia's Launcher sont acceptées."), "Julia's Launcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                var ask = L("Replace your saves with this one? Yours are kept in a backup first (", "Substituir os teus saves por este? Os teus ficam primeiro numa copia de seguranca (", "Remplacer tes sauvegardes par celle-ci ? Les tiennes sont d'abord gardées dans une copie de sécurité (") + SaveBackups + ").";
                if (MessageBox.Show(this, ask, "Julia's Launcher", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) { Directory.Delete(incoming, true); return; }
                if (HasFiles(SavesDir))
                {
                    var backup = Path.Combine(SaveBackups, "saves-before-import-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip");
                    ZipFile.CreateFromDirectory(SavesDir, backup, CompressionLevel.Optimal, false);
                    Directory.Delete(SavesDir, true);
                    Log(L("Your saves were kept in ", "Os teus saves ficaram em ", "Tes sauvegardes sont gardées dans ") + backup);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(SavesDir));
                Directory.Move(incoming, SavesDir);
                Log(L("Save imported. Press PLAY and load it from the game's menu.", "Save importado. Carrega em JOGAR e abre-o no menu do jogo.", "Sauvegarde importée. Appuie sur PLAY et charge-la depuis le menu du jeu."));
                MessageBox.Show(this, L("Save imported. Press PLAY and load it from the game's menu.", "Save importado. Carrega em JOGAR e abre-o no menu do jogo.", "Sauvegarde importée. Appuie sur PLAY et charge-la depuis le menu du jeu."), "Julia's Launcher");
            }
            catch (Exception ex)
            {
                try { if (Directory.Exists(incoming)) Directory.Delete(incoming, true); } catch (Exception) { }
                MessageBox.Show(this, L("The save could not be imported: ", "Nao deu para importar o save: ", "Impossible d'importer la sauvegarde : ") + ex.Message, "Julia's Launcher");
            }
        }

        // ---------------------------------------------------------------- automatic backups
        // Before every game the saves are copied to save-backups\auto-<time>.zip (a few hundred KB each; the
        // newest 15 are kept), so a save the game breaks, a crash while it writes, or an import gone wrong can
        // be undone from "Restore a backup...". Nothing is written if the saves did not change since the last one.
        const int AutoBackupsKept = 15;

        void BackupSavesBeforePlay()
        {
            try
            {
                if (!HasFiles(SavesDir)) return;
                Directory.CreateDirectory(SaveBackups);
                // Skipped when nothing changed since the newest automatic backup.
                var newest = new DirectoryInfo(SaveBackups).EnumerateFiles("auto-*.zip").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
                var changed = Directory.EnumerateFiles(SavesDir, "*", SearchOption.AllDirectories).Select(File.GetLastWriteTimeUtc).DefaultIfEmpty(DateTime.MinValue).Max();
                if (newest != null && newest.LastWriteTimeUtc >= changed) return;
                var zip = Path.Combine(SaveBackups, "auto-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip");
                ZipFile.CreateFromDirectory(SavesDir, zip, CompressionLevel.Optimal, false);
                foreach (var old in new DirectoryInfo(SaveBackups).EnumerateFiles("auto-*.zip").OrderByDescending(f => f.LastWriteTimeUtc).Skip(AutoBackupsKept))
                    try { old.Delete(); } catch (Exception) { }
            }
            catch (Exception e) { Log(L("Save backup: ", "Copia dos saves: ", "Copie des sauvegardes : ") + e.Message); }
        }

        void RestoreBackup()
        {
            if (State() != GameState.None) { MessageBox.Show(this, L("Close the game first.", "Fecha o jogo primeiro.", "Ferme d'abord le jeu."), "Julia's Launcher"); return; }
            var backups = Directory.Exists(SaveBackups) ? new DirectoryInfo(SaveBackups).EnumerateFiles("*.zip").OrderByDescending(f => f.LastWriteTime).ToList() : new List<FileInfo>();
            if (backups.Count == 0) { MessageBox.Show(this, L("There is no backup yet: one is made before every game.", "Ainda nao ha copias: e feita uma antes de cada jogo.", "Pas encore de copie : une est faite avant chaque partie."), "Julia's Launcher"); return; }
            using (var f = Dialog(L("Restore a backup", "Restaurar uma copia", "Restaurer une copie"), 580, 520))
            {
                const int pad = 24, fw = 532;
                var intro = Kit.Text(L("Your saves as they were before each game (newest first). The current ones are kept in a backup too.",
                             "Os teus saves como estavam antes de cada jogo (os mais recentes primeiro). Os atuais tambem ficam numa copia.",
                             "Tes sauvegardes telles qu'avant chaque partie (les plus récentes d'abord). Les actuelles sont aussi gardées."),
                             Theme.Ui(9.5f), Theme.Soft, Theme.Back, pad, 20, fw);
                f.Controls.Add(intro);
                int ly = 20 + intro.Height + 12;
                var listCard = new RoundCard { Location = new Point(pad, ly), Size = new Size(fw, 330) };
                var list = new ThemedList { Location = new Point(8, 8), Size = new Size(fw - 16, 314) };
                listCard.Controls.Add(list);
                f.Controls.Add(listCard);
                foreach (var b in backups)
                    list.Items.Add(b.LastWriteTime.ToString("g") + "   " + (b.Name.StartsWith("auto-") ? L("before a game", "antes de um jogo", "avant une partie") : L("before an import", "antes de importar", "avant un import")) + "   " + Math.Max(1, b.Length / 1024) + " KB");
                list.SelectedIndex = 0;
                var ok = new Pill { Main = true, Text = L("Restore", "Restaurar", "Restaurer"), Size = new Size(180, 40) };
                var cancel = new Pill { Text = L("Cancel", "Cancelar", "Annuler"), Size = new Size(150, 40) };
                ok.Click += (s, e) => { f.DialogResult = DialogResult.OK; f.Close(); };
                cancel.Click += (s, e) => f.Close();
                int by = Kit.Row(f, pad, listCard.Bottom + 16, fw, ok, cancel);
                f.ClientSize = new Size(580, by + 22);
                if (f.ShowDialog(this) != DialogResult.OK || list.SelectedIndex < 0) return;
                var chosen = backups[list.SelectedIndex].FullName;
                try
                {
                    if (HasFiles(SavesDir))
                    {
                        var keep = Path.Combine(SaveBackups, "saves-before-restore-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip");
                        ZipFile.CreateFromDirectory(SavesDir, keep, CompressionLevel.Optimal, false);
                        Directory.Delete(SavesDir, true);
                    }
                    Directory.CreateDirectory(SavesDir);
                    ZipFile.ExtractToDirectory(chosen, SavesDir);
                    Log(L("Saves restored from ", "Saves restaurados de ", "Sauvegardes restaurées depuis ") + Path.GetFileName(chosen));
                    MessageBox.Show(this, L("Saves restored. Press PLAY and load from the game's menu.", "Saves restaurados. Carrega em JOGAR e abre-os no menu do jogo.", "Sauvegardes restaurées. Appuie sur PLAY et charge-les depuis le menu du jeu."), "Julia's Launcher");
                }
                catch (Exception ex) { MessageBox.Show(this, L("The backup could not be restored: ", "Nao deu para restaurar a copia: ", "Impossible de restaurer la copie : ") + ex.Message, "Julia's Launcher"); }
            }
        }
    }
}
