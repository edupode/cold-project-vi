// Julia's Launcher (edupode): one window to set up and play the edupode build of VIctory.
// Built with the C# compiler that ships with Windows (.NET Framework 4.x):
//   launcher\build-launcher.cmd   ->   "Julia's Launcher.exe" in the repository's root.
// No game files are included or downloaded; the player points it at their own dump.
// English by default; the buttons in the side bar switch to Portuguese or French (saved in launcher.ini).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace VIctoryLauncher
{
    static class Program
    {
        public static readonly string ErrorFile = Path.Combine(Path.GetTempPath(), "vi-launcher-error.txt");

        [STAThread]
        static int Main(string[] args)
        {
            // What ends the launcher is written down (%TEMP%\vi-launcher-error.txt) for a bug report.
            AppDomain.CurrentDomain.UnhandledException += (s, e) => {
                try { File.AppendAllText(ErrorFile, DateTime.Now + "  " + e.ExceptionObject + Environment.NewLine); } catch (Exception) { }
            };
            Application.ThreadException += (s, e) => {
                try { File.AppendAllText(ErrorFile, DateTime.Now + "  " + e.Exception + Environment.NewLine); } catch (Exception) { }
                MessageBox.Show("Julia's Launcher: " + e.Exception.Message + "\r\n\r\n(Help > Send errors to Discord)", "Julia's Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            // The installer's one administrator step (installer\VI.iss): Windows settings, then out.
            if (args.Contains("--admin-setup")) return AdminTasks.Run(args);
            // The Setup page's virtual memory step, run as administrator (no PowerShell).
            if (args.Contains("--pagefile")) return LauncherForm.SetManagedPagefile() ? 0 : 1;
            // The Setup page's "Resizable BAR in the NVIDIA driver", run as administrator (the driver asks for it).
            if (args.Contains("--rebar-profile")) return SmoothMotion.SetRebar(true) == null ? 0 : 1;
            // After an update: the launcher it replaced ends first (it still holds the one-launcher lock).
            int wait = Array.IndexOf(args, "--wait-pid");
            int pid;
            if (wait >= 0 && wait + 1 < args.Length && int.TryParse(args[wait + 1], out pid))
                try { Process.GetProcessById(pid).WaitForExit(15000); } catch (Exception) { }
            // One launcher at a time: a second start brings the first to the front.
            bool first;
            using (var one = new Mutex(true, "VI-Launcher-edupode", out first))
            {
                if (!first)
                {
                    foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Application.ExecutablePath)))
                        try { if (p.Id != Process.GetCurrentProcess().Id && p.MainWindowHandle != IntPtr.Zero) SetForegroundWindow(p.MainWindowHandle); } catch (Exception) { }
                    return 0;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.Run(new LauncherForm());
            }
            return 0;
        }

        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    }

    // A preset: render scale and the switches it turns on.
    class Mode
    {
        public string Name, Short, Tag, Description;
        public string Scale;      // null: native 1080p
        public bool LightTextures;
        public bool Modest;       // smaller textures and buffers for 6-8 GB cards and 16 GB of RAM
        public bool UltraLow;     // the least memory: 4-6 GB cards, 8-16 GB of RAM
        public bool Custom;       // the player's own values (Advanced Tweaks)
        public Mode(string name, string tag, string shortLine, string description, string scale, bool light, bool modest)
        {
            Name = name; Tag = tag; Short = shortLine; Description = description; Scale = scale; LightTextures = light; Modest = modest;
        }
        public override string ToString() { return Name + " (" + Tag + ")"; }
    }

    // An app that holds video memory or RAM, all its processes together.
    class MemoryUser
    {
        public string Name;
        public long Vram;
        public long Ram;
        public bool Protected;
        public string Note = "";
    }

    partial class LauncherForm : Form
    {
        static bool Pt, Fr;
        // French comes from French.cs (looked up by the English text), or from fr when a call passes it.
        static string L(string en, string pt, string fr = null) { return Fr ? (fr ?? Fr_(en)) : Pt ? pt : en; }

        // Opens the launcher again (a new language). Application.Restart started the new one while this one
        // still held the one-launcher lock, so the new one saw "already running" and quit: nothing reopened.
        // --wait-pid makes it wait until this one has ended.
        static void RestartLauncher()
        {
            try { Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--wait-pid " + Process.GetCurrentProcess().Id) { UseShellExecute = false }); }
            catch (Exception) { }
            Application.Exit();
        }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        readonly string root = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        readonly string dataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VIctory", "luz");
        readonly string settingsFile;
        // Where the shader cache and the game's saves live; launcher.ini's cache= and data= move them
        // (to share them with another way of starting VIctory).
        string cacheDir, dataDir;

        Banner banner;
        StatusStrip statusStrip;
        TextBox gameBox;
        Label gameStatus, buildStatus, modeInfo, vramSummary, smoothStatus, footerStatus, footerState, warnText, updateBadge;
        Panel warnPanel;
        Pill warnFix;
        Heading presetHeading, gameHeading;
        readonly List<ModeCard> cards = new List<ModeCard>();
        int modeIndex = -1;
        CheckBox panelBox, borderlessBox, fps60Box, reserveBox, smoothBox, qualityBox, facesBox, loadingBox, fightsBox, warmBox, cpuBox;
        LoadBar loadBar;
        TextBox logBox;
        Pill playButton, prepareButton, buildButton, closeButton;
        ListView vramList;
        readonly List<Panel> pages = new List<Panel>();
        readonly List<NavButton> nav = new List<NavButton>();

        bool rebar, rtxFrameGen;
        // Resizable BAR as the card reports it (nvidia-smi's BAR1 of 1 GB and more) and as D3D12
        // offers it to a run (`victory gpu`: GPU upload heaps; -1 not asked yet).
        bool rebarBios;
        // nvidia-smi's BAR1 was read at all; D3D12 knows GPU upload heaps (Windows 11 24H2 and newer).
        bool barRead, probeOptions16;
        int rebarHeap = -1;
        string probeAdapter = "", probeVendor = "";
        double vramGb, ramGb;
        string gpuName = "unknown GPU";
        string smoothState = "?";

        Mode[] modes;
        // Pages by their place in `pages`; the side bar shows the first eight.
        const int PagePlay = 0, PagePerf = 1, PageShaders = 2, PageControls = 3, PageSetup = 4, PageHardware = 5, PageUpdates = 6, PageHelp = 7, PageCustom = 8;
        // The order the preset cards show the modes in (modes keep their numbers in launcher.ini).
        static readonly int[] CardOrder = { 0, 1, 2, 5, 3, 4, 6 };
        const int ModeUltraLow = 5, ModeCustom = 6;

        public const string DiscordInvite = "https://discord.gg/648WEXCAsZ";
        public const string DiscordChannel = "Send bugs here please";
        public const string RepoUrl = "https://github.com/felicienradio/cold-project-vi/tree/main";

        public LauncherForm()
        {
            settingsFile = Path.Combine(Path.GetDirectoryName(dataRoot), "launcher.ini");
            Pt = ReadSetting("lang") == "pt";
            Fr = ReadSetting("lang") == "fr";
            cacheDir = ReadSetting("cache");
            if (cacheDir == null || (!cacheDir.All(c => c < 128) && cacheDir == Path.Combine(dataRoot, "cache"))) cacheDir = DefaultCacheDir();
            dataDir = ReadSetting("data") ?? Path.Combine(dataRoot, "data");
            modes = new Mode[] {
                new Mode(L("Recommended", "Recomendado"), "720p → 1080p", L("Balanced · edupode", "Equilibrado · edupode"),
                    L("The world drawn at 1280x720 and rebuilt to 1080p by the game's own upscaler, texture levels held (video memory under budget), 60 FPS clock. About 22-26 FPS in the open world and 35+ in cutscenes on an RTX 5070 Ti laptop (6-8 GB of video memory, ~20 GB of RAM).",
                      "O mundo desenhado a 1280x720 e reconstruido para 1080p pelo upscaler do proprio jogo, niveis de texturas guardados (VRAM dentro do limite), relogio de 60 FPS. Cerca de 22-26 FPS no mundo aberto e 35+ nas cutscenes num portatil RTX 5070 Ti (6-8 GB de VRAM, ~20 GB de RAM)."),
                    "0.6666667", true, false),
                new Mode(L("Fastest", "Mais rapido"), "540p", L("Peak FPS", "Mais FPS"),
                    L("The world drawn at 960x540: the most frames, a softer picture. For slower CPUs.",
                      "O mundo desenhado a 960x540: mais frames, imagem mais suave. Para processadores mais lentos."),
                    "0.5", true, false),
                new Mode(L("Modest PC", "PC modesto"), "540p", L("6-8 GB GPUs", "Placas 6-8 GB"),
                    L("For 6-8 GB cards and 16 GB of RAM: 540p, textures over 1024 texels halved, fewer of the game's buffers on the card, smaller texture batches, the memory guards on. Less video memory and RAM, a softer picture.",
                      "Para placas de 6-8 GB e 16 GB de RAM: 540p, texturas acima de 1024 cortadas a metade, menos buffers do jogo na placa, lotes de texturas mais pequenos, protecoes de memoria ligadas. Menos VRAM e RAM, imagem mais suave."),
                    "0.5", true, true),
                new Mode(L("Sharp", "Nitido"), "1080p", L("12 GB+ GPUs", "Placas 12 GB+"),
                    L("The world at full 1080p with held texture levels. Needs a strong GPU with 12 GB or more; slower in the open world.",
                      "O mundo a 1080p completos com niveis de texturas guardados. Precisa de uma placa forte com 12 GB ou mais; mais lento no mundo aberto."),
                    null, true, false),
                new Mode(L("Classic cgbj", "cgbj classico"), "1080p", L("Reference", "Referencia"),
                    L("The original cgbj settings at 1080p, no texture changes. For comparison.",
                      "As definicoes originais do cgbj a 1080p, sem mudancas nas texturas. Para comparar."),
                    null, false, false),
                new Mode(L("Ultra Low", "Ultra baixo"), "540p", L("4-6 GB, 8-16 GB RAM", "4-6 GB, 8-16 GB RAM"),
                    L("For the weakest PCs (4-6 GB cards, 8-16 GB of RAM): 540p, textures capped at 512 texels, the fewest buffers on the card, small texture batches, the memory guards on and a 30 FPS clock. The least memory of all; a soft picture. Give Windows a large page file (Setup).",
                      "Para os PCs mais fracos (placas de 4-6 GB, 8-16 GB de RAM): 540p, texturas ate 512 texels, o minimo de buffers na placa, lotes de texturas pequenos, protecoes de memoria ligadas e relogio de 30 FPS. A menor memoria de todas; imagem suave. Da ao Windows um ficheiro de paginacao grande (Preparar)."),
                    "0.5", true, true) { UltraLow = true },
                new Mode(L("Custom", "Personalizado"), L("your values", "os teus valores"), L("Advanced Tweaks", "Ajustes avancados"),
                    L("Your own values from Advanced Tweaks: resolution, texture size, buffers, memory guards, FPS clock. Copy them as a code to share, or paste a friend's.",
                      "Os teus proprios valores dos Ajustes avancados: resolucao, tamanho das texturas, buffers, protecoes de memoria, relogio de FPS. Copia-os como um codigo para partilhar, ou cola o de um amigo."),
                    null, true, false) { Custom = true },
            };

            Text = "Julia's Launcher";
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }
            ClientSize = new Size(1200, 900);
            MinimumSize = new Size(1080, 720);
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            Font = Theme.Ui(9.75f);
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;

            var main = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Back };
            var content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Back };
            main.Controls.Add(content);
            main.Controls.Add(Footer());
            pages.Add(PlayPage());
            pages.Add(MemoryPage());
            pages.Add(ShadersPage());
            pages.Add(ControlsPage());
            pages.Add(SetupPage());
            pages.Add(HardwarePage());
            pages.Add(UpdatesPage());
            pages.Add(HelpPage());
            pages.Add(CustomPage());
            foreach (var p in pages) { p.Dock = DockStyle.Fill; p.Visible = false; content.Controls.Add(p); }
            Controls.Add(main);
            Controls.Add(SideBar());
            Controls.Add(TopBar());

            LoadSettings();
            DetectSystem();
            Show(PagePlay);
            Refresh2();
            // A first run (nothing built or no game yet): the Setup page, checked.
            if (VictoryExe == null || !File.Exists(GameExe)) Show(PageSetup);
            // `--page N` opens on that page (0 Play, 1 Performance, 2 Shaders, 3 Controls, 4 Setup,
            // 5 Hardware, 6 Updates, 7 Help, 8 Advanced Tweaks).
            var argv = Environment.GetCommandLineArgs();
            int page;
            for (int a = 1; a + 1 < argv.Length; a++)
                if (argv[a] == "--page" && int.TryParse(argv[a + 1], out page) && page >= 0 && page < pages.Count) Show(page);
        }

        // Work on other threads that reports back to the window starts once the window exists.
        ToolTip tips;

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // PLAY follows the game and the tasks on its own (a game closed, a build done).
            var timer = new System.Windows.Forms.Timer { Interval = 2000 };
            timer.Tick += (s, a) => { if (!setupBusy) Refresh2(); };
            timer.Start();
            // No game folder yet: look for the game on its own.
            if (!File.Exists(GameExe)) RunSteps(steps.Where(st => st.Fix == (Func<bool>)FindGame).ToArray());
            else CheckAll();
            new Thread(() => {
                var s = SmoothMotion.Status();
                try { BeginInvoke(new Action(() => { smoothState = s; UpdateBanner(); UpdateSmooth(); })); } catch (Exception) { }
            }) { IsBackground = true }.Start();
            ProbeGpu();
            // Windows 10 needs the D3D12 Agility SDK beside victory.exe (Agility.cs).
            EnsureAgility();
            StartChecks();
            // Once the window is up: the notices of new things to try (Hardware.cs).
            var later = new System.Windows.Forms.Timer { Interval = 1500 };
            // First what changed in this version (once), then older copies of the launcher to remove, then the notices.
            later.Tick += (s, a) => { later.Stop(); later.Dispose(); ShowWhatsNewOnce(); CleanupOldLaunchers(); if (!setupBusy) ShowNotices(); };
            later.Start();
            StartUpdates();
            StartHardwareShare();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int on = 1;
            try { if (DwmSetWindowAttribute(Handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(Handle, 19, ref on, 4); } catch (Exception) { }
        }

        // ---------------------------------------------------------------- top bar and side bar
        Control TopBar()
        {
            var bar = new Panel { Dock = DockStyle.Top, Height = 38, BackColor = Theme.Side };
            bar.Paint += (s, e) => {
                var g = e.Graphics;
                Theme.Smooth(g);
                using (var f = new Font("Segoe UI Symbol", 11f))
                using (var b = new SolidBrush(Theme.Text)) g.DrawString("❄", f, b, 14, 8);
                float x = 38;
                using (var f = Theme.UiBold(10f))
                using (var b = new SolidBrush(Theme.Text)) { g.DrawString("Cold Project VI", f, b, x, 9); x += g.MeasureString("Cold Project VI", f).Width + 6; }
                using (var f = Theme.Mono(8.5f))
                using (var b = new SolidBrush(Theme.Dim)) g.DrawString("v" + Updater.Version, f, b, x, 12);
                using (var p = new Pen(Theme.Line)) g.DrawLine(p, 0, bar.Height - 1, bar.Width, bar.Height - 1);
            };
            Func<string, string, Label> link = (text, url) => {
                var l = new Label { Text = text, AutoSize = true, ForeColor = Theme.Dim, BackColor = Theme.Side, Font = Theme.Ui(9f), Cursor = Cursors.Hand, Anchor = AnchorStyles.Top | AnchorStyles.Right };
                l.Click += (s, e) => OpenUrl(url);
                l.MouseEnter += (s, e) => l.ForeColor = Theme.Text;
                l.MouseLeave += (s, e) => l.ForeColor = Theme.Dim;
                bar.Controls.Add(l);
                return l;
            };
            var site = link("↗ Site", RepoUrl);
            var discord = link("↗ Discord", DiscordInvite);
            updateBadge = new Label { Text = "", AutoSize = true, ForeColor = Theme.Good, BackColor = Theme.Side, Font = Theme.Mono(8.5f), Cursor = Cursors.Hand, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            updateBadge.Click += (s, e) => Show(PageUpdates);
            bar.Controls.Add(updateBadge);
            bar.Resize += (s, e) => {
                site.Location = new Point(bar.Width - site.Width - 16, 11);
                discord.Location = new Point(site.Left - discord.Width - 14, 11);
                updateBadge.Location = new Point(discord.Left - updateBadge.Width - 18, 12);
            };
            return bar;
        }

        Control SideBar()
        {
            var side = new Panel { Dock = DockStyle.Left, Width = 216, BackColor = Theme.Side };
            side.Paint += (s, e) => { using (var p = new Pen(Theme.Line)) e.Graphics.DrawLine(p, side.Width - 1, 0, side.Width - 1, side.Height); };
            string[][] items = {
                new[] { "", L("Play", "Jogar") },
                new[] { "", L("Performance", "Desempenho") },
                new[] { "", L("Shaders", "Shaders") },
                new[] { "", L("Controls", "Controlos") },
                new[] { "", L("Setup", "Preparar") },
                new[] { "", L("Hardware & ID", "Hardware e ID") },
                new[] { "", L("Updates", "Atualizacoes") },
                new[] { "", L("Help", "Ajuda") },
            };
            for (int i = 0; i < items.Length; i++)
            {
                int k = i;
                var b = new NavButton { Glyph = items[i][0], Text = items[i][1], Location = new Point(6, 16 + i * 42) };
                b.Click += (s, e) => Show(k);
                nav.Add(b);
                side.Controls.Add(b);
            }
            var report = new Pill { Text = L("Send errors (GitHub / Discord)", "Enviar erros (GitHub / Discord)"), Size = new Size(184, 34), Location = new Point(16, 0), BackColor = Theme.Side, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            report.Click += (s, e) => ReportErrors();
            side.Controls.Add(report);
            var en = new Pill { Text = "EN", Size = new Size(58, 30), BackColor = Theme.Side, Anchor = AnchorStyles.Bottom | AnchorStyles.Left, Main = !Pt && !Fr };
            var pt = new Pill { Text = "PT", Size = new Size(58, 30), BackColor = Theme.Side, Anchor = AnchorStyles.Bottom | AnchorStyles.Left, Main = Pt };
            var fr = new Pill { Text = "FR", Size = new Size(58, 30), BackColor = Theme.Side, Anchor = AnchorStyles.Bottom | AnchorStyles.Left, Main = Fr };
            en.Click += (s, e) => { if (Pt || Fr) { Pt = false; Fr = false; SaveSettings(); RestartLauncher(); } };
            pt.Click += (s, e) => { if (!Pt) { Pt = true; Fr = false; SaveSettings(); RestartLauncher(); } };
            fr.Click += (s, e) => { if (!Fr) { Fr = true; Pt = false; SaveSettings(); RestartLauncher(); } };
            side.Controls.Add(en); side.Controls.Add(pt); side.Controls.Add(fr);
            var tech = new Label { Text = "D3D12 · SM 6.6", Font = Theme.Mono(8f), ForeColor = Theme.Dim, AutoSize = true, BackColor = Theme.Side, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            side.Controls.Add(tech);
            side.Resize += (s, e) => {
                report.Top = side.Height - 124;
                en.Location = new Point(16, side.Height - 80); pt.Location = new Point(79, side.Height - 80); fr.Location = new Point(142, side.Height - 80);
                tech.Location = new Point((side.Width - tech.Width) / 2, side.Height - 36);
            };
            return side;
        }

        void Show(int page)
        {
            for (int i = 0; i < pages.Count; i++) pages[i].Visible = i == page;
            for (int i = 0; i < nav.Count; i++) nav[i].Active = i == page || (page == PageCustom && i == PagePlay);
            if (page == PageSetup) CheckAll();
            if (page == PagePerf) ScanMemory();
            if (page == PageShaders) RefreshShaders();
            if (page == PageHardware) RefreshHardware();
            if (page == PageUpdates) RefreshUpdates();
            if (page == PageCustom) RefreshCustom();
        }

        Panel Page()
        {
            return new Panel { BackColor = Theme.Back, AutoScroll = true, Padding = new Padding(24, 20, 24, 20) };
        }

        Label Small(string text, int x, int y, int width)
        {
            return new Label { Text = text, Location = new Point(x, y), Size = new Size(width, 20), ForeColor = Theme.Dim, BackColor = Theme.Back };
        }

        // ---------------------------------------------------------------- footer: state and PLAY
        Control Footer()
        {
            var foot = new Panel { Dock = DockStyle.Bottom, Height = 76, BackColor = Theme.Back };
            var box = new Panel { BackColor = Theme.Card, Location = new Point(28, 14), Height = 48 };
            box.Paint += (s, e) => { using (var p = new Pen(Theme.Line)) e.Graphics.DrawRectangle(p, 0, 0, box.Width - 1, box.Height - 1); };
            footerState = new Label { Location = new Point(14, 0), Size = new Size(96, 48), TextAlign = ContentAlignment.MiddleLeft, Font = Theme.Mono(9f), ForeColor = Theme.Good, BackColor = Theme.Card };
            footerStatus = new Label { Location = new Point(110, 0), Height = 48, TextAlign = ContentAlignment.MiddleLeft, Font = Theme.Mono(9f), ForeColor = Theme.Text, BackColor = Theme.Card, AutoEllipsis = true };
            box.Controls.Add(footerState); box.Controls.Add(footerStatus);
            foot.Controls.Add(box);
            closeButton = new Pill { Text = L("Close game", "Fechar o jogo"), Size = new Size(158, 48), Visible = false };
            closeButton.Click += (s, e) => {
                if (State() == GameState.Running && MessageBox.Show(this, L("Close the game now? What was not saved is lost.", "Fechar o jogo agora? O que nao foi guardado perde-se."),
                    "Julia's Launcher", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                CloseGames();
                Refresh2();
            };
            foot.Controls.Add(closeButton);
            playButton = new Pill { Main = true, Text = L("▶  PLAY", "▶  JOGAR"), Size = new Size(210, 48), Font = new Font("Segoe UI Semibold", 12.5f) };
            playButton.Click += (s, e) => Play();
            foot.Controls.Add(playButton);
            foot.Paint += (s, e) => { using (var p = new Pen(Theme.Line)) e.Graphics.DrawLine(p, 0, 0, foot.Width, 0); };
            foot.Resize += (s, e) => {
                playButton.Location = new Point(foot.Width - 28 - playButton.Width, 14);
                closeButton.Location = new Point(playButton.Left - 10 - closeButton.Width, 14);
                box.Width = Math.Max(200, (closeButton.Visible ? closeButton.Left : playButton.Left) - 14 - box.Left);
                footerStatus.Width = box.Width - footerStatus.Left - 10;
            };
            closeButton.VisibleChanged += (s, e) => { var w = foot.Width; foot.Width = w + 1; foot.Width = w; };
            return foot;
        }

        // ---------------------------------------------------------------- Play page
        Panel PlayPage()
        {
            var page = Page();
            const int X = 28, W = 900;
            int y = 18;
            banner = new Banner { Location = new Point(X, y), Size = new Size(W, 110),
                Title = L("Xbox Series-to-Windows compatibility runtime · ", "Runtime de compatibilidade Xbox Series para Windows · ramo "),
                Accent = "edupode", Subtitle = L(" branch", "") };
            page.Controls.Add(banner);
            y += 124;

            // The checks in one strip, with Auto-Setup and Configure.
            statusStrip = new StatusStrip { Location = new Point(X, y), Size = new Size(W, 52), BackColor = Theme.Card };
            var auto = new Pill { Text = "Auto-Setup", Location = new Point(W - 222, 9), Size = new Size(104, 34), BackColor = Theme.Card };
            auto.Click += (s, e) => { Show(PageSetup); DoEverything(); };
            var conf = new Pill { Text = L("Configure", "Configurar"), Location = new Point(W - 112, 9), Size = new Size(100, 34), BackColor = Theme.Card };
            conf.Click += (s, e) => Show(PageSetup);
            statusStrip.Controls.Add(auto); statusStrip.Controls.Add(conf);
            page.Controls.Add(statusStrip);
            y += 60;

            // What is missing (Windows settings, Resizable BAR, shaders), with the way to fix it.
            warnPanel = new Panel { Location = new Point(X, y), Size = new Size(W, 44), BackColor = Theme.Card };
            warnPanel.Paint += (s, e) => { using (var p = new Pen(warnText.ForeColor == Theme.Warn ? Color.FromArgb(120, 100, 40) : Theme.Line)) e.Graphics.DrawRectangle(p, 0, 0, warnPanel.Width - 1, warnPanel.Height - 1); };
            warnText = new Label { Location = new Point(12, 0), Size = new Size(W - 160, 44), TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Dim, BackColor = Theme.Card, Font = Theme.Ui(9f), AutoEllipsis = true };
            warnFix = new Pill { Text = L("Fix it", "Corrigir"), Location = new Point(W - 132, 6), Size = new Size(120, 32), BackColor = Theme.Card, Visible = false };
            warnFix.Click += (s, e) => Show(PageSetup);
            warnPanel.Controls.Add(warnText); warnPanel.Controls.Add(warnFix);
            page.Controls.Add(warnPanel);
            y += 58;

            gameHeading = new Heading(L("Game", "Jogo")) { Location = new Point(X, y), Width = W };
            page.Controls.Add(gameHeading);
            y += 30;
            gameBox = new TextBox { Location = new Point(X, y + 4), Width = W - 316, BackColor = Theme.Card, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Mono(9.5f) };
            gameBox.TextChanged += (s, e) => Refresh2();
            page.Controls.Add(gameBox);
            var detect = new Pill { Text = L("Auto-detect", "Detetar"), Location = new Point(X + W - 304, y), Width = 146 };
            detect.Click += (s, e) => RunSteps(steps.Where(st => st.Fix == (Func<bool>)FindGame).ToArray());
            page.Controls.Add(detect);
            var browse = new Pill { Text = L("Browse...", "Procurar..."), Location = new Point(X + W - 146, y), Width = 146 };
            browse.Click += (s, e) => {
                using (var d = new FolderBrowserDialog { Description = L("Choose the folder that holds the game's game_scarlett*.exe", "Escolhe a pasta que tem o game_scarlett*.exe do jogo") })
                    if (d.ShowDialog(this) == DialogResult.OK) gameBox.Text = d.SelectedPath;
            };
            page.Controls.Add(browse);
            y += 40;
            gameStatus = Small("", X, y, W - 310);
            page.Controls.Add(gameStatus);
            page.Controls.Add(ExeBox(X + W - 300, y - 2, 300));
            y += 34;

            presetHeading = new Heading(L("Render preset", "Predefinicao de imagem")) { Location = new Point(X, y), Width = W - 230 };
            page.Controls.Add(presetHeading);
            // The resolution the console reports to the game (VICTORY_DISPLAY_MODE): the preset's scale applies on top.
            outputBox = new ComboBox { Location = new Point(X + W - 220, y - 2), Width = 220, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Theme.Card, ForeColor = Theme.Text };
            outputBox.Items.AddRange(new object[] {
                L("Output 1080p (Full HD)", "Saida 1080p (Full HD)"),
                L("Output 1440p (2K)", "Saida 1440p (2K)"),
                L("Output 2160p (4K)", "Saida 2160p (4K)"),
                L("World at 720p (lighter)", "Mundo a 720p (mais leve)"),
            });
            outputBox.SelectedIndex = 0;
            outputBox.SelectedIndexChanged += (s, e) => { SaveSettings(); Refresh2(); };
            new ToolTip { AutoPopDelay = 20000 }.SetToolTip(outputBox, L(
                "The resolution the game draws its picture for; the preset's scale applies on top (Recommended at 4K draws 2560x1440 and upscales). Higher output: a sharper picture, more video memory for the screen's images (about +0.5 GB at 2K, +1.5 GB at 4K) and more work for the card; the CPU stays the limit on strong cards. 2K and 4K are new: tell us on Discord how they run.",
                "A resolucao para a qual o jogo desenha a imagem; a escala do preset aplica-se por cima (o Recomendado em 4K desenha 2560x1440 e amplia). Saida maior: imagem mais nitida, mais VRAM para as imagens do ecra (cerca de +0,5 GB em 2K, +1,5 GB em 4K) e mais trabalho para a placa; em placas fortes o limite continua a ser o processador. 2K e 4K sao novos: conta-nos no Discord como correm."));
            page.Controls.Add(outputBox);
            y += 32;
            int cw = (W - 6 * 8) / 7;
            for (int i = 0; i < CardOrder.Length; i++)
            {
                int k = CardOrder[i];
                var c = new ModeCard { Title = modes[k].Name, Tag2 = modes[k].Tag, Line = modes[k].Short, Location = new Point(X + i * (cw + 8), y), Size = new Size(cw, 74), Tag = k };
                c.Click += (s, e) => { SelectMode(k); if (k == ModeCustom) Show(PageCustom); };
                cards.Add(c);
                page.Controls.Add(c);
            }
            y += 82;
            modeInfo = new Label { Location = new Point(X, y), Size = new Size(W, 36), ForeColor = Theme.Dim, BackColor = Theme.Back, Font = Theme.Ui(8.75f) };
            page.Controls.Add(modeInfo);
            y += 42;

            page.Controls.Add(new Heading(L("Launch options", "Opcoes de arranque")) { Location = new Point(X, y), Width = W - 360 });
            var best = new Pill { Text = L("★ Recommended setup", "★ Configuracao recomendada"), Location = new Point(X + W - 352, y - 4), Width = 200 };
            best.Click += (s, e) => Recommended();
            page.Controls.Add(best);
            var tweaks = new Pill { Text = L("Advanced Tweaks", "Ajustes avancados"), Location = new Point(X + W - 144, y - 4), Width = 144 };
            tweaks.Click += (s, e) => Show(PageCustom);
            page.Controls.Add(tweaks);
            y += 36;
            int half = (W - 10) / 2;
            Func<string, string, int, int, bool, CheckBox> T = (title, line, col, row, on) => {
                var t = new OptionCard { Text = title, Line = line, Checked = on, Location = new Point(X + col * (half + 10), y + row * 66), Size = new Size(half, 58) };
                t.CheckedChanged += (s, e) => Refresh2();
                page.Controls.Add(t);
                return t;
            };
            borderlessBox = T(L("★ Borderless Fullscreen", "★ Ecra inteiro sem bordas"), L("Native desktop presentation", "Apresentacao nativa no ambiente de trabalho"), 0, 0, true);
            fps60Box = T(L("★ Unlock 60 FPS Clock", "★ Desbloquear relogio de 60 FPS"), L("Decoupled simulation tick (no 30 FPS cap)", "Tick de simulacao separado (sem limite de 30 FPS)"), 0, 1, true);
            smoothBox = T(L("Frame generation", "Geracao de frames"), FrameGenLine(), 0, 2, true);
            panelBox = T(L("★ Diagnostics Overlay (F3)", "★ Overlay de diagnostico (F3)"), L("FPS, frame time, VRAM & RAM", "FPS, tempo de frame, VRAM e RAM"), 0, 3, true);
            reserveBox = T(L("Keep the most video memory", "Guardar o maximo de VRAM"), L("Windows reserves video memory for the game", "O Windows reserva VRAM para o jogo"), 0, 4, true);
            fightsBox = T(L("★ Crowds and fights", "★ Multidoes e lutas"), L("2 GB of the game's buffers on the card", "2 GB dos buffers do jogo na placa"), 1, 0, true);
            qualityBox = T(L("Anti-stretch", "Anti-esticados"), L("Fewer stretched bodies; several FPS slower", "Menos corpos esticados; varios FPS mais lento"), 1, 1, false);
            qualityBox.Visible = false;
            facesBox = T(L("Rainbow-face fix", "Correcao das caras arco-iris"), L("Textures finish uploading first; costs FPS", "Texturas acabam de subir primeiro; custa FPS"), 1, 2, false);
            loadingBox = T(L("Fast loading (experimental)", "Carregamento rapido (experimental)"), L("Shorter loading pauses; untested", "Pausas de carregamento mais curtas; nao testado"), 1, 3, false);
            warmBox = T(L("Load pipelines before the game", "Carregar pipelines antes do jogo"), L("No stutter the first time you see a place", "Sem engasgos a primeira vez que ves um sitio"), 1, 4, true);
            cpuBox = T(L("★ More CPU cores", "★ Mais nucleos do processador"), L("Half the cores copy the game's memory (+1 FPS)", "Metade dos nucleos copia a memoria do jogo (+1 FPS)"), 0, 5, true);
            y += 6 * 66 + 2;
            explain = new Label { Location = new Point(X, y), Size = new Size(W, 40), ForeColor = Theme.Cyan, BackColor = Theme.Back, Font = Theme.Ui(9f),
                Text = L("Point at an option to see what it does. ★ marks what was tested on edupode's PC (RTX 5070 Ti laptop).", "Passa o rato por uma opcao para ver o que faz. ★ marca o que foi testado no PC do edupode (portatil RTX 5070 Ti).") };
            page.Controls.Add(explain);
            var tipsBox = new ToolTip { AutoPopDelay = 20000, InitialDelay = 300 };
            foreach (var kv in Explanations())
            {
                var t = kv.Key; var text = kv.Value;
                tipsBox.SetToolTip(t, text);
                t.MouseEnter += (s, e) => { explain.Text = text; };
            }
            y += 46;

            page.Controls.Add(new Heading(L("Activity", "Atividade")) { Location = new Point(X, y), Width = W });
            y += 30;
            logBox = new TextBox { Location = new Point(X, y), Size = new Size(W, 120), Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Theme.Card, ForeColor = Theme.Dim, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Mono(8.5f) };
            page.Controls.Add(logBox);
            y += 128;
            loadBar = new LoadBar { Location = new Point(X, y), Size = new Size(W, 26), Visible = false };
            page.Controls.Add(loadBar);
            page.Controls.Add(new Label { Location = new Point(X, y + 36), Size = new Size(10, 10), BackColor = Theme.Back });
            return page;
        }

        Label explain;

        // What each option does, and whether it is part of the setup tested on edupode's PC.
        Dictionary<CheckBox, string> Explanations()
        {
            var d = new Dictionary<CheckBox, string>();
            d[panelBox] = L("★ Shows FPS (now, last-minute average, whole-run average, worst), frame time, draws, video memory and RAM in the upper left corner. F3 hides it.",
                            "★ Mostra os FPS (agora, media do ultimo minuto, media total, pior), tempo por frame, draws, VRAM e RAM no canto superior esquerdo. F3 esconde.");
            d[borderlessBox] = L("★ A window without borders over the whole screen (test A: 22-24 FPS on edupode's PC). Needed for Smooth Motion and quick Alt+Tab.",
                                 "★ Janela sem bordas no ecra todo (teste A: 22-24 FPS no PC do edupode). Precisa-se para o Smooth Motion e para Alt+Tab rapido.");
            d[fps60Box] = L("★ The game asks for 30 FPS; this lets its clock run up to 60, so cutscenes and menus go past 30 when the PC can.",
                            "★ O jogo pede 30 FPS; isto deixa o relogio ir ate 60, por isso cutscenes e menus passam dos 30 quando o PC consegue.");
            d[smoothBox] = L("New: NVIDIA's driver frame generation for RTX 40/50. The launcher writes it into the driver's profile for victory.exe, no NVIDIA App needed. Doubles the frames shown; adds a little input lag.",
                             "Novo: a geracao de frames do driver NVIDIA para RTX 40/50. O launcher escreve-a no perfil do driver para o victory.exe, sem NVIDIA App. Duplica os frames mostrados; junta um pouco de atraso.");
            d[reserveBox] = L("New: asks Windows to reserve video memory for the game, so other apps are moved out first when memory runs short. Fewer black and flat textures.",
                              "Novo: pede ao Windows uma reserva de VRAM para o jogo, por isso as outras apps saem primeiro quando falta memoria. Menos texturas pretas e lisas.");
            d[fightsBox] = L("Keeps 2 GB of the game's buffers on the card instead of 1.5: for crowds and fights, where the FPS drops. Needs a 12 GB card. Tested on edupode's PC: crowds 14-17 FPS instead of 10-15.",
                             "Mantem 2 GB de buffers do jogo na placa em vez de 1,5: para multidoes e lutas, onde os FPS caem. Precisa de placa de 12 GB. Testado no PC do edupode: multidoes a 14-17 FPS em vez de 10-15.");
            d[qualityBox] = L("Waits for every copy of the game's buffers before drawing: fewer stretched bodies ('spaghetti'), but several FPS slower.",
                              "Espera por todas as copias dos buffers do jogo antes de desenhar: menos corpos esticados ('esparguete'), mas varios FPS mais lento.");
            d[facesBox] = L("Waits for textures to finish uploading before use: fixes rainbow faces, costs FPS.",
                            "Espera que as texturas acabem de subir antes de as usar: corrige as caras arco-iris, custa FPS.");
            d[cpuBox] = L("★ Tested on edupode's PC: +1 FPS in the open world (21.6 vs 20.5) and 2.3 ms less preparing each frame. The CPU is the limit (the card waits ~0 ms a frame): 6 helper threads used to refresh the game's buffers and textures (~18 ms of a 44 ms frame); this gives them half your cores (up to 12) and asks Windows which pages changed on a thread of its own.",
                          "★ Testado no PC do edupode: +1 FPS no mundo aberto (21,6 contra 20,5) e menos 2,3 ms a preparar cada frame. O limite e o processador (a placa espera ~0 ms por frame): 6 ajudantes refrescavam os buffers e as texturas do jogo (~18 ms de um frame de 44 ms); isto da-lhes metade dos teus nucleos (ate 12) e pergunta ao Windows que paginas mudaram numa thread a parte.");
            d[warmBox] = L("New: before the game starts, a loading bar makes every pipeline earlier sessions drew with, in separate processes, so the driver has them ready: no stutter the first time you see a place. Takes seconds once the cache is ready; after a VIctory or driver update it takes longer, once.",
                           "Novo: antes de o jogo arrancar, uma barra de carregamento prepara todas as pipelines usadas nas sessoes anteriores, em processos a parte, para o driver ja as ter: sem engasgos a primeira vez que ves um sitio. Demora segundos com a cache pronta; depois de atualizar o VIctory ou o driver demora mais, uma vez.");
            d[loadingBox] = L("Experimental: textures the game builds on the CPU no longer wait for the game's memory to settle. Shorter loading pauses; untested.",
                              "Experimental: as texturas que o jogo prepara no processador deixam de esperar que a memoria assente. Pausas de carregamento mais curtas; nao testado.");
            return d;
        }

        // The setup that worked on edupode's PC: 720p with light textures, borderless (test A), 60 FPS clock, overlay;
        // plus the new video-memory reserve and Smooth Motion.
        void Recommended()
        {
            SelectMode(0);
            panelBox.Checked = true; borderlessBox.Checked = true; fps60Box.Checked = true; reserveBox.Checked = true;
            smoothBox.Checked = rtxFrameGen;
            fightsBox.Checked = !integratedOnly && vramGb >= 11; cpuBox.Checked = true; qualityBox.Checked = false; facesBox.Checked = false; loadingBox.Checked = false; warmBox.Checked = true;
            Log(L("Recommended setup (= the TUDO + lutas shortcut): Recommended 720p, overlay, borderless, 60 FPS clock, 2 GB of buffers for crowds, video memory reserve" + (rtxFrameGen ? ", Smooth Motion." : "."),
                  "Configuracao recomendada (= atalho TUDO + lutas): Recomendado 720p, overlay, sem bordas, relogio de 60 FPS, 2 GB de buffers para multidoes, reserva de VRAM" + (rtxFrameGen ? ", Smooth Motion." : "."),
                  "Réglages recommandés (= raccourci TUDO + lutas) : Recommandé 720p, overlay, sans bordure, horloge à 60 FPS, 2 Go de buffers pour les foules, réserve de VRAM" + (rtxFrameGen ? ", Smooth Motion." : ".")));
            // Integrated graphics: the lightest setup, nothing that costs memory it does not have.
            if (integratedOnly) { SelectMode(ModeUltraLow); fps60Box.Checked = false; smoothBox.Checked = false; fightsBox.Checked = false; }
        }

        void SelectMode(int k)
        {
            if (k < 0 || k >= modes.Length) return;
            modeIndex = k;
            foreach (var c in cards) c.Selected = (int)c.Tag == k;
            Refresh2();
        }

        Mode Selected { get { return modeIndex >= 0 ? modes[modeIndex] : modes[0]; } }

        // ---------------------------------------------------------------- Performance page
        Panel MemoryPage()
        {
            var page = Page();
            const int X = 24, W = 852;
            int y = 20;
            page.Controls.Add(new Heading(L("Video memory and RAM", "Memoria de video e RAM")) { Location = new Point(X, y), Width = W });
            y += 32;
            vramSummary = new Label { Location = new Point(X, y), Size = new Size(W, 46), ForeColor = Theme.Text, BackColor = Theme.Back, Font = new Font("Segoe UI Semibold", 10.5f) };
            page.Controls.Add(vramSummary);
            y += 50;
            vramList = new ListView { Location = new Point(X, y), Size = new Size(W, 230), View = View.Details, CheckBoxes = true, FullRowSelect = true, BackColor = Theme.Card, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 10f), HeaderStyle = ColumnHeaderStyle.Nonclickable, OwnerDraw = true };
            vramList.Columns.Add(L("App", "App"), 230);
            vramList.Columns.Add(L("Video memory", "VRAM"), 120, HorizontalAlignment.Right);
            vramList.Columns.Add("RAM", 110, HorizontalAlignment.Right);
            vramList.Columns.Add(L("Note", "Nota"), 370);
            DarkHeader(vramList);
            vramList.ItemCheck += (s, e) => {
                var u = vramList.Items[e.Index].Tag as MemoryUser;
                if (u != null && u.Protected) e.NewValue = CheckState.Unchecked;
            };
            page.Controls.Add(vramList);
            y += 240;
            var refresh = new Pill { Text = L("Refresh", "Atualizar"), Location = new Point(X, y), Width = 140 };
            refresh.Click += (s, e) => ScanMemory();
            page.Controls.Add(refresh);
            var close = new Pill { Text = L("Close the checked apps", "Fechar as apps marcadas"), Location = new Point(X + 150, y), Width = 220 };
            close.Click += (s, e) => CloseChecked();
            page.Controls.Add(close);
            y += 52;

            page.Controls.Add(new Heading("NVIDIA Smooth Motion") { Location = new Point(X, y), Width = W });
            y += 32;
            smoothStatus = new Label { Location = new Point(X, y), Size = new Size(W, 44), ForeColor = Theme.Text, BackColor = Theme.Back };
            page.Controls.Add(smoothStatus);
            y += 46;
            var smoothOn = new Pill { Text = L("Turn it on now", "Ligar agora"), Location = new Point(X, y), Width = 160 };
            smoothOn.Click += (s, e) => ApplySmooth(true, true);
            page.Controls.Add(smoothOn);
            var smoothOff = new Pill { Text = L("Turn it off", "Desligar"), Location = new Point(X + 170, y), Width = 140 };
            smoothOff.Click += (s, e) => ApplySmooth(false, true);
            page.Controls.Add(smoothOff);
            y += 52;

            page.Controls.Add(new Heading(L("Getting the most video memory", "Ter o maximo de VRAM")) { Location = new Point(X, y), Width = W });
            y += 30;
            var tips = new Label { Location = new Point(X, y), Size = new Size(W, 250), ForeColor = Theme.Dim, BackColor = Theme.Back };
            tips.Text = string.Join("\r\n", Pt ? new[] {
                "Mais VRAM livre = mais texturas na placa = menos coisas pretas, texturas lisas e recarregamentos.",
                "•  NVIDIA App > Definicoes > Funcionalidades: desliga o Instant Replay e o overlay (o 'NVIDIA Overlay' pode ter 1-2 GB).",
                "•  Discord: Definicoes > Avancado > Aceleracao de hardware: desligada. Wallpaper Engine: pausa ou fecha.",
                "•  Browsers, emuladores e outros jogos: fecha-os enquanto jogas.",
                "•  Portateis com MUX (PredatorSense, Armoury Crate, Legion Vantage): no modo Hibrido/Optimus o ambiente de trabalho",
                "   corre na grafica integrada e liberta normalmente 0,5-1,5 GB na NVIDIA.",
                "•  'Guardar o maximo de VRAM' pede ao Windows uma reserva para o VIctory: quando outra app pede memoria, a dela sai primeiro.",
                "•  O Smooth Motion e escrito no perfil do driver para victory.exe (como o NVIDIA Profile Inspector): nao precisa da NVIDIA App.",
            } : Fr ? new[] {
                "Plus de VRAM libre = plus de textures sur la carte = moins d'objets noirs, de textures plates et de rechargements.",
                "•  NVIDIA App > Paramètres > Fonctionnalités : désactive Instant Replay et l'overlay ('NVIDIA Overlay' peut prendre 1-2 Go).",
                "•  Discord : Paramètres > Avancés > Accélération matérielle : désactivée. Wallpaper Engine : mets-le en pause ou quitte-le.",
                "•  Navigateurs, émulateurs et autres jeux : ferme-les pendant que tu joues.",
                "•  Portables avec MUX (PredatorSense, Armoury Crate, Legion Vantage) : en mode Hybride/Optimus le bureau",
                "   tourne sur la carte intégrée, ce qui libère en général 0,5-1,5 Go sur la carte NVIDIA.",
                "•  'Garder le max de VRAM' demande à Windows une réserve pour VIctory : les autres applis sortent en premier.",
                "•  Smooth Motion est écrit dans le profil du pilote pour victory.exe (comme NVIDIA Profile Inspector) : pas besoin de NVIDIA App.",
            } : new[] {
                "More free video memory = more textures on the card = fewer black objects, flat textures and reloads.",
                "•  NVIDIA App > Settings > Features: turn off Instant Replay and the overlay ('NVIDIA Overlay' can hold 1-2 GB).",
                "•  Discord: Settings > Advanced > Hardware acceleration: off. Wallpaper Engine: pause or quit it.",
                "•  Browsers, emulators and other games: close them while you play.",
                "•  Laptops with a MUX switch (PredatorSense, Armoury Crate, Legion Vantage): in Hybrid/Optimus mode the desktop",
                "   runs on the integrated GPU, which usually frees 0.5-1.5 GB on the NVIDIA card.",
                "•  'Keep the most video memory' asks Windows to reserve memory for VIctory: other apps are moved out first.",
                "•  Smooth Motion is written into the driver's profile for victory.exe (as NVIDIA Profile Inspector does): no NVIDIA App needed.",
            });
            page.Controls.Add(tips);
            return page;
        }

        static void DarkHeader(ListView list)
        {
            list.DrawColumnHeader += (s, e) => {
                using (var b = new SolidBrush(Theme.Side)) e.Graphics.FillRectangle(b, e.Bounds);
                using (var b = new SolidBrush(Theme.Dim))
                using (var f = new Font("Segoe UI Semibold", 9.5f))
                {
                    var sf = new StringFormat { LineAlignment = StringAlignment.Center, Alignment = e.Header.TextAlign == HorizontalAlignment.Right ? StringAlignment.Far : StringAlignment.Near };
                    var r = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height);
                    e.Graphics.DrawString(e.Header.Text, f, b, r, sf);
                }
            };
            list.DrawItem += (s, e) => { e.DrawDefault = true; };
            list.DrawSubItem += (s, e) => { e.DrawDefault = true; };
        }

        static readonly HashSet<string> ProtectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "dwm", "csrss", "explorer", "System", "Idle", "svchost", "winlogon", "services", "lsass", "smss", "wininit",
            "fontdrvhost", "sihost", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "TextInputHost",
            "ctfmon", "RuntimeBroker", "audiodg", "Memory Compression", "Registry", "victory", "victory-ferramenta",
            "edu-launcher", "Edu's Remake", "Julia's Launcher", "MsMpEng", "NVDisplay.Container", "nvcontainer", "claude", "msedgewebview2", "conhost",
            "powershell", "pwsh", "cmd", "WindowsTerminal", "OpenConsole", "ApplicationFrameHost", "LockApp",
            "SecurityHealthSystray", "Taskmgr", "PredatorSense", "Secure System", "LsaIso", "spoolsv", "dllhost",
        };

        // Per-app video memory on the busiest card (the NVIDIA one), from the "GPU Process Memory" counters
        // Task Manager uses; and the card's total in use. Empty if the counters are not there.
        static List<MemoryUser> GpuUsers(out long cardUsed)
        {
            cardUsed = 0;
            var byLuid = new Dictionary<string, Dictionary<int, long>>();
            try
            {
                var data = new PerformanceCounterCategory("GPU Process Memory").ReadCategory()["Dedicated Usage"];
                foreach (InstanceData d in data.Values)
                {
                    // pid_1234_luid_0x00000000_0x0001ca5c_phys_0
                    var parts = d.InstanceName.Split('_');
                    if (parts.Length < 5 || parts[0] != "pid") continue;
                    int pid;
                    if (!int.TryParse(parts[1], out pid)) continue;
                    var luid = parts[3] + "_" + parts[4];
                    Dictionary<int, long> per;
                    if (!byLuid.TryGetValue(luid, out per)) byLuid[luid] = per = new Dictionary<int, long>();
                    long was;
                    per.TryGetValue(pid, out was);
                    per[pid] = was + d.RawValue;
                }
                var adapters = new PerformanceCounterCategory("GPU Adapter Memory").ReadCategory()["Dedicated Usage"];
                string best = null;
                foreach (InstanceData d in adapters.Values)
                    if (d.RawValue > cardUsed) { cardUsed = d.RawValue; best = d.InstanceName; }
                if (best == null) return new List<MemoryUser>();
                var bp = best.Split('_');
                var key = bp.Length >= 3 ? bp[1] + "_" + bp[2] : "";
                Dictionary<int, long> card;
                if (!byLuid.TryGetValue(key, out card)) return new List<MemoryUser>();
                var users = new Dictionary<string, MemoryUser>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in card)
                {
                    string name;
                    try { name = Process.GetProcessById(kv.Key).ProcessName; } catch (Exception) { continue; }
                    MemoryUser u;
                    if (!users.TryGetValue(name, out u)) users[name] = u = new MemoryUser { Name = name };
                    u.Vram += kv.Value;
                }
                return users.Values.ToList();
            }
            catch (Exception) { return new List<MemoryUser>(); }
        }

        void ScanMemory()
        {
            vramSummary.Text = L("Looking...", "A ver...");
            new Thread(() => {
                long cardUsed;
                var users = GpuUsers(out cardUsed);
                var byName = users.ToDictionary(u => u.Name, StringComparer.OrdinalIgnoreCase);
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        MemoryUser u;
                        if (!byName.TryGetValue(p.ProcessName, out u)) byName[p.ProcessName] = u = new MemoryUser { Name = p.ProcessName };
                        u.Ram += p.PrivateMemorySize64;
                    }
                    catch (Exception) { }
                }
                long victory = 0;
                MemoryUser self;
                if (byName.TryGetValue("victory", out self)) victory = self.Vram;
                var shown = byName.Values.Where(u => u.Vram >= (40L << 20) || u.Ram >= (400L << 20)).OrderByDescending(u => u.Vram * 4 + u.Ram).ToList();
                foreach (var u in shown)
                {
                    u.Protected = ProtectedNames.Contains(u.Name);
                    if (u.Name.Equals("dwm", StringComparison.OrdinalIgnoreCase)) u.Note = L("Windows desktop: see the MUX tip below", "Ambiente de trabalho: ve a dica do MUX em baixo");
                    else if (u.Name.Equals("victory", StringComparison.OrdinalIgnoreCase)) u.Note = L("the game", "o jogo");
                    else if (u.Name.StartsWith("NVIDIA Overlay", StringComparison.OrdinalIgnoreCase)) u.Note = L("turn off Instant Replay / overlay in NVIDIA App", "desliga Instant Replay / overlay na NVIDIA App");
                    else if (u.Name.Equals("Discord", StringComparison.OrdinalIgnoreCase)) u.Note = L("or turn off its hardware acceleration", "ou desliga a aceleracao de hardware");
                    else if (u.Name.StartsWith("wallpaper", StringComparison.OrdinalIgnoreCase)) u.Note = "Wallpaper Engine";
                    else if (u.Name.Equals("msedgewebview2", StringComparison.OrdinalIgnoreCase)) u.Note = L("part of other apps (protected)", "parte de outras apps (protegido)");
                    else if (u.Protected) u.Note = L("system or needed (protected)", "sistema ou necessario (protegido)");
                }
                var ram = new Microsoft.VisualBasic.Devices.ComputerInfo();
                double ramTotal = ram.TotalPhysicalMemory / 1073741824.0, ramFree = ram.AvailablePhysicalMemory / 1073741824.0;
                BeginInvoke(new Action(() => {
                    double gb = 1073741824.0;
                    var others = Math.Max(0, cardUsed - victory);
                    vramSummary.Text = cardUsed == 0
                        ? L("Could not read the video memory counters (they need Windows 10 1709 or later).", "Nao deu para ler os contadores de VRAM (precisam do Windows 10 1709 ou mais recente).")
                        : string.Format(L("Card: {0:0.0} of {1:0.0} GB in use  —  VIctory {2:0.0} GB, everything else {3:0.0} GB\r\nRAM: {4:0.0} of {5:0.0} GB free",
                                          "Placa: {0:0.0} de {1:0.0} GB em uso  —  VIctory {2:0.0} GB, o resto {3:0.0} GB\r\nRAM: {4:0.0} de {5:0.0} GB livres"),
                                        cardUsed / gb, vramGb, victory / gb, others / gb, ramFree, ramTotal);
                    vramSummary.ForeColor = others > (2L << 30) ? Theme.Warn : Theme.Text;
                    vramList.BeginUpdate();
                    vramList.Items.Clear();
                    foreach (var u in shown)
                    {
                        var item = new ListViewItem(new[] { u.Name, u.Vram > 0 ? (u.Vram >> 20) + " MB" : "-", (u.Ram >> 20) + " MB", u.Note }) { Tag = u };
                        if (u.Protected) item.ForeColor = Theme.Dim;
                        vramList.Items.Add(item);
                    }
                    vramList.EndUpdate();
                }));
            }) { IsBackground = true }.Start();
        }

        void CloseChecked()
        {
            var names = vramList.CheckedItems.Cast<ListViewItem>().Select(i => i.Tag as MemoryUser).Where(u => u != null && !u.Protected).Select(u => u.Name).ToList();
            if (names.Count == 0) { MessageBox.Show(this, L("Check the apps to close first.", "Marca primeiro as apps a fechar.")); return; }
            var ask = L("Close these apps? Unsaved work in them is lost.\r\n\r\n", "Fechar estas apps? O que nao estiver guardado nelas perde-se.\r\n\r\n") + string.Join("\r\n", names);
            if (MessageBox.Show(this, ask, "Julia's Launcher", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            foreach (var n in names)
                foreach (var p in Process.GetProcessesByName(n))
                {
                    try { if (!p.CloseMainWindow() || !p.WaitForExit(1500)) p.Kill(); }
                    catch (Exception) { }
                }
            Thread.Sleep(800);
            ScanMemory();
        }

        // ---------------------------------------------------------------- Smooth Motion
        void UpdateSmooth()
        {
            if (smoothStatus == null) return;
            string state = smoothState == "on" ? L("ON for victory.exe", "LIGADO para o victory.exe")
                : smoothState == "off" ? L("off for victory.exe", "desligado para o victory.exe")
                : smoothState == "not set" ? L("not set yet (PLAY sets it when the option is on)", "ainda nao configurado (o JOGAR configura com a opcao ligada)")
                : smoothState;
            smoothStatus.Text = L("Driver frame generation: the card shows a made-up frame between two of the game's. Now: ", "Geracao de frames do driver: a placa mostra um frame inventado entre dois do jogo. Agora: ") + state
                + (rtxFrameGen ? "" : L("\r\nNeeds an RTX 40 or 50 series card and driver 571.86 or newer.", "\r\nPrecisa de uma placa RTX 40 ou 50 e do driver 571.86 ou mais recente."));
            smoothStatus.ForeColor = smoothState == "on" ? Theme.Good : Theme.Text;
        }

        void ApplySmooth(bool on, bool loud)
        {
            new Thread(() => {
                var err = SmoothMotion.Set(on);
                var s = SmoothMotion.Status();
                BeginInvoke(new Action(() => {
                    smoothState = s;
                    UpdateSmooth();
                    UpdateBanner();
                    if (err != null) Log("Smooth Motion: " + err);
                    else Log(on ? L("Smooth Motion is on for victory.exe (driver profile).", "Smooth Motion ligado para o victory.exe (perfil do driver).")
                                : L("Smooth Motion is off for victory.exe.", "Smooth Motion desligado para o victory.exe."));
                    if (loud && err != null) MessageBox.Show(this, "Smooth Motion: " + err, "Julia's Launcher");
                }));
            }) { IsBackground = true }.Start();
        }

        // ---------------------------------------------------------------- Controls and Help
        // The controls grouped by what the player is doing (on foot, talking, fighting, driving, flying, menus),
        // each line: what it does, the key, the pad. Keys are drawn as key caps.
        Panel ControlsPage()
        {
            var page = Page();
            const int X = 24, W = 852;
            int y = 20;
            page.Controls.Add(new Heading(L("Controls, by what you are doing", "Controlos, pelo que estas a fazer", "Commandes, selon ce que tu fais")) { Location = new Point(X, y), Width = W });
            y += 30;
            page.Controls.Add(Small(L("Keyboard and mouse, and the Xbox controller button for each. A controller connected to the PC replaces all of this.",
                                      "Teclado e rato, e o botao do comando Xbox de cada um. Um comando ligado ao PC substitui tudo isto.",
                                      "Clavier et souris, et le bouton de la manette Xbox pour chacun. Une manette branchée au PC remplace tout cela."), X, y, W));
            y += 30;
            Func<string, string, string, string[]> row = (what, key, pad) => new[] { what, key, pad };
            var groups = new List<Tuple<string, string[][]>> {
                Tuple.Create(L("On foot", "A pe", "À pied"), new[] {
                    row(L("Move", "Andar", "Se déplacer"), "W A S D", L("Left stick", "Analogico esq.", "Stick gauche")),
                    row(L("Look around (click the game to capture the mouse)", "Olhar (clica no jogo para capturar o rato)", "Regarder (clique dans le jeu pour capturer la souris)"), L("Mouse", "Rato", "Souris"), L("Right stick", "Analogico dir.", "Stick droit")),
                    row(L("Run", "Correr", "Courir"), L("Left Shift", "Shift esq.", "Maj gauche"), "L3"),
                    row(L("Jump, climb", "Saltar, escalar", "Sauter, grimper"), L("Space / Enter", "Espaco / Enter", "Espace / Entrée"), "A"),
                    row(L("Crouch, rest", "Agachar, descansar", "S'accroupir, se reposer"), L("Left Ctrl / C / Backspace", "Ctrl esq. / C / Backspace", "Ctrl gauche / C / Retour"), "B"),
                    row(L("Take cover", "Proteger-se", "Se mettre à couvert"), "Q", "RB"),
                    row(L("Look back", "Olhar para tras", "Regarder derrière"), "V", "R3"),
                }),
                Tuple.Create(L("Talking and using things", "Falar e usar coisas", "Parler et utiliser"), new[] {
                    row(L("Interact, talk", "Interagir, falar", "Interagir, parler"), "E / Y", "Y"),
                    row(L("Aim, interact", "Mirar, interagir", "Viser, interagir"), L("Right mouse button", "Botao direito", "Clic droit"), "LT"),
                }),
                Tuple.Create(L("Fighting", "Lutar", "Combat"), new[] {
                    row(L("Shoot", "Disparar", "Tirer"), L("Left mouse button", "Botao esquerdo", "Clic gauche"), "RT / RB"),
                    row(L("Punch, kick", "Soco, pontape", "Coup de poing, de pied"), "R / X", "X"),
                    row(L("Block", "Bloquear", "Bloquer"), L("Space", "Espaco", "Espace"), "A"),
                    row(L("Inventory, weapon wheel (hold)", "Inventario, roda de armas (manter)", "Inventaire, roue des armes (maintenir)"), "Tab", "LB"),
                }),
                Tuple.Create(L("Driving (press F: a high beep means vehicle mode)", "Conduzir (carrega no F: um apito agudo e o modo veiculo)", "Conduire (appuie sur F : un bip aigu = mode véhicule)"), new[] {
                    row(L("Get in, get out", "Entrar, sair", "Monter, descendre"), "F", L("Y + vehicle mode", "Y + modo veiculo", "Y + mode véhicule")),
                    row(L("Accelerate", "Acelerar", "Accélérer"), "W", "RT"),
                    row(L("Brake, reverse", "Travar, marcha-atras", "Freiner, reculer"), "S", "LT"),
                    row(L("Steer", "Virar", "Tourner"), "A / D", L("Left stick", "Analogico esq.", "Stick gauche")),
                    row(L("Handbrake", "Travao de mao", "Frein à main"), L("Space", "Espaco", "Espace"), "A"),
                    row(L("Horn, nitro", "Buzina, nitro", "Klaxon, nitro"), L("Left Shift", "Shift esq.", "Maj gauche"), "L3"),
                    row(L("Headlights", "Farois", "Phares"), L("Left Ctrl / C", "Ctrl esq. / C", "Ctrl gauche / C"), "B"),
                    row(L("Draw a weapon", "Sacar arma", "Sortir une arme"), "Tab", "LB"),
                    row(L("Vehicle mode out of sync? Set it right", "Modo veiculo trocado? Acerta-o", "Mode véhicule décalé ? Le recaler"), "Caps Lock", "-"),
                }),
                Tuple.Create(L("Flying", "Voar", "Voler"), new[] {
                    row(L("Take off", "Levantar voo", "Décoller"), "W + K", "-"),
                    row(L("Nose down, nose up", "Nariz baixo, nariz cima", "Piquer, cabrer"), "I / K", L("Left stick up/down", "Analogico esq. cima/baixo", "Stick gauche haut/bas")),
                    row(L("Turn left", "Virar a esquerda", "Tourner à gauche"), "R / X", "X"),
                    row(L("Turn right", "Virar a direita", "Tourner à droite"), L("Left Ctrl / C", "Ctrl esq. / C", "Ctrl gauche / C"), "B"),
                }),
                Tuple.Create(L("Map, phone and menus", "Mapa, telemovel e menus", "Carte, téléphone et menus"), new[] {
                    row(L("Map", "Mapa", "Carte"), "M", "View"),
                    row(L("Phone, radio, partner", "Telemovel, radio, parceiro", "Téléphone, radio, partenaire"), L("Arrows / 1-4", "Setas / 1-4", "Flèches / 1-4"), L("D-pad", "D-pad", "Croix")),
                    row(L("Pause", "Pausa", "Pause"), "Esc", "Menu"),
                }),
                Tuple.Create(L("VIctory's own keys", "Teclas do VIctory", "Touches de VIctory"), new[] {
                    row(L("Release the mouse", "Soltar o rato", "Libérer la souris"), "Alt / F1", "-"),
                    row(L("Show or hide the overlay (FPS, memory)", "Mostrar ou esconder o overlay (FPS, memoria)", "Afficher ou masquer l'overlay (FPS, mémoire)"), "F3", "-"),
                    row(L("Record a frame (recording mode)", "Gravar um frame (modo de gravacao)", "Enregistrer une image (mode enregistrement)"), "F9", "-"),
                }),
            };
            foreach (var g in groups)
            {
                page.Controls.Add(new Heading(g.Item1) { Location = new Point(X, y), Width = W });
                y += 30;
                foreach (var r in g.Item2)
                {
                    page.Controls.Add(new Label { Text = r[0], Location = new Point(X + 8, y + 6), Size = new Size(470, 22), ForeColor = Theme.Text, BackColor = Theme.Back, Font = Theme.Ui(9.75f), AutoEllipsis = true });
                    page.Controls.Add(new KeyCap { Text = r[1], Location = new Point(X + 490, y + 2), Size = new Size(210, 28) });
                    page.Controls.Add(new KeyCap { Text = r[2], Pad = true, Location = new Point(X + 712, y + 2), Size = new Size(140, 28) });
                    y += 34;
                }
                y += 10;
            }
            page.Controls.Add(new Label { Location = new Point(X, y), Size = new Size(10, 10), BackColor = Theme.Back });
            return page;
        }
        Panel HelpPage()
        {
            var page = Page();
            const int X = 28, W = 900;
            int y = 18;
            page.Controls.Add(new Heading(L("Help: everything the launcher does", "Ajuda: tudo o que o launcher faz")) { Location = new Point(X, y), Width = W });
            y += 32;
            var discord = new Pill { Main = true, Text = L("Send errors (GitHub / Discord)", "Enviar erros (GitHub / Discord)"), Location = new Point(X, y), Size = new Size(220, 38) };
            discord.Click += (s, e) => ReportErrors();
            page.Controls.Add(discord);
            var join = new Pill { Text = L("Join the Discord", "Entrar no Discord"), Location = new Point(X + 230, y), Size = new Size(170, 38) };
            join.Click += (s, e) => OpenUrl(DiscordInvite);
            page.Controls.Add(join);
            var logs = new Pill { Text = L("Open the logs folder", "Abrir a pasta dos logs"), Location = new Point(X + 410, y), Size = new Size(190, 38) };
            logs.Click += (s, e) => { Directory.CreateDirectory(Path.Combine(dataRoot, "logs")); OpenUrl(Path.Combine(dataRoot, "logs")); };
            page.Controls.Add(logs);
            var news = new Pill { Text = L("What's new", "Novidades", "Nouveautés"), Location = new Point(X + 610, y), Size = new Size(130, 38) };
            news.Click += (s, e) => ShowWhatsNew();
            page.Controls.Add(news);
            var safe = new Pill { Text = L("Is it safe?", "E seguro?", "Est-ce sûr ?"), Location = new Point(X + 750, y), Size = new Size(130, 38) };
            safe.Click += (s, e) => OpenUrl(SafetyUrl);
            page.Controls.Add(safe);
            y += 52;
            var text = new TextBox { Location = new Point(X, y), Size = new Size(W, 1180), Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, ForeColor = Theme.Text, BackColor = Theme.Back, Font = Theme.Ui(10f), ScrollBars = ScrollBars.None, TabStop = false };
            text.Text = string.Join("\r\n", FunctionList().Concat(new[] { "", L("ABOUT", "SOBRE"), L("Julia's Launcher is edupode's remake, built on the work of:", "O Julia's Launcher e o remake do edupode, feito com o trabalho de:") }).Concat(Credits()).ToArray());
            page.Controls.Add(text);
            page.Controls.Add(new Label { Location = new Point(X, y + 1190), Size = new Size(10, 10), BackColor = Theme.Back });
            return page;
        }

        // Every function, as the Help page and the README's download section list it.
        IEnumerable<string> FunctionList()
        {
            return Pt ? new[] {
                "JOGAR",
                "   •  Predefinicoes: Recomendado (720p), Mais rapido e PC modesto (540p), Ultra baixo (PCs com muito pouca memoria), Nitido e",
                "      cgbj classico (1080p), Personalizado (os teus valores). O launcher escolhe uma pelo teu hardware na primeira vez.",
                "   •  Opcoes de arranque: passa o rato por cada uma para ver o que faz. A ★ marca o que foi testado.",
                "   •  JOGAR prepara as pipelines, abre o jogo e, quando fecha, guarda as pipelines desta sessao para a proxima.",
                "   •  Um jogo preso (um erro, uma janela que deixou de responder) nunca bloqueia o JOGAR: aparece 'FECHAR E JOGAR'.",
                "   •  Se faltar alguma coisa (memoria virtual, energia, Resizable BAR, shaders), um aviso amarelo diz o que e e leva-te a corrigir.",
                "",
                "PREPARAR",
                "   •  'Fazer tudo por mim' corre os passos que faltam: motor, pasta do jogo, shaders, energia,",
                "      memoria virtual, placa grafica certa para o VIctory e geracao de frames. O Windows pede administrador quando e preciso.",
                "   •  Resizable BAR: o launcher ve o que o D3D12 da ao jogo (nao so a BIOS) e mostra o guia da tua placa-mae.",
                "",
                "SHADERS",
                "   •  Mostra em tempo real os shaders traduzidos, as pipelines prontas, as que falharam, o espaco em disco e os erros.",
                "   •  'Reparar' refaz o que falta; 'Refazer tudo' apaga a cache e prepara de novo.",
                "",
                "DESEMPENHO",
                "   •  Quem ocupa a VRAM e a RAM; marca apps e fecha-as antes de jogar. Liga ou desliga a geracao de frames.",
                "",
                "HARDWARE E ID",
                "   •  O teu ID permanente (VI-XXXX-XXXX), com botao de copiar. Vem de pecas do PC e nao muda ao reinstalar o Windows.",
                "   •  Partilha de hardware (desligada por defeito): envia SO o hardware (placa, processador, RAM, placa-mae, Windows,",
                "      tipo de disco). Nunca envia logs (essas tem uma escolha propria), opcoes, caminhos, nomes nem o IP. A pagina mostra exatamente o que e enviado.",
                "",
                "ATUALIZACOES",
                "   •  O launcher verifica o GitHub ao abrir e de 5 em 5 minutos. Um update so e instalado se a assinatura e o SHA-256",
                "      baterem certo; instala-se sozinho quando o jogo nao esta a correr. E a unica coisa que o launcher recebe da internet.",
                "",
                "AJUSTES AVANCADOS (PERSONALIZADO)",
                "   •  Resolucao, texturas, buffers, protecoes de memoria e relogio de FPS. 'Copiar o meu perfil' da um codigo (VI1-...)",
                "      para partilhar; cola o de um amigo na caixa e carrega em 'Aplicar' (mostra o que muda antes).",
                "",
                "LOGS E ERROS",
                "   •  Logs do jogo: " + Path.Combine(dataRoot, "logs") + " (session-*.log, prepare.log).",
                "   •  Erros do launcher: " + Program.ErrorFile,
                "   •  'Enviar erros no Discord': escolhes 'erros do launcher' ou 'erros do jogo' e escreves o que aconteceu. O launcher junta os",
                "      ficheiros certos num zip no Ambiente de trabalho, abre a pasta e diz-te para o enviares no canal \"" + DiscordChannel + "\".",
                "      Nada e enviado sozinho: es tu que envias.",
                "",
                "PLACAS",
                "   •  NVIDIA RTX 40/50: Smooth Motion. RTX 30: NVSmooth30 (experimental). Outras placas (GTX 10, RTX 20, AMD, Intel): geracao de frames VI",
                "      (experimental, desligada por defeito): um frame entre cada dois do jogo, feito so com as imagens, com meio frame de atraso.",
                "   •  Todas as placas (NVIDIA, AMD, Intel) jogam com o motor edupode; para Radeon ha a alternativa do Belobug.",
                "",
                "TECLAS NO JOGO",
                "   •  F3 mostra/esconde o overlay: FPS, media do ultimo minuto (AVG 1M), media total (ALL), pior meio segundo (LOW).",
                "   •  Alt ou F1 solta o rato. A pagina Controlos tem o resto.",
            } : Fr ? new[] {
                "JOUER",
                "   •  Préréglages : Recommandé (720p), Le plus rapide et PC modeste (540p), Ultra bas (PC avec très peu de mémoire), Net et",
                "      cgbj classique (1080p), Personnalisé (tes valeurs). Le launcher en choisit un selon ton matériel la première fois.",
                "   •  Options de lancement : survole chacune pour voir ce qu'elle fait. ★ marque ce qui a été testé.",
                "   •  JOUER charge les pipelines, lance le jeu et, à sa fermeture, garde les pipelines de cette session pour la prochaine.",
                "   •  Un jeu bloqué (une erreur, une fenêtre qui ne répond plus) ne bloque jamais JOUER : il devient 'FERMER ET JOUER'.",
                "   •  Quand il manque quelque chose (mémoire virtuelle, alimentation, Resizable BAR, shaders), un avis jaune dit quoi et t'emmène corriger.",
                "",
                "INSTALLATION",
                "   •  'Tout faire pour moi' lance les étapes manquantes : moteur, dossier du jeu, shaders, alimentation,",
                "      mémoire virtuelle, bonne carte graphique pour VIctory et génération d'images. Windows demande les droits administrateur si besoin.",
                "   •  Resizable BAR : le launcher vérifie ce que D3D12 donne au jeu (pas seulement le BIOS) et montre le guide de ta carte mère.",
                "",
                "SHADERS",
                "   •  Affiche en direct les shaders traduits, les pipelines prêtes, celles en échec, l'espace disque et les erreurs.",
                "   •  'Réparer' fait ce qui manque ; 'Tout refaire' vide le cache et prépare à nouveau.",
                "",
                "PERFORMANCES",
                "   •  Ce qui occupe la VRAM et la RAM ; coche des applis et ferme-les avant de jouer. Génération d'images activée ou non.",
                "",
                "MATÉRIEL ET ID",
                "   •  Ton ID permanent (VI-XXXX-XXXX), avec un bouton copier. Il vient des composants du PC et reste après une réinstallation de Windows.",
                "   •  Partage du matériel (désactivé par défaut) : envoie UNIQUEMENT le matériel (carte graphique, processeur, RAM, carte mère, Windows,",
                "      type de disque). Jamais de logs (ceux-ci ont leur propre choix), d'options, de chemins, de noms ni ton IP. La page montre exactement ce qui est envoyé.",
                "",
                "MISES À JOUR",
                "   •  Le launcher vérifie GitHub à l'ouverture et toutes les 5 minutes. Une mise à jour n'est installée que si sa signature et",
                "      son SHA-256 correspondent ; elle s'installe seule quand le jeu ne tourne pas. C'est la seule chose que le launcher reçoit d'internet.",
                "",
                "RÉGLAGES AVANCÉS (PERSONNALISÉ)",
                "   •  Résolution, textures, buffers, protections mémoire et horloge FPS. 'Copier mon profil' donne un code (VI1-...) à partager ;",
                "      colle celui d'un ami dans la case et appuie sur 'Appliquer' (il montre d'abord ce qui change).",
                "",
                "LOGS ET ERREURS",
                "   •  Logs du jeu : " + Path.Combine(dataRoot, "logs") + " (session-*.log, prepare.log).",
                "   •  Erreurs du launcher : " + Program.ErrorFile,
                "   •  'Envoyer les erreurs sur Discord' : choisis 'erreurs du launcher' ou 'erreurs du jeu' et écris ce qui s'est passé. Le launcher met",
                "      les bons fichiers dans un zip sur ton Bureau, ouvre le dossier et te demande de l'envoyer dans le salon \"" + DiscordChannel + "\".",
                "      Rien n'est envoyé tout seul : c'est toi qui l'envoies.",
                "",
                "CARTES GRAPHIQUES",
                "   •  NVIDIA RTX 40/50 : Smooth Motion. RTX 30 : NVSmooth30 (expérimental). Autres cartes (GTX 10, RTX 20, AMD, Intel) : génération d'images VI",
                "      (expérimental, désactivée par défaut) : une image entre chaque paire, faite à partir des images seules, avec une demi-image de retard.",
                "   •  AMD : le launcher détecte la carte et utilise le moteur AMD de Belobug (VIctory-Belobug-AMD).",
                "",
                "TOUCHES EN JEU",
                "   •  F3 affiche/masque l'overlay : FPS, moyenne de la dernière minute (AVG 1M), moyenne depuis le début (ALL), pire demi-seconde (LOW).",
                "   •  Alt ou F1 libère la souris. La page Commandes a le reste.",
            } : new[] {
                "PLAY",
                "   •  Presets: Recommended (720p), Fastest and Modest PC (540p), Ultra Low (PCs with very little memory), Sharp and",
                "      Classic cgbj (1080p), Custom (your values). The launcher picks one from your hardware the first time.",
                "   •  Launch options: point at each to see what it does. ★ marks what was tested.",
                "   •  PLAY loads the pipelines, starts the game and, when it closes, keeps this session's pipelines for the next one.",
                "   •  A stuck game (an error, a window that stopped answering) never blocks PLAY: it turns into 'CLOSE AND PLAY'.",
                "   •  When something is missing (virtual memory, power, Resizable BAR, shaders), a yellow notice says what and takes you to fix it.",
                "",
                "SETUP",
                "   •  'Do everything for me' runs the missing steps: engine, game folder, shaders, power, virtual",
                "      memory, the right graphics card for VIctory and frame generation. Windows asks for administrator rights when needed.",
                "   •  Resizable BAR: the launcher checks what D3D12 gives the game (not only the BIOS) and shows your motherboard's guide.",
                "",
                "SHADERS",
                "   •  Shows, live, the translated shaders, the pipelines ready, the ones that failed, disk space and errors.",
                "   •  'Repair' makes what is missing; 'Rebuild everything' clears the cache and prepares again.",
                "",
                "PERFORMANCE",
                "   •  What holds video memory and RAM; check apps and close them before playing. Frame generation on or off.",
                "",
                "HARDWARE & ID",
                "   •  Your permanent ID (VI-XXXX-XXXX), with a copy button. It comes from PC parts and stays after a Windows reinstall.",
                "   •  Hardware sharing (off by default): sends ONLY hardware (graphics card, CPU, RAM, motherboard, Windows, disk type).",
                "      It never sends logs (those have a choice of their own), options, paths, names or your IP. The page shows exactly what is sent.",
                "",
                "UPDATES",
                "   •  The launcher checks GitHub when it opens and every 5 minutes. An update is installed only when its signature and",
                "      SHA-256 match; it installs itself while the game is not running. It is the only thing the launcher takes from the internet.",
                "",
                "ADVANCED TWEAKS (CUSTOM)",
                "   •  Resolution, textures, buffers, memory guards and FPS clock. 'Copy my profile' gives a code (VI1-...) to share;",
                "      paste a friend's in the box and press 'Apply' (it shows what changes first).",
                "",
                "LOGS AND ERRORS",
                "   •  Game logs: " + Path.Combine(dataRoot, "logs") + " (session-*.log, prepare.log).",
                "   •  Launcher errors: " + Program.ErrorFile,
                "   •  'Send errors to Discord': choose 'launcher errors' or 'game errors' and write what happened. The launcher puts the",
                "      right files in a zip on your Desktop, opens the folder and asks you to send it in the \"" + DiscordChannel + "\" channel.",
                "      Nothing is sent on its own: you send it.",
                "",
                "GRAPHICS CARDS",
                "   •  NVIDIA RTX 40/50: Smooth Motion. RTX 30: NVSmooth30 (experimental). Other cards (GTX 10, RTX 20, AMD, Intel): VI frame generation",
                "      (experimental, off by default): a frame between each two of the game's, made from the pictures alone, half a frame later.",
                "   •  Every card (NVIDIA, AMD, Intel) plays with the edupode engine; Radeon cards can pick Belobug's alternative.",
                "",
                "KEYS IN THE GAME",
                "   •  F3 shows/hides the overlay: FPS, last minute's average (AVG 1M), average since the start (ALL), worst half second (LOW).",
                "   •  Alt or F1 releases the mouse. The Controls page has the rest.",
            };
        }

        // The projects inside this build, for the Help page.
        static IEnumerable<string> Credits()
        {
            return new[] {
                "   •  VIctory (Marvisak) — the Xbox Series emulator: github.com/Marvisak/VIctory",
                "   •  cgbj (cputroll/cgbj-reupload) — the performance build this one starts from",
                "   •  AnyXboxSeries — Xbox Series runtime work",
                "   •  Leonidas — fixes and research",
                "   •  VIctory-NVIDIA (trustno1-coder) — the lighting for NVIDIA cards",
                "   •  fxceless — rendering and performance fixes",
                "   •  VIctory-7900XTX (avatarmanz) — adaptations from the AMD fork",
                "   •  VIctory-Belobug-AMD (Belobug) — the alternative engine for Radeon cards: github.com/Belobug/VIctory-Belobug-AMD",
                "   •  NVSmooth30 (ItsAdeline) — Smooth Motion on RTX 30 cards (experimental): github.com/ItsAdeline/NVSmooth30",
                "   •  Cold Project VI (felicienradio) — the project page this remake follows",
            };
        }

        // ---------------------------------------------------------------- state
        // The emulator this PC plays with: on an AMD card the AMD fork's (Hardware.cs, Engine), this
        // repository's otherwise; a build from source first, then the one an update put in place.
        string VictoryExe
        {
            get
            {
                var places = UseAmdEngine
                    ? new[] { Path.Combine(BuildTarget(AmdEngineDir), "diag", "victory.exe"), Path.Combine(AmdEngineDir, "target", "diag", "victory.exe"), Path.Combine(AmdEngineDir, "victory.exe") }
                    : OwnExeCandidates;
                foreach (var p in places)
                    if (File.Exists(p)) return p;
                return null;
            }
        }

        // Where this repository's emulator may be: built in the source folder (beside the launcher,
        // above it, or the one the launcher downloaded), or ready-made beside the launcher (a release).
        IEnumerable<string> OwnExeCandidates
        {
            get
            {
                var list = new List<string>();
                var src = SourceDir;
                if (src != null) list.Add(Path.Combine(src, "target", "diag", "victory.exe"));
                if (src != null && StorageRoot != null) list.Add(Path.Combine(BuildTarget(src), "diag", "victory.exe"));
                list.Add(Path.Combine(root, "target", "diag", "victory.exe"));
                list.Add(Path.Combine(root, "victory.exe"));
                list.Add(Path.Combine(AppDir, "victory.exe"));
                // The newest first: a fresh build or a fresh update, whichever came last.
                return list.Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(p => File.Exists(p) ? File.GetLastWriteTimeUtc(p) : DateTime.MinValue).ToList();
            }
        }

        // Where updates put the emulator and tools: beside the launcher when it is installed (the
        // installer's folder holds victory.exe), else %LOCALAPPDATA%\VIctory\app, so a launcher on the
        // Desktop or in Downloads never fills that folder.
        string AppDir
        {
            get
            {
                if (File.Exists(Path.Combine(root, "victory.exe")) || File.Exists(Path.Combine(root, "unins000.exe"))) return root;
                return Path.Combine(Path.GetDirectoryName(dataRoot), "app");
            }
        }

        // This repository's own build (the `victory gpu` probe and the tools), whatever the engine.
        string OwnVictoryExe
        {
            get
            {
                foreach (var p in OwnExeCandidates)
                    if (File.Exists(p)) return p;
                return null;
            }
        }

        void Refresh2()
        {
            RefreshExes();
            bool game = gameBox.Text.Trim().Length > 0 && File.Exists(GameExe);
            int count = exeBox != null && exeBox.Visible ? exeBox.Items.Count : 0;
            gameStatus.Text = !game ? L("Choose the folder of your dump.", "Escolhe a pasta do teu dump.")
                : exeProblem != null ? "✗  " + exeProblem
                : count > 1 ? string.Format(L("✓  Game found: {0} executables, choose one on the right.", "✓  Jogo encontrado: {0} executaveis, escolhe um a direita."), count)
                : L("✓  Game found.", "✓  Jogo encontrado.");
            gameStatus.ForeColor = !game ? Theme.Dim : exeProblem != null ? Theme.Warn : Theme.Good;
            gameHeading.Note = game ? Path.GetFileName(GameExe) : "";
            gameHeading.Invalidate();
            var exe = VictoryExe;
            if (buildStatus != null)
            {
                buildStatus.Text = exe != null ? "✓  " + exe : UseAmdEngine
                    ? L("The AMD engine is not built yet: Setup > 'AMD engine (Belobug)'.", "O motor AMD ainda nao foi compilado: Preparar > 'Motor AMD (Belobug)'.")
                    : L("Not built yet: press 'Build VIctory' (needs Rust and Visual Studio Build Tools).", "Ainda nao compilado: carrega em 'Compilar VIctory' (precisa do Rust e do Visual Studio Build Tools).");
                buildStatus.ForeColor = exe != null ? Theme.Good : Theme.Warn;
            }
            modeInfo.Text = modeIndex < 0 ? "" : Selected.Description;
            presetHeading.Note = PresetSummary();
            presetHeading.Invalidate();
            var state = State();
            // A stuck session never keeps PLAY off: PLAY closes it first.
            bool ready = game && exe != null && state != GameState.Running && !busyNow && !setupBusy;
            playButton.Enabled = ready;
            if (prepareButton != null) { prepareButton.Enabled = ready && state == GameState.None; prepareButton.Invalidate(); }
            playButton.Text = state == GameState.Running ? L("RUNNING", "A CORRER") : busyNow ? L("WAIT...", "ESPERA...")
                : state == GameState.Stuck ? L("CLOSE AND PLAY", "FECHAR E JOGAR") : L("▶  PLAY", "▶  JOGAR");
            closeButton.Visible = state != GameState.None;
            closeButton.Text = state == GameState.Stuck ? L("Close stuck game", "Fechar jogo preso") : L("Close game", "Fechar o jogo");
            if (tips == null) tips = new ToolTip();
            tips.SetToolTip(playButton, state == GameState.Stuck ? L("A VIctory session stopped answering: PLAY closes it and starts again.", "Uma sessao do VIctory deixou de responder: JOGAR fecha-a e arranca de novo.")
                : ready ? "" : !game ? L("Choose the game folder first (Auto-detect).", "Escolhe primeiro a pasta do jogo (Detetar).")
                : exe == null ? L("Build VIctory first (Setup).", "Compila o VIctory primeiro (Preparar).")
                : state == GameState.Running ? L("The game is already running ('Close game' ends it).", "O jogo ja esta a correr ('Fechar o jogo' termina-o).")
                : L("Wait for the current task to finish.", "Espera que a tarefa atual acabe."));
            playButton.Invalidate();
            closeButton.Invalidate();

            // The strip of checks, the warnings and the footer.
            var missing = Missing();
            statusStrip.Items = new[] {
                game && exeProblem == null ? L("Dump verified", "Dump verificado") : L("No game dump", "Sem dump do jogo"),
                exe != null ? L("Binary ready", "Binario pronto") : L("Binary missing", "Falta o binario"),
                shaderFiles > 0 ? string.Format(L("{0:N0} shaders cached", "{0:N0} shaders em cache"), shaderFiles) : L("Shaders not prepared", "Shaders por preparar"),
                missing.Count == 0 ? L("Windows ready", "Windows pronto") : string.Format(L("{0} to fix", "{0} por corrigir"), missing.Count),
            };
            statusStrip.States = new[] { game && exeProblem == null ? 1 : 0, exe != null ? 1 : 0, shaderFiles >= 1000 ? 1 : 0, missing.Count == 0 ? 1 : 0 };
            statusStrip.Invalidate();
            if (missing.Count > 0)
            {
                warnText.Text = "⚠  " + L("Running without: ", "A jogar sem: ") + string.Join(" · ", missing.ToArray());
                warnText.ForeColor = Theme.Warn;
                warnFix.Visible = true;
            }
            else
            {
                warnText.Text = "✓  " + L("Windows settings, Resizable BAR and shaders are all set.", "Definicoes do Windows, Resizable BAR e shaders: tudo pronto.");
                warnText.ForeColor = Theme.Dim;
                warnFix.Visible = false;
            }
            warnPanel.Invalidate();
            footerState.Text = state == GameState.Running ? "RUNNING" : state == GameState.Stuck ? "STUCK" : busyNow ? "WORKING" : ready ? "READY" : "NOT READY";
            footerState.ForeColor = state == GameState.Running ? Theme.Cyan : state == GameState.Stuck ? Theme.Warn : busyNow ? Theme.Cyan : ready ? Theme.Good : Theme.Warn;
            footerStatus.Text = string.Format("{0} ({1})  ·  {2:0} GB VRAM  ·  {3:0} GB RAM  ·  {4}", Selected.Name, Selected.Custom ? CustomTag() : Selected.Tag, vramGb, ramGb, EngineName);
        }

        ComboBox outputBox;

        // The output size picked: 1080p, 1440p, 2160p or 720p (launcher.ini keeps the box's index: 3 is 720p).
        int OutputHeight { get { return outputBox == null ? 1080 : outputBox.SelectedIndex == 3 ? 720 : outputBox.SelectedIndex == 2 ? 2160 : outputBox.SelectedIndex == 1 ? 1440 : 1080; } }
        int OutputWidth { get { return OutputHeight * 16 / 9; } }

        // The size the console reports to the game. Never 720p: the game stops on a 1280x720 display mode with its
        // own fatal error (title+0x2219650; every one of 11 sessions at 720p in 0.3.6, players' logs 27/11/2026),
        // and Belobug's engine crashed with a 1280x720 output. "720p" keeps a 1080p screen and draws the world at
        // 720p at most (EffectiveScale).
        int DisplayHeight { get { return OutputHeight == 720 ? 1080 : OutputHeight; } }
        int DisplayWidth { get { return DisplayHeight * 16 / 9; } }

        // At "720p" the world is drawn at the preset's size or 720p, whichever is smaller (the preset's scale and
        // 2/3 of the 1080p screen): Recommended, Sharp and Classic at 1280x720, the 540p presets as they are. null: native.
        string EffectiveScale(string scale)
        {
            if (OutputHeight != 720) return scale;
            double s = 1.0;
            if (scale != null) double.TryParse(scale, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out s);
            if (s <= 0) s = 1.0;
            return Math.Min(s, 2.0 / 3.0).ToString("0.#######", System.Globalization.CultureInfo.InvariantCulture);
        }

        // The edupode engine is the default on every card. On a Radeon, when it ends with an error in the first
        // three minutes twice in a row, the launcher moves to Belobug's AMD engine on its own (built once by
        // Setup if it is not there yet) and says how to go back. One early failure is not enough: a game can
        // end at start once and then run (an RX 6800 player's first session, 27/11/2026).
        void EngineFallback(bool failedEarly)
        {
            if (!failedEarly) { if (ReadSetting("edupode_fails") != "0") WriteSetting("edupode_fails", "0"); return; }
            if (UseAmdEngine || !hasRadeon || hasNvidia) return;
            int fails;
            int.TryParse(ReadSetting("edupode_fails") ?? "0", out fails);
            fails++;
            WriteSetting("edupode_fails", fails.ToString());
            if (fails < 2)
            {
                Log(L("The edupode engine ended at start on this Radeon (1 of 2): one more and the launcher moves to Belobug's AMD engine.",
                      "O motor edupode fechou ao arrancar nesta Radeon (1 de 2): mais uma e o launcher passa para o motor AMD do Belobug."));
                return;
            }
            WriteSetting("edupode_fails", "0");
            WriteSetting("engine", "amd");
            WriteSetting("engine_auto", "1");
            try
            {
                BeginInvoke(new Action(() => {
                    if (engineBox != null) engineBox.SelectedIndex = 1;
                    UpdateBanner(); Refresh2();
                    Log(L("The edupode engine ended at start twice on this Radeon: switched to Belobug's AMD engine.",
                          "O motor edupode fechou ao arrancar duas vezes nesta Radeon: mudei para o motor AMD do Belobug."));
                    MessageBox.Show(this, L("The edupode engine did not start twice in a row on your Radeon, so the launcher switched to Belobug's AMD engine (VIctory-Belobug-AMD, all credit to Belobug). If it is not built yet, Setup builds it now; then press PLAY.\r\n\r\nTo try the edupode engine again: Hardware & ID > Engine. A report ('Send errors') helps us make it start on your card.",
                                            "O motor edupode nao arrancou duas vezes seguidas na tua Radeon, por isso o launcher mudou para o motor AMD do Belobug (VIctory-Belobug-AMD, todo o credito para o Belobug). Se ainda nao estiver compilado, o Preparar compila-o agora; depois carrega em JOGAR.\r\n\r\nPara experimentar o motor edupode outra vez: Hardware e ID > Motor. Um relatorio ('Enviar erros') ajuda-nos a pô-lo a arrancar na tua placa."),
                                    "Julia's Launcher", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    if (VictoryExe == null) { Show(PageSetup); RunSteps(steps.Where(st => st.Fix == (Func<bool>)BuildAmdEngine).ToArray()); }
                }));
            }
            catch (Exception) { }
        }

        // "1280×720 internal upscaled to 1080p · 60 FPS clock" for the preset's heading.
        string PresetSummary()
        {
            if (modeIndex < 0) return "";
            var m = Selected;
            string scale = EffectiveScale(m.Custom ? custom.Scale : m.Scale);
            string pic;
            double sc;
            if (scale != null && double.TryParse(scale, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out sc) && sc < 0.999)
                pic = string.Format(L("{0}×{1} internal upscaled to {2}p", "{0}×{1} interno ampliado para {2}p"), (int)Math.Round(DisplayWidth * sc), (int)Math.Round(DisplayHeight * sc), DisplayHeight);
            else pic = string.Format(L("native {0}p", "{0}p nativo"), DisplayHeight);
            bool sixty = m.Custom ? custom.Fps == 60 : fps60Box.Checked && !m.UltraLow;
            return pic + " · " + (sixty ? L("60 FPS clock", "relogio de 60 FPS") : L("30 FPS clock", "relogio de 30 FPS"));
        }

        void UpdateBanner()
        {
            if (banner == null) return;
            var chips = new List<string>();
            var colors = new List<Color>();
            chips.Add(gpuName); colors.Add(Theme.Dim);
            chips.Add(string.Format("{0:0} GB VRAM", vramGb)); colors.Add(vramGb >= 10 ? Theme.Dim : Theme.Warn);
            chips.Add(string.Format("{0:0} GB RAM", ramGb)); colors.Add(ramGb >= 24 ? Theme.Dim : Theme.Warn);
            chips.Add(RebarText()); colors.Add(rebar ? Theme.Dim : Theme.Warn);
            chips.Add(FrameGenChip()); colors.Add(Theme.Dim);
            if (UseAmdEngine) { chips.Add("AMD · Belobug"); colors.Add(Theme.Cyan); }
            var fg = smoothBox as OptionCard;
            if (fg != null) { fg.Line = FrameGenLine(); fg.Enabled = FrameGenAvailable; fg.Invalidate(); }
            banner.Chips = chips.ToArray();
            banner.ChipColors = colors.ToArray();
            banner.Invalidate();
        }

        // Resizable BAR in one chip: what a run gets (D3D12) and, when it differs, what the card says.
        string RebarText()
        {
            if (rebarHeap == 1) return L("ReBAR On", "ReBAR ligado");
            if (rebarHeap == 0 && rebarBios) return L("ReBAR On (not used by Windows)", "ReBAR ligado (o Windows nao o usa)");
            if (rebarBios) return L("ReBAR On", "ReBAR ligado");
            if (!integratedOnly && RebarCardSupport() == 0) return L("ReBAR n/a (card)", "ReBAR n/d (placa)");
            if (RebarMissing()) return L("ReBAR Off", "ReBAR desligado");
            return rebarHeap == 0 ? L("ReBAR not offered by Windows", "ReBAR nao oferecido pelo Windows") : L("ReBAR ?", "ReBAR ?");
        }

        static string NvidiaSmi
        {
            get
            {
                var mine = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvidia-smi.exe");
                return File.Exists(mine) ? mine : "nvidia-smi";
            }
        }

        void DetectSystem()
        {
            try
            {
                var smi = RunCapture(NvidiaSmi, "--query-gpu=name,memory.total,driver_version --format=csv,noheader,nounits");
                if (!string.IsNullOrEmpty(smi) && smi.Contains(","))
                {
                    // The card with the most memory (a laptop may list more than one).
                    string best = null; double bestMb = -1;
                    foreach (var line in smi.Split('\n'))
                    {
                        var parts = line.Split(',');
                        double mb;
                        if (parts.Length >= 2 && double.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out mb) && mb > bestMb)
                        { bestMb = mb; best = line; }
                    }
                    if (best != null)
                    {
                        var parts = best.Split(',');
                        gpuName = parts[0].Trim().Replace("NVIDIA GeForce ", "");
                        vramGb = bestMb / 1024.0;
                        if (parts.Length > 2) driverVersion = parts[2].Trim();
                    }
                    hasNvidia = true;
                    var q = RunCapture(NvidiaSmi, "-q -d MEMORY");
                    // BAR1 of 1 GB or more: the card and the PC's firmware have Resizable BAR on.
                    foreach (Match m in Regex.Matches(q, @"BAR1 Memory Usage\s+Total\s*:\s*(\d+)\s*MiB", RegexOptions.IgnoreCase))
                    {
                        double bar;
                        if (!double.TryParse(m.Groups[1].Value, out bar)) continue;
                        barRead = true;
                        if (bar >= 1024) rebarBios = true;
                    }
                    rtxFrameGen = Regex.IsMatch(gpuName, @"RTX\s*(40|50)\d\d");
                }
                var mem = new Microsoft.VisualBasic.Devices.ComputerInfo();
                ramGb = mem.TotalPhysicalMemory / 1073741824.0;
            }
            catch (Exception) { }
            DetectCards();
            rebar = rebarBios;
            if (!FrameGenAvailable) smoothBox.Checked = false;
            // Integrated graphics, once: the lightest options whatever an earlier launcher saved (it had read the
            // memory as unknown and turned on Crowds and fights and frame generation).
            if (integratedOnly && ReadSetting("igpu_defaults") != "1")
            {
                fightsBox.Checked = false; smoothBox.Checked = false; fps60Box.Checked = false;
                SelectMode(ModeUltraLow);
                WriteSetting("igpu_defaults", "1");
            }
            // VIctory's own frame generation is experimental: off until the player turns it on.
            else if (OwnFrameGen && ReadSetting("smooth") == null) smoothBox.Checked = false;
            if (modeIndex < 0)
                SelectMode(integratedOnly || (ramGb > 0 && ramGb <= 16.5) || (vramGb > 0 && vramGb <= 6.5) ? ModeUltraLow
                    : (vramGb > 0 && vramGb < 10) || (ramGb > 0 && ramGb < 24) ? 2 : 0);
            UpdateBanner();
            UpdateSmooth();
        }

        static string RunCapture(string file, string args)
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true });
                var s = p.StandardOutput.ReadToEnd();
                p.WaitForExit(5000);
                return s;
            }
            catch (Exception) { return ""; }
        }

        bool busyNow;

        void Log(string line)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string>(Log), line); return; }
            logBox.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + line + "\r\n");
        }

        // ---------------------------------------------------------------- environment
        Dictionary<string, string> Environment2()
        {
            // The AMD fork reads its own switches (Hardware.cs).
            if (UseAmdEngine) return AmdEnvironment();
            var e = new Dictionary<string, string>();
            Action<string, string> set = (k, v) => e[k] = v;
            set("VICTORY_GAME_DIR", gameBox.Text.Trim());
            set("VICTORY_CACHE_DIR", cacheDir);
            set("VICTORY_DATA_DIR", dataDir);
            set("VICTORY_LOG", "warn,victory_gpu::host=info,victory_gpu::copystat=info,victory_gpu::framegen=info");
            // The cgbj base (Play-VIctory.cmd pareto-residency) for a 12 GB card.
            set("VICTORY_DISPLAY_MODE", DisplayWidth + "x" + DisplayHeight);
            set("VICTORY_CARD_BUFFERS_MB", "1024");
            set("VICTORY_PIPELINE_BOOK", "1");
            set("VICTORY_RIPE_BLOCKS", "4");
            set("VICTORY_PRESSURE_FREE_MB", "1024");
            set("VICTORY_PRESSURE_CLEAR_MB", "2048");
            set("VICTORY_TEXTURE_RAM_GUARD", "0");
            set("VICTORY_CARD_ROOM_FOR_TARGETS_MB", "1792");
            set("VICTORY_AMBIENT", "1.6");
            set("VICTORY_TEXTURE_NO_WAIT", "1");
            set("VICTORY_RELAY", "2");
            set("VICTORY_WIDE_BACK", "1");
            set("VICTORY_CARD_REHASH_RUNS", "1");
            set("VICTORY_JOIN_COPIES", "1");
            set("VICTORY_INDIRECT_WINDOW", "1");
            set("VICTORY_RELAY_COPY", "1");
            set("VICTORY_CARD_STEPS", "2");
            set("VICTORY_INDIRECT_BATCH", "2");
            set("VICTORY_MAP_THREADS", "1");
            set("VICTORY_CARD_AWAKE", "1");
            set("VICTORY_SHOW_AFTER", "1");
            set("VICTORY_THIN", "1");
            set("VICTORY_JOIN_PARTS", "8");
            set("VICTORY_UPLOAD_AHEAD", "1");
            set("VICTORY_LEVELS_ON_DEMAND", "1");
            set("VICTORY_CARD_BUFFERS_MIN_KB", "64");
            // The NVIDIA fork's lighting, and the one wave64 water program that translates right.
            set("VICTORY_BVH_MISS", "1");
            set("VICTORY_AMBIENT_AIR", "1.5");
            set("VICTORY_AMBIENT_MOST", "2");
            set("VICTORY_NAN_SCRUB", "1");
            set("VICTORY_DYNAMIC_SLOTS", "524288");
            set("VICTORY_WAVE_WIDE_DIGESTS", "7099a57fded142613b44f410463d6be6");
            var skip = Path.Combine(SourceDir ?? root, "scripts", "rt_off_hashes.txt");
            if (File.Exists(skip)) set("VICTORY_SKIP_PIPELINES", "@" + skip);

            var m = Selected;
            if (m.Scale != null)
            {
                set("VICTORY_RENDER_SCALE", m.Scale);
                set("VICTORY_OWN_UPSCALER", "1");
            }
            if (m.LightTextures)
            {
                set("VICTORY_TEXTURE_MAX", "2048");
                set("VICTORY_LEVELS_ON_DEMAND", "0");
                set("VICTORY_CARD_BUFFERS_MB", "1536");
                set("VICTORY_POOL_FULLEST", "1");
                set("VICTORY_TEXTURE_STAGE_MB", "384");
                if (rebar) set("VICTORY_WINDOWS_BAR_MB", "1024");
            }
            if (m.Modest || integratedOnly || (vramGb > 0 && vramGb < 10))
            {
                // 6-8 GB cards: less of the game's buffers on the card, smaller textures and batches.
                set("VICTORY_CARD_BUFFERS_MB", "768");
                set("VICTORY_TEXTURE_MAX", "1024");
                set("VICTORY_TEXTURE_STAGE_MB", "128");
                set("VICTORY_RIPE_BLOCKS", "2");
                set("VICTORY_CARD_ROOM_FOR_TARGETS_MB", "1024");
                e.Remove("VICTORY_WINDOWS_BAR_MB");
            }
            if (m.Modest || (ramGb > 0 && ramGb < 24))
            {
                // 16 GB of RAM: the memory guards as cgbj has them (texture prefetch held back early).
                e.Remove("VICTORY_PRESSURE_FREE_MB");
                e.Remove("VICTORY_PRESSURE_CLEAR_MB");
                e.Remove("VICTORY_TEXTURE_RAM_GUARD");
            }
            if (m.UltraLow)
            {
                // The least memory: 4-6 GB cards and 8-16 GB of RAM.
                set("VICTORY_CARD_BUFFERS_MB", "512");
                set("VICTORY_TEXTURE_MAX", "512");
                set("VICTORY_TEXTURE_STAGE_MB", "64");
                set("VICTORY_RIPE_BLOCKS", "2");
                set("VICTORY_CARD_ROOM_FOR_TARGETS_MB", "768");
                set("VICTORY_CARD_BUFFERS_MIN_KB", "128");
                set("VICTORY_LEVELS_ON_DEMAND", "1");
                e.Remove("VICTORY_POOL_FULLEST");
                e.Remove("VICTORY_WINDOWS_BAR_MB");
            }
            // 2 GB of the game's buffers only on cards of 11 GB and more: on 8 GB cards it overfilled video
            // memory (6.9 of 7.0 GB, 7 GB spilled to RAM) and the game ran at 3 FPS (players, 27/11/2026).
            if (fightsBox.Checked && !m.Modest && !m.UltraLow && !integratedOnly && vramGb >= 11) set("VICTORY_CARD_BUFFERS_MB", "2048");
            if (panelBox.Checked) set("VICTORY_PANEL", "1");
            if (borderlessBox.Checked) { set("VICTORY_BORDERLESS", "1"); set("VICTORY_BORDERLESS_DPI", "0"); }
            if (fps60Box.Checked && !m.UltraLow) set("VICTORY_FPS_MAX", "60");
            // VIctory's own frame generation, for cards without Smooth Motion or NVSmooth30.
            if (smoothBox.Checked && OwnFrameGen) set("VICTORY_FRAMEGEN", "1");
            // Advanced Tweaks: the player's values over the preset they start from (Custom.cs).
            if (m.Custom) ApplyCustom(e);
            if (OutputHeight == 720)
            {
                string given;
                var s = EffectiveScale(e.TryGetValue("VICTORY_RENDER_SCALE", out given) ? given : null);
                if (s == null) { e.Remove("VICTORY_RENDER_SCALE"); e.Remove("VICTORY_OWN_UPSCALER"); }
                else { set("VICTORY_RENDER_SCALE", s); set("VICTORY_OWN_UPSCALER", "1"); }
            }
            set("VICTORY_VRAM_RESERVE", reserveBox.Checked ? "max" : "0");
            if (cpuBox.Checked)
            {
                // Half the logical cores for the copy and refresh helpers (2-12), the write watch asked early.
                set("VICTORY_WIDE_THREADS", Math.Max(2, Math.Min(12, Environment.ProcessorCount / 2)).ToString());
                set("VICTORY_ASK_EARLY", "2");
            }
            // (Anti-stretch, VICTORY_RELAY=1 + VICTORY_CARD_STEPS=0, is off for everyone: on edupode's RTX 5070 Ti it
            // turned the whole picture cyan and dark without bringing faces back, and an RTX 4070 player ran at
            // 2-3 FPS with it, 27/11/2026. The box is hidden; a saved quality=1 is ignored.)
            if (facesBox.Checked) set("VICTORY_TEXTURE_NO_WAIT", "0");
            if (loadingBox.Checked) set("VICTORY_BAKE_NO_WAIT", "1");
            // The F3 panel's "FRAME GEN" line: the driver's frame generation runs outside the emulator.
            set("VICTORY_PANEL_FRAMEGEN", !smoothBox.Checked ? "OFF" : rtxFrameGen ? "SMOOTH MOTION" : NvSmoothHere ? "NVSMOOTH30" : OwnFrameGen ? "VI 2X" : "OFF");
            ApplyTestEnv(e);
            return e;
        }

        // Test switches, for trying an engine change before a release: %LOCALAPPDATA%\VIctory\test-env.txt, one
        // KEY=VALUE a line (# starts a comment), only VICTORY_* keys; they go over the preset's. Logged at PLAY,
        // and in the game log's summary, so a report says what was on. Delete the file to go back.
        void ApplyTestEnv(Dictionary<string, string> e)
        {
            try
            {
                var file = Path.Combine(Path.GetDirectoryName(dataRoot), "test-env.txt");
                if (!File.Exists(file)) return;
                foreach (var raw in File.ReadAllLines(file))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    var i = line.IndexOf('=');
                    if (i <= 0) continue;
                    var k = line.Substring(0, i).Trim();
                    if (!k.StartsWith("VICTORY_") || k == "VICTORY_GAME_DIR" || k == "VICTORY_DATA_DIR") continue;
                    e[k] = line.Substring(i + 1).Trim();
                }
            }
            catch (Exception) { }
        }

        Process Start(string args, string logFile) { return Start(args, logFile, null); }

        Process Start(string args, string logFile, Action<string> onLine)
        {
            var psi = new ProcessStartInfo(VictoryExe, args) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = UseAmdEngine ? AmdEngineDir : root };
            foreach (var kv in Environment2()) psi.EnvironmentVariables[kv.Key] = kv.Value;
            Directory.CreateDirectory(Path.GetDirectoryName(logFile));
            var writer = new StreamWriter(logFile, true, new UTF8Encoding(false)) { AutoFlush = true };
            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            DataReceivedEventHandler sink = (s, e) => {
                if (e.Data == null) return;
                lock (writer) writer.WriteLine(e.Data);
                if (onLine != null) onLine(e.Data);
            };
            p.OutputDataReceived += sink;
            p.ErrorDataReceived += sink;
            p.Exited += (s, e) => { lock (writer) writer.Dispose(); };
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            return p;
        }

        void Busy(bool busy)
        {
            if (InvokeRequired) { BeginInvoke(new Action<bool>(Busy), busy); return; }
            busyNow = busy;
            playButton.Enabled = !busy; prepareButton.Enabled = !busy; buildButton.Enabled = !busy;
            playButton.Invalidate(); prepareButton.Invalidate(); buildButton.Invalidate();
            if (!busy) Refresh2();
        }

        // ---------------------------------------------------------------- actions
        void Play()
        {
            // Setup still at work (building, translating shaders): two VIctory processes on one cache. An AMD
            // player started the game twice while shaders were being prepared and it ended at start (27/11/2026).
            // Integrated graphics only: asked once, the player decides.
            if (integratedOnly && ReadSetting("igpu_ack") != "1")
            {
                var body = L("Integrated graphics (Intel Iris Xe / UHD, AMD Radeon Graphics) found, and no graphics card of its own.\r\n\r\nThe game is very likely to crash or miss graphics: integrated graphics borrow the PC's RAM, which the game already needs, and the driver refuses some of its shaders. A graphics card with 4 GB or more of its own memory is needed.\r\n\r\nThe launcher has picked Ultra Low with the heavy options off. You can still try.",
                             "Foi encontrada uma grafica integrada (Intel Iris Xe / UHD, AMD Radeon Graphics) e nenhuma placa grafica dedicada.\r\n\r\nE muito provavel que o jogo feche ou que faltem graficos: a grafica integrada usa a RAM do PC, de que o jogo ja precisa, e o driver recusa alguns shaders. E preciso uma placa grafica com 4 GB ou mais de memoria propria.\r\n\r\nO launcher escolheu o Ultra baixo com as opcoes pesadas desligadas. Podes tentar na mesma.",
                             "Graphique intégré trouvé (Intel Iris Xe / UHD, AMD Radeon Graphics), sans carte graphique dédiée.\r\n\r\nLe jeu risque fort de planter ou d'avoir des graphismes manquants : le graphique intégré emprunte la RAM du PC, dont le jeu a déjà besoin, et le pilote refuse certains shaders. Il faut une carte graphique avec 4 Go ou plus de mémoire à elle.\r\n\r\nLe launcher a choisi Ultra bas avec les options lourdes désactivées. Tu peux essayer quand même.");
                var answer = Notice(L("Integrated graphics", "Grafica integrada", "Graphique intégré"), body,
                    L("Play anyway", "Jogar na mesma", "Jouer quand même"), L("Cancel", "Cancelar", "Annuler"));
                if (answer != 1) return;
                WriteSetting("igpu_ack", "1");
            }
            if (setupBusy)
            {
                MessageBox.Show(this, L("Setup is still working (building VIctory or preparing shaders). PLAY opens when it finishes.",
                                        "O Preparar ainda esta a trabalhar (a compilar o VIctory ou a preparar shaders). O JOGAR abre quando acabar."), "Julia's Launcher");
                return;
            }
            SaveSettings();
            var state = State();
            if (state == GameState.Running)
            {
                if (MessageBox.Show(this, L("VIctory is already running. Close it and start again?", "O VIctory ja esta a correr. Fechar e arrancar de novo?"),
                    "Julia's Launcher", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                CloseGames();
            }
            else if (state == GameState.Stuck)
            {
                Log(L("The last session had stopped answering: closing it.", "A ultima sessao tinha deixado de responder: a fecha-la."));
                CloseGames();
            }
            // The pipelines of the last session, still being made: the warm-up before this one makes them too.
            if (afterWarm != null && !afterWarm.HasExited)
                try { RunWait("taskkill", "/PID " + afterWarm.Id + " /T /F", false); } catch (Exception) { }
            afterWarm = null;
            // A game file that cannot be a program (a download that stopped, a placeholder): say so
            // instead of VIctory's "malformed image".
            if (exeProblem != null)
            {
                MessageBox.Show(this, exeProblem, "Julia's Launcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            // What is missing (virtual memory, power, Resizable BAR...): asked once per launcher session.
            var missing = Missing();
            if (missing.Count > 0 && !warnedThisSession)
            {
                var ask = L("The game will run worse (or close) without these:\r\n\r\n", "O jogo vai correr pior (ou fechar) sem isto:\r\n\r\n")
                    + string.Join("\r\n", missing.Select(x => "  •  " + x).ToArray())
                    + L("\r\n\r\nYes: play anyway.   No: open Setup to fix them.", "\r\n\r\nSim: jogar na mesma.   Nao: abrir o Preparar para corrigir.");
                if (MessageBox.Show(this, ask, "Julia's Launcher", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) { Show(PageSetup); return; }
                warnedThisSession = true;
            }
            // Too little memory to promise: the window would stay white (Session.cs).
            if (!EnoughMemory()) { Show(PagePerf); return; }
            // Asked on every PLAY until the player shares or says not to ask again (Hardware.cs).
            AskToShare();
            // Once: a log of each game sent when it closes (SessionLogs.cs).
            AskSessionLogs();
            // The saves as they are, before the game can change them (Saves.cs).
            BackupSavesBeforePlay();
            long cardUsed;
            GpuUsers(out cardUsed);
            if (cardUsed > (2L << 30))
                Log(string.Format(L("Other apps already hold {0:0.0} GB of the card: the Performance page frees it.", "Outras apps ja ocupam {0:0.0} GB da placa: a pagina Desempenho liberta-a."), cardUsed / 1073741824.0));
            ApplyFrameGen();
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var log = Path.Combine(dataRoot, "logs", "session-" + stamp + ".log");
            Log(L("Starting ", "A iniciar ") + Selected + ". Log: " + log);
            Busy(true);
            var game = GameExe;
            bool warmFirst = warmBox.Checked;
            new Thread(() => {
                try
                {
                    if (warmFirst) WarmWithBar(log);
                    var started = DateTime.Now;
                    var p = Start("run \"" + game + "\" --console series-s --execute", log);
                    gameProc = p; gameHadWindow = false; windowGone = null;
                    WatchStart(log, p);
                    // From here the game's own state keeps PLAY off (RUNNING), not the launcher's task,
                    // so a game that ends badly never leaves PLAY waiting.
                    Busy(false);
                    p.WaitForExit();
                    // NVSmooth30's version.dll is there for the game only (tools must not load it).
                    RemoveNvSmooth();
                    int code = 0;
                    try { code = p.ExitCode; } catch (Exception) { }
                    SendSessionLog(log, code, started, DateTime.Now);
                    if (gameProc != p) return; // closed from the launcher (Close game, or PLAY again)
                    gameProc = null;
                    if (code != 0)
                        Log(string.Format(L("The game ended with an error (code {0}). Log: {1}", "O jogo terminou com um erro (codigo {0}). Log: {1}"), code, log));
                    EngineFallback(code != 0 && (DateTime.Now - started).TotalMinutes < 3);
                    Log(L("Game closed. Preparing this session's pipelines for the next one...", "Jogo fechado. A preparar as pipelines desta sessao para a proxima..."));
                    var w = Start("warm --used", log);
                    afterWarm = w;
                    BeginInvoke(new Action(Refresh2));
                    // Stops on its own if it runs past 10 minutes; PLAY ends it too.
                    if (!w.WaitForExit(10 * 60 * 1000))
                        try { RunWait("taskkill", "/PID " + w.Id + " /T /F", false); } catch (Exception) { }
                    if (afterWarm == w) { afterWarm = null; Log(L("Done.", "Feito.")); }
                }
                catch (Exception ex)
                {
                    Log(L("Could not start: ", "Nao arrancou: ") + ex.Message + (ex.Message.Contains("denied") ? L("  (Defender: see Help)", "  (Defender: ve a Ajuda)") : ""));
                }
                finally
                {
                    if (busyNow) Busy(false);
                    BeginInvoke(new Action(Refresh2));
                }
            }) { IsBackground = true }.Start();
        }

        // Makes the pipelines earlier sessions drew with (victory warm --used) and shows how far it is.
        void WarmWithBar(string log)
        {
            int total = 0;
            BeginInvoke(new Action(() => { loadBar.Visible = true; loadBar.Set(0, L("Loading pipelines...", "A carregar pipelines...")); }));
            var w = Start("warm --used", log, line => {
                var m = Regex.Match(line, @"; (\d+) to make");
                if (m.Success) total = int.Parse(m.Groups[1].Value);
                var p = Regex.Match(line, @"^(\d+) of (\d+), (\d+) s");
                if (p.Success)
                {
                    int n = int.Parse(p.Groups[1].Value), of = int.Parse(p.Groups[2].Value);
                    BeginInvoke(new Action(() => loadBar.Set(of > 0 ? (float)n / of : 1f, string.Format(L("Loading pipelines: {0} of {1}", "A carregar pipelines: {0} de {1}"), n, of))));
                    Stage(1, of > 0 ? (float)n / of : 1f, string.Format(L("Pipelines: {0:N0} of {1:N0} loaded before the game · {2} s", "Pipelines: {0:N0} de {1:N0} carregadas antes do jogo · {2} s"), n, of, p.Groups[3].Value), true);
                }
                else if (m.Success)
                    BeginInvoke(new Action(() => loadBar.Set(total == 0 ? 1f : 0f, total == 0 ? L("Pipelines ready.", "Pipelines prontas.") : string.Format(L("Loading pipelines: 0 of {0}", "A carregar pipelines: 0 de {0}"), total))));
            });
            w.WaitForExit();
            Stage(1, 1f, "", false);
            Log(total == 0 ? L("Pipelines were already ready.", "As pipelines ja estavam prontas.") : string.Format(L("{0} pipelines loaded.", "{0} pipelines carregadas."), total));
            BeginInvoke(new Action(() => { loadBar.Set(1f, L("Starting the game...", "A abrir o jogo...")); }));
            Thread.Sleep(600);
            BeginInvoke(new Action(() => { loadBar.Visible = false; }));
        }

        void Prepare()
        {
            RunSteps(steps.Where(st => st.Fix == (Func<bool>)PrepareBlocking).ToArray());
        }

        // Build: Rust and the C++ tools first when they are missing (the build alone "did nothing"
        // without them), then the emulator (or the AMD engine on an AMD card).
        void Build()
        {
            Show(PageSetup);
            var chain = steps.Take(4).Where(st => { int s; try { s = st.Check(); } catch (Exception) { s = 0; } return s == 0; }).ToList();
            var build = steps.First(st => st.Fix == (Func<bool>)(UseAmdEngine ? (Func<bool>)BuildAmdEngine : BuildBlocking));
            if (!chain.Contains(build)) chain.Add(build);
            RunSteps(chain.ToArray());
        }

        // ---------------------------------------------------------------- settings
        string ReadSetting(string key)
        {
            try
            {
                if (!File.Exists(settingsFile)) return null;
                foreach (var line in File.ReadAllLines(settingsFile))
                    if (line.StartsWith(key + "=")) return Unmangle(line.Substring(key.Length + 1));
            }
            catch (Exception) { }
            return null;
        }

        // A path like C:\Users\Ã©lÃ¨ve is UTF-8 read back as Windows-1252 (once or more): turn it back into
        // C:\Users\élève, or the game cannot create its folders there (Access denied).
        static string Unmangle(string s)
        {
            if (s == null) return null;
            var w1252 = System.Text.Encoding.GetEncoding(1252);
            for (int i = 0; i < 4 && (s.IndexOf('Ã') >= 0 || s.IndexOf('Â') >= 0); i++)
            {
                var t = System.Text.Encoding.UTF8.GetString(w1252.GetBytes(s));
                if (t == s || t.IndexOf('\uFFFD') >= 0) break;
                s = t;
            }
            return s;
        }

        // The cache's path must be plain (no accents): with a profile like C:\Users\élève it goes to C:\VIctory\cache.
        string DefaultCacheDir()
        {
            var home = Path.Combine(dataRoot, "cache");
            return home.All(c => c < 128) ? home : Path.Combine(Path.GetPathRoot(dataRoot), "VIctory", "cache");
        }

        void LoadSettings()
        {
            try
            {
                if (!File.Exists(settingsFile)) return;
                foreach (var line in File.ReadAllLines(settingsFile))
                {
                    var i = line.IndexOf('=');
                    if (i < 0) continue;
                    var k = line.Substring(0, i); var v = Unmangle(line.Substring(i + 1));
                    switch (k)
                    {
                        case "game": gameBox.Text = v; break;
                        case "exe": exeChoice = v.Length > 0 ? v : null; break;
                        case "mode": int m; if (int.TryParse(v, out m)) SelectMode(m); break;
                        case "panel": panelBox.Checked = v == "1"; break;
                        case "borderless": borderlessBox.Checked = v == "1"; break;
                        case "fps60": fps60Box.Checked = v == "1"; break;
                        case "reserve": reserveBox.Checked = v == "1"; break;
                        case "smooth": smoothBox.Checked = v == "1"; break;
                        case "quality": qualityBox.Checked = false; break;
                        case "faces": facesBox.Checked = v == "1"; break;
                        case "loading": loadingBox.Checked = v == "1"; break;
                        case "fights": fightsBox.Checked = v == "1"; break;
                        case "warm": warmBox.Checked = v == "1"; break;
                        case "cpu": cpuBox.Checked = v == "1"; break;
                        case "output": { int o; if (outputBox != null && int.TryParse(v, out o) && o >= 0 && o <= 3) outputBox.SelectedIndex = o; } break;
                    }
                }
            }
            catch (Exception) { }
        }

        void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(settingsFile));
                Func<CheckBox, string> b = c => c.Checked ? "1" : "0";
                var mine = new[] {
                    "lang=" + (Pt ? "pt" : Fr ? "fr" : "en"), "cache=" + cacheDir, "data=" + dataDir,
                    "game=" + gameBox.Text.Trim(), "exe=" + (exeChoice ?? ""), "mode=" + modeIndex,
                    "panel=" + b(panelBox), "borderless=" + b(borderlessBox), "fps60=" + b(fps60Box), "reserve=" + b(reserveBox),
                    "smooth=" + b(smoothBox), "quality=" + b(qualityBox), "faces=" + b(facesBox), "loading=" + b(loadingBox), "fights=" + b(fightsBox), "warm=" + b(warmBox), "cpu=" + b(cpuBox), "output=" + (outputBox == null ? 0 : outputBox.SelectedIndex),
                };
                // The other keys (shaders=, engine=, custom=, source=...) are kept: this used to drop them.
                var keys = new HashSet<string>(mine.Select(l => l.Substring(0, l.IndexOf('=') + 1)));
                var others = File.Exists(settingsFile) ? File.ReadAllLines(settingsFile).Where(l => l.Contains("=") && !keys.Contains(l.Substring(0, l.IndexOf('=') + 1))) : new string[0];
                File.WriteAllLines(settingsFile, mine.Concat(others).ToArray());
            }
            catch (Exception) { }
        }
    }
}
