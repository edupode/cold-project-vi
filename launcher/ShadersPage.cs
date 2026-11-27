// Julia's Launcher: the Shaders page. What the shader cache holds, read again every second while the
// page is open (translated shaders, pipelines made, the ones that failed, disk room, the last errors),
// and the ways to fix it: prepare, repair, rebuild.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace VIctoryLauncher
{
    partial class LauncherForm
    {
        StatusMark[] shaderMarks;
        Label[] shaderTexts;
        TextBox shaderErrors;
        LoadBar shaderBar, pipeBar;
        ToolTip shaderTips = new ToolTip { AutoPopDelay = 20000 };
        // A stage being worked on (0 shaders, 1 pipelines) shows its live progress; else the cache's state.
        readonly bool[] stageLive = new bool[2];
        System.Windows.Forms.Timer shaderTimer;
        volatile bool shaderReading;

        Panel ShadersPage()
        {
            var page = Page();
            const int X = 40, W = 900;
            int y = PageTop(page, L("Shaders", "Shaders", "Shaders"), L("Shaders", "Shaders", "Shaders"), L("kept warm.", "sempre prontos.", "au chaud."), L("The emulator, its shader cache and how healthy both are, live.", "O emulador, a cache de shaders e a saude de ambos, ao vivo.", "L'émulateur, son cache de shaders et leur état, en direct."));
            // The engine card: whether VIctory is built (green or amber), and the two buttons that build and prepare it.
            var engine = new RoundCard { Location = new Point(X, y), Width = W, Height = 76, Flat = true, BackColor = Theme.Back };
            var buildText = L("Build VIctory", "Compilar VIctory", "Compiler VIctory");
            var prepText = L("Prepare shaders", "Preparar shaders", "Préparer les shaders");
            buildButton = new Pill { Text = buildText, Size = new Size(Kit.PillW(buildText, 9.5f, 140), 38) };
            prepareButton = new Pill { Main = true, Text = prepText, Size = new Size(Kit.PillW(prepText, 9.5f, 160), 38) };
            prepareButton.Location = new Point(W - 16 - prepareButton.Width, (engine.Height - 38) / 2);
            buildButton.Location = new Point(prepareButton.Left - 10 - buildButton.Width, prepareButton.Top);
            buildButton.Click += (s, e) => Build();
            prepareButton.Click += (s, e) => Prepare();
            buildStatus = new Label { Location = new Point(18, 10), Size = new Size(buildButton.Left - 18 - 16, 56), ForeColor = Theme.Dim, BackColor = Theme.Back, Font = Theme.Ui(9f), UseMnemonic = false, TextAlign = ContentAlignment.MiddleLeft };
            buildStatus.TextChanged += (s, e) => shaderTips.SetToolTip(buildStatus, buildStatus.Text);
            engine.Controls.Add(buildStatus); engine.Controls.Add(buildButton); engine.Controls.Add(prepareButton);
            page.Controls.Add(engine);
            y += engine.Height + 18;

            page.Controls.Add(new Heading(L("Shader and pipeline health (live)", "Saude dos shaders e pipelines (ao vivo)", "État des shaders et pipelines (en direct)")) { Location = new Point(X, y), Width = W });
            y += 34;
            const int rows = 8;
            shaderMarks = new StatusMark[rows]; shaderTexts = new Label[rows];
            var card = new RoundCard { Location = new Point(X, y), Size = new Size(W, rows * 34 + 20), Flat = true, BackColor = Theme.Back };
            var rowFont = Theme.Ui(9.5f);
            for (int i = 0; i < rows; i++)
            {
                shaderMarks[i] = new StatusMark { Location = new Point(18, 12 + i * 34 + 2), Size = new Size(22, 22), Kind = 4 };
                shaderTexts[i] = new Label { Location = new Point(52, 10 + i * 34), Size = new Size(W - 52 - 18, 26), Font = rowFont, ForeColor = Theme.Text, BackColor = Theme.Back, AutoEllipsis = true, UseMnemonic = false, TextAlign = ContentAlignment.MiddleLeft };
                card.Controls.Add(shaderMarks[i]); card.Controls.Add(shaderTexts[i]);
            }
            // Before the first reading (a second at most): not a blank card.
            shaderTexts[0].Text = L("Reading the cache...", "A ler a cache...", "Lecture du cache...");
            page.Controls.Add(card);
            y += card.Height + 12;

            // The two bars on one card.
            var bars = new RoundCard { Location = new Point(X, y), Size = new Size(W, 112), Flat = true, BackColor = Theme.Back };
            shaderBar = new LoadBar { Location = new Point(18, 14), Size = new Size(W - 36, 36), BackColor = Theme.Back };
            pipeBar = new LoadBar { Location = new Point(18, 62), Size = new Size(W - 36, 36), BackColor = Theme.Back };
            bars.Controls.Add(shaderBar); bars.Controls.Add(pipeBar);
            page.Controls.Add(bars);
            y += bars.Height + 14;

            var repair = new Pill { Text = L("Repair", "Reparar", "Réparer"), Size = new Size(130, 38) };
            repair.Click += (s, e) => RepairShaders(false);
            var rebuild = new Pill { Text = L("Rebuild everything", "Refazer tudo", "Tout refaire"), Size = new Size(180, 38) };
            rebuild.Click += (s, e) => RepairShaders(true);
            var open = new Pill { Text = L("Open the cache folder", "Abrir a pasta da cache", "Ouvrir le dossier du cache"), Size = new Size(200, 38) };
            open.Click += (s, e) => { Directory.CreateDirectory(ActiveCacheDir); OpenUrl(ActiveCacheDir); };
            y = Kit.Row(page, X, y, W, repair, rebuild, open) + 14;
            var note = Kit.Text(L("Repair: makes the missing shaders and retries the pipelines that failed. Rebuild everything: clears the cache and prepares it again (about 5 minutes).",
                         "Reparar: faz os shaders que faltam e tenta de novo as pipelines que falharam. Refazer tudo: limpa a cache e prepara-a de novo (cerca de 5 minutos).",
                         "Réparer : fait les shaders manquants et réessaie les pipelines qui ont échoué. Tout refaire : vide le cache et le prépare à nouveau (environ 5 minutes)."),
                         Theme.Ui(9f), Theme.Dim, Theme.Back, X, y, W);
            page.Controls.Add(note);
            y += note.Height + 20;
            y = StorageSection(page, X, y, W);
            y += 6;
            page.Controls.Add(new Heading(L("Last errors (prepare.log)", "Ultimos erros (prepare.log)", "Dernières erreurs (prepare.log)")) { Location = new Point(X, y), Width = W });
            y += 34;
            shaderErrors = new TextBox { Multiline = true, ReadOnly = true, ForeColor = Theme.Dim, Font = Theme.Mono(8.5f), ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, BackColor = Theme.Back, TabStop = false };
            var errCard = new RoundCard { Location = new Point(X, y), Size = new Size(W, 170), Flat = true, BackColor = Theme.Back };
            shaderErrors.Location = new Point(16, 14);
            shaderErrors.Size = new Size(W - 32, errCard.Height - 28);
            Kit.Dark(shaderErrors, "DarkMode_Explorer");
            errCard.Controls.Add(shaderErrors);
            page.Controls.Add(errCard);
            page.Controls.Add(new Label { Location = new Point(X, y + errCard.Height + 10), Size = new Size(10, 14), BackColor = Theme.Back });

            shaderTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            shaderTimer.Tick += (s, e) => { if (page.Visible) RefreshShaders(); };
            shaderTimer.Start();
            return page;
        }

        // The AMD engine keeps a cache of its own (Hardware.cs, AmdEnvironment).
        string ActiveCacheDir { get { return UseAmdEngine ? Path.Combine(dataRoot, "cache-amd") : cacheDir; } }

        // A stage's live progress on the Shaders page (0 shaders, 1 pipelines); `live` false hands
        // the bar back to the cache's state (RefreshShaders).
        void Stage(int stage, float part, string text, bool live)
        {
            if (InvokeRequired) { try { BeginInvoke(new Action<int, float, string, bool>(Stage), stage, part, text, live); } catch (Exception) { } return; }
            stageLive[stage] = live;
            var bar = stage == 0 ? shaderBar : pipeBar;
            if (bar != null) bar.Set(part, Kit.Clip(text, bar.Font, Math.Max(80, bar.Width - 70)));
        }

        static int Lines(string file)
        {
            try { return File.Exists(file) ? File.ReadLines(file).Count(l => l.Trim().Length > 0) : 0; } catch (Exception) { return 0; }
        }

        void RefreshShaders()
        {
            if (shaderTexts == null || shaderReading) return;
            shaderReading = true;
            var cache = ActiveCacheDir;
            var log = Path.Combine(dataRoot, "logs", "prepare.log");
            new Thread(() => {
                var rows = new List<Tuple<int, string>>();
                string errors = "";
                int shaders = 0, done = 0, bad = 0, used = 0;
                bool prepared = false;
                try
                {
                    long bytes = 0;
                    var sdir = Path.Combine(cache, "shaders");
                    if (Directory.Exists(sdir))
                        foreach (var f in new DirectoryInfo(sdir).EnumerateFiles("*", SearchOption.AllDirectories)) { shaders++; bytes += f.Length; }
                    var pdir = Path.Combine(cache, "pipelines");
                    done = Lines(Path.Combine(pdir, "warm-done.txt")); bad = Lines(Path.Combine(pdir, "warm-bad.txt")); used = Lines(Path.Combine(pdir, "early-pipelines.txt"));
                    long free = -1;
                    try { Directory.CreateDirectory(cache); free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(cache))).AvailableFreeSpace; } catch (Exception) { }
                    prepared = ReadSetting("shaders") == "1";
                    var pathOk = cache.All(c => c < 128);
                    rows.Add(Tuple.Create(shaders >= 1000 ? 1 : 0, string.Format(L("Shaders translated: {0:N0}  ({1:0.0} GB)", "Shaders traduzidos: {0:N0}  ({1:0.0} GB)"), shaders, bytes / 1073741824.0)));
                    rows.Add(Tuple.Create(prepared || shaders >= 1000 ? 1 : 0, prepared ? L("Last preparation finished well", "A ultima preparacao acabou bem")
                        : shaders >= 1000 ? L("Prepared (by another way of starting VIctory, or before this launcher)", "Preparado (por outra forma de arrancar o VIctory, ou antes deste launcher)")
                        : L("Not prepared yet, or not since the last update (press Prepare shaders)", "Ainda nao preparado, ou nao desde o ultimo update (carrega em Preparar shaders)")));
                    rows.Add(Tuple.Create(done > 0 ? 1 : -1, string.Format(L("Pipelines made for the driver: {0:N0}  (used by your sessions: {1:N0})", "Pipelines feitas para o driver: {0:N0}  (usadas nas tuas sessoes: {1:N0})"), done, used)));
                    rows.Add(Tuple.Create(bad == 0 ? 1 : 0, string.Format(L("Pipelines that failed: {0:N0}", "Pipelines que falharam: {0:N0}"), bad) + (bad > 0 ? L("  (Repair tries them again)", "  (Reparar tenta de novo)") : "")));
                    var lastDriver = ReadSetting("shaders_driver") ?? "";
                    rows.Add(Tuple.Create(lastDriver.Length == 0 || lastDriver == driverVersion ? 1 : -1, driverVersion.Length == 0 ? L("Graphics driver: not read", "Driver grafico: nao lido")
                        : lastDriver.Length > 0 && lastDriver != driverVersion ? string.Format(L("Driver changed ({0} → {1}): PLAY remakes the pipelines once", "Driver mudou ({0} → {1}): o JOGAR refaz as pipelines uma vez"), lastDriver, driverVersion)
                        : string.Format(L("Graphics driver {0}", "Driver grafico {0}"), driverVersion)));
                    rows.Add(Tuple.Create(free < 0 ? -1 : free >= (12L << 30) ? 1 : 0, free < 0 ? L("Disk room: not read", "Espaco em disco: nao lido") : string.Format(L("Free on the cache's disk: {0:0.0} GB  (12 GB needed to prepare)", "Livre no disco da cache: {0:0.0} GB  (12 GB para preparar)"), free / 1073741824.0)));
                    rows.Add(Tuple.Create(pathOk ? 1 : 0, pathOk ? L("Cache path is plain (no accents or symbols)", "Caminho da cache simples (sem acentos nem simbolos)") : L("The cache path has accents or symbols: move it (launcher.ini cache=)", "O caminho da cache tem acentos ou simbolos: muda-o (launcher.ini cache=)")));
                    rows.Add(Tuple.Create(-1, L("Cache: ", "Cache: ") + Scrub(cache) + "  ·  " + EngineName));
                    if (File.Exists(log))
                        using (var f = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            if (f.Length > (4L << 20)) f.Seek(-(4L << 20), SeekOrigin.End);
                            var lines = new StreamReader(f).ReadToEnd().Split('\n').Select(s => s.TrimEnd('\r'))
                                .Where(s => Regex.IsMatch(s, @"(?i)\b(error|panicked|failed|denied|not found|no space|do not translate)\b")).Reverse().Take(30).Reverse();
                            errors = string.Join("\r\n", lines.ToArray());
                        }
                }
                catch (Exception) { }
                try
                {
                    BeginInvoke(new Action(() => {
                        for (int i = 0; i < shaderTexts.Length && i < rows.Count; i++)
                        {
                            var st = rows[i].Item1;
                            shaderMarks[i].Set(st == 1 ? 1 : st == 0 ? 0 : 3, "");
                            if (shaderTexts[i].Text != rows[i].Item2) { shaderTexts[i].Text = rows[i].Item2; shaderTips.SetToolTip(shaderTexts[i], rows[i].Item2); }
                        }
                        var t = errors.Length == 0 ? L("(no errors)", "(sem erros)") : Scrub(errors);
                        if (shaderErrors.Text != t) shaderErrors.Text = t;
                        shaderErrors.ForeColor = errors.Length == 0 ? Theme.Dim : Theme.Soft;
                        if (!stageLive[0])
                            shaderBar.Set(shaders >= 1000 || prepared ? 1f : 0f, Kit.Clip(shaders >= 1000 || prepared
                                ? string.Format(L("Shaders: {0:N0} translated, ready", "Shaders: {0:N0} traduzidos, prontos", "Shaders : {0:N0} traduits, prêts"), shaders)
                                : L("Shaders: not prepared yet (press Prepare shaders)", "Shaders: ainda nao preparados (carrega em Preparar shaders)", "Shaders : pas encore préparés (appuie sur Préparer les shaders)"), shaderBar.Font, Math.Max(80, shaderBar.Width - 70)));
                        if (!stageLive[1])
                            pipeBar.Set(used > 0 ? Math.Min(1f, (float)done / used) : done > 0 ? 1f : 0f,
                                Kit.Clip(string.Format(L("Pipelines: {0:N0} made for the driver (your sessions used {1:N0})", "Pipelines: {0:N0} feitas para o driver (as tuas sessoes usaram {1:N0})", "Pipelines : {0:N0} faites pour le pilote (tes sessions en ont utilisé {1:N0})"), done, used), pipeBar.Font, Math.Max(80, pipeBar.Width - 70)));
                        shaderReading = false;
                    }));
                }
                catch (Exception) { shaderReading = false; }
            }) { IsBackground = true }.Start();
        }

        // Repair: the failed pipelines' list goes (they are tried again) and Prepare runs. Rebuild:
        // the whole cache goes first.
        void RepairShaders(bool all)
        {
            if (State() != GameState.None) { MessageBox.Show(this, L("Close the game first.", "Fecha o jogo primeiro."), "Julia's Launcher"); return; }
            var cache = ActiveCacheDir;
            try
            {
                if (all)
                {
                    if (MessageBox.Show(this, L("Delete the shader cache and prepare it again (about 5 minutes)?", "Apagar a cache de shaders e prepara-la de novo (cerca de 5 minutos)?"),
                        "Julia's Launcher", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    foreach (var d in new[] { "shaders", "pipelines" })
                    {
                        var p = Path.Combine(cache, d);
                        if (Directory.Exists(p)) Directory.Delete(p, true);
                    }
                    WriteSetting("shaders", "0");
                    Log(L("Shader cache cleared.", "Cache de shaders limpa."));
                }
                else
                {
                    var bad = Path.Combine(cache, "pipelines", "warm-bad.txt");
                    if (File.Exists(bad)) File.Delete(bad);
                }
            }
            catch (Exception e) { Log(L("Could not clear the cache: ", "Nao deu para limpar a cache: ") + e.Message); return; }
            Prepare();
        }
    }
}

