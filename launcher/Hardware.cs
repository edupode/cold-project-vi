// Julia's Launcher: the PC's hardware, its permanent ID, the hardware-only report (opt-in, write-only),
// the engine (edupode for every card; Belobug's AMD fork as the Radeon alternative), and frame generation for each card.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace VIctoryLauncher
{
    partial class LauncherForm
    {
        // ---------------------------------------------------------------- cards and engines
        bool hasNvidia, hasRadeon, rtx30;
        // Every adapter DXGI reports, and whether the PC has only integrated graphics (Intel Iris Xe / UHD, an
        // AMD APU's Radeon Graphics): no memory of its own, far below what the game needs (Gpus.cs).
        List<GpuAdapter> adapters = new List<GpuAdapter>();
        bool integratedOnly;
        string driverVersion = "";
        string radeonName = "";

        // Belobug's AMD fork of VIctory (GPL-3.0): github.com/Belobug/VIctory-Belobug-AMD.
        public const string AmdRepo = "https://github.com/Belobug/VIctory-Belobug-AMD";
        public const string AmdBranch = "a-publier";
        // The commit the launcher was checked with; Setup builds this one.
        public const string AmdCommit = "4eb6cfdf9e61cb112f1f811344ec0dd309eddd6c";

        // Beside the other data, so it never depends on where the launcher sits (Downloads, Program Files).
        string AmdEngineDir { get { return Path.Combine(StorageRoot ?? Path.GetDirectoryName(dataRoot), "engines", "amd"); } }

        // launcher.ini's engine=: edupode (the default, every card: the least video memory and the most
        // stable), or amd (Belobug's AMD engine, the alternative for Radeon cards, chosen by the player).
        // The AMD engine is for Radeon cards only: picked on an NVIDIA card it crashed at start (an RTX 4080 SUPER, 27/11/2026).
        string EngineChoice { get { return ReadSetting("engine") == "amd" && !(hasNvidia && !hasRadeon) ? "amd" : "edupode"; } }

        bool UseAmdEngine { get { return EngineChoice == "amd"; } }

        string EngineName { get { return UseAmdEngine ? "AMD · Belobug" : "edupode"; } }

        // The graphics cards Windows lists (names only).
        void DetectCards()
        {
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Name, AdapterCompatibility FROM Win32_VideoController"))
                    foreach (ManagementObject o in s.Get())
                    {
                        var name = (o["Name"] as string ?? "").Trim();
                        if (name.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0) hasNvidia = true;
                        // A Radeon card of its own (RX, Pro), not the graphics inside a Ryzen processor.
                        if (Regex.IsMatch(name, @"Radeon\s*(RX|Pro|VII)", RegexOptions.IgnoreCase)) { hasRadeon = true; radeonName = name; }
                    }
            }
            catch (Exception) { }
            adapters = Gpus.List();
            integratedOnly = adapters.Count > 0 && adapters.TrueForAll(a => a.Integrated);
            GpuAdapter best = null;
            foreach (var a in adapters)
                if (best == null || (best.Integrated && !a.Integrated) || (best.Integrated == a.Integrated && a.DedicatedMb > best.DedicatedMb)) best = a;
            if (best != null && vramGb <= 0)
            {
                // Without nvidia-smi (AMD, Intel): the adapter's own memory, never "unknown" read as large.
                vramGb = best.DedicatedMb / 1024.0;
                if (!hasNvidia && !hasRadeon) gpuName = best.Name;
            }
            rtx30 = Regex.IsMatch(gpuName, @"RTX\s*30\d\d");
            if (!hasNvidia && hasRadeon) gpuName = radeonName.Replace("AMD ", "");
        }

        // `victory gpu` (this repository's build): the card a run gets and whether D3D12 offers it
        // Resizable BAR (GPU upload heaps). Without the build, the BIOS side (nvidia-smi) stands.
        void ProbeGpu()
        {
            var exe = OwnVictoryExe;
            if (exe == null) return;
            new Thread(() => {
                var text = RunCapture(exe, "gpu");
                var kv = new Dictionary<string, string>();
                foreach (var line in text.Split('\n'))
                {
                    var i = line.IndexOf('=');
                    if (i > 0) kv[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
                }
                string v;
                if (!kv.TryGetValue("gpu_upload_heap", out v)) return;
                try
                {
                    BeginInvoke(new Action(() => {
                        rebarHeap = v == "true" ? 1 : 0;
                        string known;
                        probeOptions16 = kv.TryGetValue("options16_known", out known) && known == "true";
                        kv.TryGetValue("adapter", out probeAdapter);
                        kv.TryGetValue("vendor", out probeVendor);
                        probeAdapter = probeAdapter ?? ""; probeVendor = probeVendor ?? "";
                        string mb;
                        double d;
                        if (vramGb <= 0 && kv.TryGetValue("dedicated_mb", out mb) && double.TryParse(mb, out d)) vramGb = d / 1024.0;
                        if (gpuName == "unknown GPU" && probeAdapter.Length > 0) gpuName = probeAdapter;
                        rebar = rebarHeap == 1;
                        UpdateBanner();
                        Refresh2();
                        Log(string.Format(L("Graphics card for VIctory: {0}; Resizable BAR for the game: {1}.", "Placa grafica do VIctory: {0}; Resizable BAR para o jogo: {1}."),
                            probeAdapter, rebarHeap == 1 ? L("yes", "sim") : rebarBios ? L("on in the BIOS, not offered by Windows (see Setup)", "ligado na BIOS, o Windows nao o oferece (ve o Preparar)") : L("no", "nao")));
                    }));
                }
                catch (Exception) { }
            }) { IsBackground = true }.Start();
        }

        // Windows' graphics preference for victory.exe: on a laptop with two cards the run must get
        // the dedicated one (`victory gpu` names the one it gets).
        bool GpuPreferenceOk()
        {
            if (probeAdapter.Length == 0) return true;
            if (hasNvidia && probeAdapter.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) < 0) return false;
            if (!hasNvidia && hasRadeon && !Regex.IsMatch(probeAdapter, @"Radeon\s*(RX|Pro|VII)", RegexOptions.IgnoreCase)) return false;
            return true;
        }

        // "GpuPreference=2;" (high performance) for every victory.exe the launcher may start.
        bool SetGpuPreference()
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences"))
                    foreach (var exe in OwnExeCandidates.Concat(new[] {
                                                Path.Combine(AmdEngineDir, "target", "diag", "victory.exe"), Path.Combine(BuildTarget(AmdEngineDir), "diag", "victory.exe"), Path.Combine(AmdEngineDir, "victory.exe") }))
                        k.SetValue(exe, "GpuPreference=2;");
                Log(L("Windows now gives victory.exe the high-performance graphics card (restart the game to apply).", "O Windows da agora ao victory.exe a placa grafica de alto desempenho (reinicia o jogo para aplicar)."));
                return true;
            }
            catch (Exception e) { Log("GPU preference: " + e.Message); return false; }
        }

        bool GpuPreferenceSet()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences"))
                {
                    var exe = VictoryExe;
                    if (k == null || exe == null) return false;
                    var v = k.GetValue(exe) as string;
                    return v != null && v.Contains("GpuPreference=2");
                }
            }
            catch (Exception) { return false; }
        }

        // The AMD fork's switches (its README): the renderer patches its measured run had on, the
        // frame interval that lets the title pass 30 FPS, keyboard and mouse for GTA, 1080p output.
        Dictionary<string, string> AmdEnvironment()
        {
            var e = new Dictionary<string, string>();
            Action<string, string> set = (k, v) => e[k] = v;
            set("VICTORY_GAME_DIR", gameBox.Text.Trim());
            set("VICTORY_CACHE_DIR", Path.Combine(dataRoot, "cache-amd"));
            set("VICTORY_DATA_DIR", dataDir);
            set("VICTORY_LOG", "warn");
            // The pipelines each game uses are written down, so "Load pipelines before the game" (warm --used)
            // has something to make: without it the AMD engine's warm-up made 0 and every pipeline was
            // compiled during play (hundreds of draws dropped a frame for the first minutes).
            set("VICTORY_PIPELINE_BOOK", "1");
            set("VICTORY_PORT_CONSTANTS", "1");
            set("VICTORY_PORT_DESCRIPTORS", "1");
            set("VICTORY_PORT_CBV", "1");
            set("VICTORY_PORT_GUARD", "1");
            set("VICTORY_PORT_JOURNAL", "1");
            // At most 1080p: Belobug's engine stops at start with a 1440p or 4K output (the game's fatal error at
            // title+0x56a2afd, 7 sessions on RX 6800 and RTX 4080 SUPER, 27/11/2026).
            int h = Math.Min(DisplayHeight, 1080), w = h * 16 / 9;
            set("VICTORY_OUTPUT", w + "x" + h);
            set("VICTORY_WINDOW_MAX", w + "x" + h);
            set("VICTORY_KBM_GTA", "1");
            // AMD cards run 64-lane waves natively: the wave-wide form is the console's own.
            set("VICTORY_WAVE_WIDE", "1");
            if (fps60Box.Checked && !Selected.UltraLow) set("VICTORY_FRAME_INTERVAL_MS", "16.667");
            // The switches both builds share.
            if (panelBox.Checked) set("VICTORY_PANEL", "1");
            if (borderlessBox.Checked) { set("VICTORY_BORDERLESS", "1"); set("VICTORY_BORDERLESS_DPI", "0"); }
            var m = Selected;
            var scale = EffectiveScale(m.Custom ? custom.Scale : m.Scale);
            if (scale != null && scale != "1.0") { set("VICTORY_RENDER_SCALE", scale); set("VICTORY_OWN_UPSCALER", "1"); }
            return e;
        }

        // ---------------------------------------------------------------- frame generation, per card
        string NvSmoothDll { get { return Path.Combine(AppDir, "tools", "nvsmooth30", "version.dll"); } }

        // Which frame generation this PC gets: NVIDIA Smooth Motion (RTX 40/50), NVSmooth30 (RTX 30,
        // when the release brought it), else VIctory's own (VICTORY_FRAMEGEN, any card; this
        // repository's engine only, the AMD engine does not have it yet).
        bool NvSmoothHere { get { return rtx30 && File.Exists(NvSmoothDll); } }
        bool OwnFrameGen { get { return !rtxFrameGen && !NvSmoothHere && !UseAmdEngine; } }
        // (VIctory's own frame generation needs memory an integrated GPU does not have.)
        // "dedicated, 12.0 GB of its own" / "integrated, 0.1 GB of its own (shares RAM)", for the hardware page and reports.
        string GpuKind()
        {
            GpuAdapter best = null;
            foreach (var a in adapters) if (best == null || (best.Integrated && !a.Integrated) || (best.Integrated == a.Integrated && a.DedicatedMb > best.DedicatedMb)) best = a;
            double own = best != null ? Math.Max(vramGb, best.DedicatedMb / 1024.0) : vramGb;
            return integratedOnly
                ? string.Format(L("integrated, {0:0.0} GB of its own (shares RAM)", "integrada, {0:0.0} GB proprios (partilha a RAM)", "intégrée, {0:0.0} Go à elle (partage la RAM)"), own)
                : string.Format(L("dedicated, {0:0.0} GB of its own", "dedicada, {0:0.0} GB proprios", "dédiée, {0:0.0} Go à elle"), own);
        }

        bool FrameGenAvailable { get { return rtxFrameGen || NvSmoothHere || (OwnFrameGen && !integratedOnly); } }

        string FrameGenLine()
        {
            if (rtxFrameGen) return L("NVIDIA Smooth Motion (RTX 40/50, set up for you)", "NVIDIA Smooth Motion (RTX 40/50, configurado por ti)");
            if (NvSmoothHere) return L("NVSmooth30 for RTX 30 (experimental)", "NVSmooth30 para RTX 30 (experimental)");
            if (OwnFrameGen) return L("VI frame generation: any card, a frame between each two (experimental)", "Geracao de frames VI: qualquer placa, um frame entre cada dois (experimental)");
            return L("Not in the AMD engine yet (pick the edupode engine on Hardware & ID to try it)", "Ainda nao no motor AMD (escolhe o motor edupode em Hardware e ID para experimentar)");
        }

        string FrameGenChip()
        {
            if (rtxFrameGen) return "Smooth Motion " + (smoothState == "on" ? L("On", "ligado") : L("ready", "pronto"));
            if (NvSmoothHere) return "NVSmooth30 " + (smoothBox.Checked ? L("On", "ligado") : L("off", "desligado"));
            if (OwnFrameGen) return L("VI frame gen ", "Geracao VI ") + (smoothBox.Checked ? L("On", "ligada") : L("off", "desligada"));
            return L("Frame gen n/a", "Geracao de frames n/d");
        }

        // Before PLAY: the driver profile (Smooth Motion), and on RTX 30 NVSmooth30's version.dll
        // beside victory.exe (taken away again when the game closes: RemoveNvSmooth).
        void ApplyFrameGen()
        {
            if (rtxFrameGen)
            {
                if (smoothBox.Checked && smoothState != "on") ApplySmooth(true, false);
                else if (!smoothBox.Checked && smoothState == "on") ApplySmooth(false, false);
                return;
            }
            if (!rtx30 || !File.Exists(NvSmoothDll) || VictoryExe == null) return;
            var beside = Path.Combine(Path.GetDirectoryName(VictoryExe), "version.dll");
            try
            {
                if (smoothBox.Checked)
                {
                    File.Copy(NvSmoothDll, beside, true);
                    if (smoothState != "on") ApplySmooth(true, false);
                    Log(L("NVSmooth30 (experimental) is on for this session.", "NVSmooth30 (experimental) ligado nesta sessao."));
                }
                else RemoveNvSmooth();
            }
            catch (Exception e) { Log("NVSmooth30: " + e.Message); }
        }

        void RemoveNvSmooth()
        {
            try
            {
                if (VictoryExe == null) return;
                var beside = Path.Combine(Path.GetDirectoryName(VictoryExe), "version.dll");
                if (File.Exists(beside)) File.Delete(beside);
            }
            catch (Exception) { }
        }

        // ---------------------------------------------------------------- hardware, ID and the report
        class HardwareInfo
        {
            public string Id = "", Gpu = "", GpuVendor = "", Driver = "", Cpu = "", Board = "", BoardMaker = "", Os = "", Disk = "";
            public int VramMb, RamGb, Cores, Threads;
            public bool Laptop, RebarBios;
            public int RebarHeap = -1;
        }

        HardwareInfo hw;
        Label idLabel;
        InfoCard hwText;
        TextBox hwJson;
        CheckBox shareBox;
        Label shareStatus;
        ComboBox engineBox;

        // Where the hardware report goes (server\hardware-worker). Empty: the database is not online
        // yet, and nothing is sent.
        public const string HardwareEndpoint = "https://vi-hardware.edupodept.workers.dev/v1/hw";

        static string Wmi(string query, string field)
        {
            try
            {
                using (var s = new ManagementObjectSearcher(query))
                    foreach (ManagementObject o in s.Get())
                    {
                        var v = o[field];
                        if (v != null && v.ToString().Trim().Length > 0) return v.ToString().Trim();
                    }
            }
            catch (Exception) { }
            return "";
        }

        static string WmiIn(string scope, string query, string field)
        {
            try
            {
                using (var s = new ManagementObjectSearcher(new ManagementScope(scope), new ObjectQuery(query)))
                    foreach (ManagementObject o in s.Get())
                    {
                        var v = o[field];
                        if (v != null) return v.ToString().Trim();
                    }
            }
            catch (Exception) { }
            return "";
        }

        HardwareInfo ReadHardware()
        {
            var h = new HardwareInfo();
            // The ID: a hash of parts that stay when Windows is installed again (the board's UUID and
            // serial, the CPU's ID); none of them leaves the PC.
            var uuid = Wmi("SELECT UUID FROM Win32_ComputerSystemProduct", "UUID");
            var serial = Wmi("SELECT SerialNumber FROM Win32_BaseBoard", "SerialNumber");
            var cpuId = Wmi("SELECT ProcessorId FROM Win32_Processor", "ProcessorId");
            h.Id = MakeId(uuid + "|" + serial + "|" + cpuId);
            h.Gpu = hasNvidia || !hasRadeon ? gpuName : radeonName;
            h.GpuVendor = hasNvidia ? "NVIDIA" : hasRadeon ? "AMD" : probeVendor;
            h.Driver = driverVersion;
            h.VramMb = (int)(vramGb * 1024);
            h.RebarBios = rebarBios;
            h.RebarHeap = rebarHeap;
            h.Cpu = Wmi("SELECT Name FROM Win32_Processor", "Name");
            int n;
            if (int.TryParse(Wmi("SELECT NumberOfCores FROM Win32_Processor", "NumberOfCores"), out n)) h.Cores = n;
            if (int.TryParse(Wmi("SELECT NumberOfLogicalProcessors FROM Win32_Processor", "NumberOfLogicalProcessors"), out n)) h.Threads = n;
            h.RamGb = (int)Math.Round(ramGb);
            h.BoardMaker = Wmi("SELECT Manufacturer FROM Win32_BaseBoard", "Manufacturer");
            h.Board = Wmi("SELECT Product FROM Win32_BaseBoard", "Product");
            h.Os = Wmi("SELECT Caption FROM Win32_OperatingSystem", "Caption") + " " + Wmi("SELECT BuildNumber FROM Win32_OperatingSystem", "BuildNumber");
            // The system drive's kind: 3 HDD, 4 SSD (Storage's MSFT_PhysicalDisk).
            var media = WmiIn(@"\\.\root\Microsoft\Windows\Storage", "SELECT MediaType FROM MSFT_PhysicalDisk WHERE DeviceId='0'", "MediaType");
            h.Disk = media == "4" ? "SSD" : media == "3" ? "HDD" : "unknown";
            var chassis = Wmi("SELECT ChassisTypes FROM Win32_SystemEnclosure", "ChassisTypes");
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT ChassisTypes FROM Win32_SystemEnclosure"))
                    foreach (ManagementObject o in s.Get())
                    {
                        var types = o["ChassisTypes"] as ushort[];
                        if (types != null && types.Any(t => t == 8 || t == 9 || t == 10 || t == 14 || t == 31 || t == 32)) h.Laptop = true;
                    }
            }
            catch (Exception) { }
            return h;
        }

        // VI-XXXX-XXXX: the first 40 bits of a SHA-256 in base 32 (no 0/O, 1/I).
        static string MakeId(string parts)
        {
            const string digits = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            byte[] hash;
            using (var sha = SHA256.Create()) hash = sha.ComputeHash(Encoding.UTF8.GetBytes(parts + "|cold-project-vi"));
            ulong v = 0;
            for (int i = 0; i < 5; i++) v = v << 8 | hash[i];
            var sb = new StringBuilder("VI-");
            for (int i = 7; i >= 0; i--)
            {
                sb.Append(digits[(int)(v >> (i * 5) & 31)]);
                if (i == 4) sb.Append('-');
            }
            return sb.ToString();
        }

        static string Json(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (var c in s ?? "")
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < 32) sb.AppendFormat("\\u{0:x4}", (int)c);
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        // Exactly what the report holds: hardware and the ID, nothing else.
        static string HardwareJson(HardwareInfo h)
        {
            return "{\n"
                + "  \"schema\": 1,\n"
                + "  \"id\": " + Json(h.Id) + ",\n"
                + "  \"gpu\": " + Json(h.Gpu) + ",\n"
                + "  \"gpu_vendor\": " + Json(h.GpuVendor) + ",\n"
                + "  \"gpu_driver\": " + Json(h.Driver) + ",\n"
                + "  \"vram_mb\": " + h.VramMb + ",\n"
                + "  \"rebar_bios\": " + (h.RebarBios ? "true" : "false") + ",\n"
                + "  \"rebar_d3d12\": " + (h.RebarHeap == 1 ? "true" : h.RebarHeap == 0 ? "false" : "null") + ",\n"
                + "  \"cpu\": " + Json(h.Cpu) + ",\n"
                + "  \"cpu_cores\": " + h.Cores + ",\n"
                + "  \"cpu_threads\": " + h.Threads + ",\n"
                + "  \"ram_gb\": " + h.RamGb + ",\n"
                + "  \"board_maker\": " + Json(h.BoardMaker) + ",\n"
                + "  \"board\": " + Json(h.Board) + ",\n"
                + "  \"os\": " + Json(h.Os) + ",\n"
                + "  \"disk\": " + Json(h.Disk) + ",\n"
                + "  \"laptop\": " + (h.Laptop ? "true" : "false") + "\n"
                + "}";
        }

        // A random secret kept only on this PC: the database takes a newer report for this ID only
        // with it, so knowing someone's ID is not enough to change their entry.
        string ShareSecret()
        {
            var file = Path.Combine(Path.GetDirectoryName(dataRoot), "hardware-secret.txt");
            try
            {
                if (File.Exists(file)) { var s = File.ReadAllText(file).Trim(); if (s.Length == 64) return s; }
                var bytes = new byte[32];
                using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
                var hex = string.Concat(bytes.Select(b => b.ToString("x2")).ToArray());
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllText(file, hex);
                return hex;
            }
            catch (Exception) { return null; }
        }

        // Sends the report (POST only; the answer is read for its status and nothing else), at most
        // once a day, when the player turned sharing on.
        void StartHardwareShare()
        {
            if (ReadSetting("share") != "1" || HardwareEndpoint.Length == 0) return;
            var last = ReadSetting("share_sent");
            if (last == DateTime.UtcNow.ToString("yyyy-MM-dd")) return;
            new Thread(() => {
                try
                {
                    Thread.Sleep(15000);
                    var h = ReadHardware();
                    var secret = ShareSecret();
                    if (secret == null) return;
                    ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
                    var req = (HttpWebRequest)WebRequest.Create(HardwareEndpoint);
                    req.Method = "POST";
                    req.ContentType = "application/json";
                    req.UserAgent = "Julias-Launcher";
                    req.Timeout = 15000;
                    req.Headers["X-VI-Secret"] = secret;
                    var body = Encoding.UTF8.GetBytes(HardwareJson(h));
                    req.ContentLength = body.Length;
                    using (var s = req.GetRequestStream()) s.Write(body, 0, body.Length);
                    using (var r = (HttpWebResponse)req.GetResponse())
                        if ((int)r.StatusCode / 100 == 2) WriteSetting("share_sent", DateTime.UtcNow.ToString("yyyy-MM-dd"));
                }
                catch (Exception) { }
            }) { IsBackground = true }.Start();
        }

        // A label gets the height its text needs at its width (never less than a few lines) and its
        // bottom edge comes back, so the buttons under it never overlap long Portuguese or French text.
        static int FitText(Label l, int top)
        {
            int need = TextRenderer.MeasureText(l.Text ?? "", l.Font, new Size(l.Width, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height + 8;
            l.Height = Math.Max(60, need);
            return top + l.Height;
        }

        // The question before each game while sharing is off: why it helps, what the list is, what
        // goes and what never does. "Don't ask again" ends it; Hardware & ID changes it any time.
        void AskToShare()
        {
            // Never asked while the database is not online (HardwareEndpoint empty): nothing would be sent.
            if (HardwareEndpoint.Length == 0 || ReadSetting("share") == "1" || ReadSetting("share_ask") == "never") return;
            var f = new Form { Text = L("Help the game run on more PCs", "Ajuda o jogo a correr em mais PCs"), ClientSize = new Size(660, 520), BackColor = Theme.Back, ForeColor = Theme.Text,
                Font = Theme.Ui(9.75f), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, Icon = Icon };
            f.Controls.Add(new Heading(L("Share your hardware?", "Partilhar o teu hardware?")) { Location = new Point(20, 16), Width = 620 });
            var text = new Label { Location = new Point(20, 50), Size = new Size(620, 390), Font = f.Font, ForeColor = Theme.Text, BackColor = Theme.Back };
            text.Text = string.Join("\r\n", Pt ? new[] {
                "Podes deixar o Julia's Launcher enviar o hardware do teu PC ao edupode?",
                "",
                "PORQUE AJUDA",
                "   Vemos em que placas graficas, processadores e quantidades de memoria as pessoas jogam de verdade.",
                "   Isso decide que predefinicoes vem primeiro, que correcoes se fazem a seguir (por exemplo o Ultra baixo",
                "   para PCs de 16 GB, o suporte AMD) e em que placas se testa a geracao de frames.",
                "",
                "A LISTA",
                "   Cada PC que partilha e uma linha, sem nome. So o edupode ve a lista.",
                "   Uma pagina publica mostra so as medias (quantos PCs, RAM e VRAM medias, as placas mais comuns),",
                "   atualizada de 6 em 6 horas: docs/HARDWARE-STATS.md no GitHub.",
                "",
                "O QUE E ENVIADO",
                "   Placa grafica e driver, VRAM, Resizable BAR, processador, RAM, modelo da placa-mae,",
                "   versao do Windows, tipo de disco, se e portatil, e o teu ID VI. No maximo uma vez por dia.",
                "",
                "NUNCA E ENVIADO",
                "   Logs, definicoes, caminhos de ficheiros, o teu nome, o teu IP.",
                "",
                "Podes desligar quando quiseres em Hardware e ID.",
            } : Fr ? new[] {
                "Julia's Launcher peut-il envoyer le matériel de ton PC à edupode ?",
                "",
                "POURQUOI ÇA AIDE",
                "   On voit sur quelles cartes graphiques, quels processeurs et quelles quantités de mémoire les gens jouent vraiment.",
                "   Ça décide quels préréglages passent en premier, quels correctifs viennent ensuite (par exemple Ultra bas pour",
                "   les PC de 16 Go, le support AMD) et sur quelles cartes la génération d'images est testée.",
                "",
                "LA LISTE",
                "   Chaque PC qui partage est une ligne, sans nom. Seul edupode voit la liste.",
                "   Une page publique ne montre que les moyennes (combien de PC, RAM et VRAM moyennes, les cartes les plus",
                "   courantes), mise à jour toutes les 6 heures : docs/HARDWARE-STATS.md sur GitHub.",
                "",
                "CE QUI EST ENVOYÉ",
                "   Carte graphique et pilote, VRAM, Resizable BAR, processeur, RAM, modèle de carte mère,",
                "   version de Windows, type de disque, portable ou non, et ton ID VI. Au plus une fois par jour.",
                "",
                "JAMAIS ENVOYÉ",
                "   Logs, réglages, chemins de fichiers, ton nom, ton adresse IP.",
                "",
                "Tu peux le désactiver à tout moment dans Matériel et ID.",
            } : new[] {
                "Can Julia's Launcher send your PC's hardware to edupode?",
                "",
                "WHY IT HELPS",
                "   We see which graphics cards, processors and amounts of memory people really play on. That decides",
                "   which presets come first, which fixes come next (for example Ultra Low for 16 GB PCs, AMD support)",
                "   and which cards frame generation is tested on.",
                "",
                "THE LIST",
                "   Each PC that shares is one line, with no name. Only edupode sees the list.",
                "   A public page shows only the averages (how many PCs, average RAM and video memory, the most",
                "   common cards), updated every 6 hours: docs/HARDWARE-STATS.md on GitHub.",
                "",
                "WHAT IS SENT",
                "   Graphics card and driver, video memory, Resizable BAR, processor, RAM, motherboard model,",
                "   Windows version, disk type, laptop or not, and your VI ID. At most once a day.",
                "",
                "NEVER SENT",
                "   Logs, settings, file paths, your name, your IP address.",
                "",
                "You can turn it off any time on Hardware & ID.",
            });
            // The text takes the height it needs (Portuguese and French wrap more); the buttons follow it.
            int yb = FitText(text, 50) + 16;
            f.ClientSize = new Size(660, yb + 62);
            f.Controls.Add(text);
            var yes = new Pill { Main = true, Text = L("Yes, share my hardware", "Sim, partilhar o meu hardware"), Location = new Point(20, yb), Size = new Size(250, 42) };
            var later = new Pill { Text = L("Not now", "Agora nao"), Location = new Point(280, yb + 4), Size = new Size(140, 34) };
            var never = new Pill { Text = L("Don't ask again", "Nao perguntar mais"), Location = new Point(430, yb + 4), Size = new Size(190, 34) };
            yes.Click += (s, e) => {
                // The switch on Hardware & ID does the rest (its handler writes the setting and sends).
                if (shareBox != null && !shareBox.Checked) shareBox.Checked = true;
                else { WriteSetting("share", "1"); WriteSetting("share_sent", ""); StartHardwareShare(); }
                Log(L("Hardware sharing is on. Thank you!", "Partilha de hardware ligada. Obrigado!"));
                f.Close();
            };
            later.Click += (s, e) => f.Close();
            never.Click += (s, e) => { WriteSetting("share_ask", "never"); f.Close(); };
            f.Controls.Add(yes); f.Controls.Add(later); f.Controls.Add(never);
            f.CancelButton = later;
            f.ShowDialog(this);
            f.Dispose();
        }

        // ---------------------------------------------------------------- notices, once each
        // New things players should try and tell us about on Discord: VIctory's own frame
        // generation (cards without Smooth Motion), and the edupode engine on AMD cards.
        void ShowNotices()
        {
            if (OwnFrameGen && ReadSetting("notice_fg") != "1")
            {
                WriteSetting("notice_fg", "1");
                var body = string.Join("\r\n", Pt ? new[] {
                    "A tua placa (" + gpuName + ") nao tem o NVIDIA Smooth Motion. Agora o Julia's Launcher tem a sua",
                    "propria geracao de frames: cria um frame entre cada dois do jogo, so a partir das imagens.",
                    "",
                    "O QUE MUDA",
                    "   O movimento fica mais fluido. Os FPS reais do jogo nao sobem, e ha meio frame a mais de atraso.",
                    "",
                    "AINDA NINGUEM A TESTOU NO JOGO: es dos primeiros.",
                    "   1.  Liga 'Geracao de frames' nas Opcoes de arranque (ou carrega em 'Ligar agora').",
                    "   2.  Joga uns minutos com o F3 ligado.",
                    "   3.  Conta no Discord, no canal \"" + DiscordChannel + "\": a tua placa, se ficou mais fluido,",
                    "        se ha tremeliques ou rastos, e se o jogo fechou. O 'Enviar erros no Discord' tem a opcao",
                    "        'Teste da geracao de frames' e junta os logs.",
                    "",
                    "Se correr mal, desliga a opcao e fica tudo como antes.",
                } : Fr ? new[] {
                    "Ta carte (" + gpuName + ") n'a pas NVIDIA Smooth Motion. Julia's Launcher a maintenant sa propre",
                    "génération d'images : il crée une image entre chaque paire d'images du jeu, à partir des images seules.",
                    "",
                    "CE QUI CHANGE",
                    "   Le mouvement paraît plus fluide. Les vrais FPS du jeu n'augmentent pas, et il y a une demi-image de retard en plus.",
                    "",
                    "PERSONNE NE L'A ENCORE TESTÉE EN JEU : tu es parmi les premiers.",
                    "   1.  Active 'Génération d'images' dans Options de lancement (ou appuie sur 'Activer maintenant').",
                    "   2.  Joue quelques minutes avec F3 activé.",
                    "   3.  Raconte-nous sur Discord, dans le salon \"" + DiscordChannel + "\" : ta carte, si c'est plus fluide,",
                    "        s'il y a du scintillement ou des traînées, et si le jeu s'est fermé. 'Envoyer les erreurs sur Discord'",
                    "        a une case 'Test de la génération d'images' et ajoute les logs.",
                    "",
                    "Si ça se passe mal, désactive l'option et tout redevient comme avant.",
                } : new[] {
                    "Your card (" + gpuName + ") has no NVIDIA Smooth Motion. Julia's Launcher now has its own",
                    "frame generation: it makes a frame between each two of the game's, from the pictures alone.",
                    "",
                    "WHAT CHANGES",
                    "   Motion looks smoother. The game's real FPS do not go up, and there is half a frame more delay.",
                    "",
                    "NOBODY HAS TESTED IT IN THE GAME YET: you are among the first.",
                    "   1.  Turn on 'Frame generation' in Launch options (or press 'Turn it on now').",
                    "   2.  Play a few minutes with F3 on.",
                    "   3.  Tell us on Discord, in the \"" + DiscordChannel + "\" channel: your card, whether it looks",
                    "        smoother, any flicker or ghosting, and whether the game closed. 'Send errors to Discord'",
                    "        has a 'Frame generation test' tick and adds the logs.",
                    "",
                    "If it goes wrong, turn the option off and everything is as before.",
                });
                var answer = Notice(L("New: frame generation for your card (experimental)", "Novo: geracao de frames para a tua placa (experimental)"), body,
                    L("Turn it on now", "Ligar agora"), L("Open Discord", "Abrir o Discord"));
                if (answer == 1) { smoothBox.Checked = true; SaveSettings(); Log(L("Frame generation is on (experimental). Tell us how it runs on Discord!", "Geracao de frames ligada (experimental). Conta-nos como corre no Discord!")); }
                if (answer == 2) OpenUrl(DiscordInvite);
            }
            if (hasRadeon && !hasNvidia && ReadSetting("notice_amd") != "1")
            {
                WriteSetting("notice_amd", "1");
                var body = string.Join("\r\n", Pt ? new[] {
                    "Placa AMD detetada (" + gpuName + "). Jogas com o motor edupode, o motor deste projeto e o predefinido",
                    "para todas as placas:",
                    "   •  VRAM de ~12 GB para 6-8 GB na placa do autor, e ~4 vezes mais texturas na mesma memoria;",
                    "   •  sessoes longas e missoes sem crashes.",
                    "",
                    "Foi afinado numa placa NVIDIA: conta-nos no Discord (\"" + DiscordChannel + "\") os FPS e a VRAM na tua Radeon.",
                    "",
                    "Alternativa para Radeon: o VIctory-Belobug-AMD, o fork do Belobug afinado para AMD (o credito e dele).",
                    "Podes trocar quando quiseres em Hardware e ID > Motor.",
                } : Fr ? new[] {
                    "Carte AMD détectée (" + gpuName + "). Tu joues avec le moteur edupode, le moteur de ce projet et celui par défaut",
                    "pour toutes les cartes :",
                    "   •  VRAM de ~12 Go à 6-8 Go sur la carte de l'auteur, et ~4 fois plus de textures dans la même mémoire ;",
                    "   •  longues sessions et missions sans plantage.",
                    "",
                    "Il a été réglé sur une carte NVIDIA : dis-nous sur Discord (\"" + DiscordChannel + "\") les FPS et la VRAM sur ta Radeon.",
                    "",
                    "Alternative pour Radeon : VIctory-Belobug-AMD, le fork de Belobug réglé pour AMD (le mérite lui revient).",
                    "Tu peux changer à tout moment dans Matériel et ID > Moteur.",
                } : new[] {
                    "AMD card found (" + gpuName + "). You play with the edupode engine, this project's engine and the default",
                    "for every card:",
                    "   •  video memory from ~12 GB down to 6-8 GB on the author's card, ~4 times more textures in the same memory;",
                    "   •  long sessions and missions without crashes.",
                    "",
                    "It was tuned on an NVIDIA card: tell us on Discord (\"" + DiscordChannel + "\") the FPS and video memory on your Radeon.",
                    "",
                    "Alternative for Radeon: VIctory-Belobug-AMD, Belobug's fork tuned for AMD (the credit is his).",
                    "You can switch any time in Hardware & ID > Engine.",
                });
                var answer = Notice(L("AMD card: you play with the edupode engine", "Placa AMD: jogas com o motor edupode", "Carte AMD : tu joues avec le moteur edupode"), body,
                    L("Keep edupode", "Ficar com o edupode", "Garder edupode"), L("Try Belobug's instead", "Experimentar o do Belobug", "Essayer celui de Belobug"));
                if (answer == 2)
                {
                    WriteSetting("engine", "amd");
                    if (engineBox != null) engineBox.SelectedIndex = 1;
                    UpdateBanner(); Refresh2();
                    if (VictoryExe == null) { Show(PageSetup); Build(); }
                }
            }
        }

        // A notice in the launcher's style: 1 for the first button, 2 for the second, 0 for "Later".
        int Notice(string title, string body, string first, string second)
        {
            int answer = 0;
            var f = new Form { Text = title, ClientSize = new Size(700, 470), BackColor = Theme.Back, ForeColor = Theme.Text, Font = Theme.Ui(9.75f),
                StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, Icon = Icon };
            f.Controls.Add(new Heading(title) { Location = new Point(20, 16), Width = 660 });
            var bodyLabel = new Label { Font = f.Font, Location = new Point(20, 50), Size = new Size(660, 340), Text = body, ForeColor = Theme.Text, BackColor = Theme.Back };
            int yb = FitText(bodyLabel, 50) + 16;
            f.ClientSize = new Size(700, yb + 62);
            f.Controls.Add(bodyLabel);
            var a = new Pill { Main = true, Text = first, Location = new Point(20, yb), Size = new Size(250, 42) };
            var b = new Pill { Text = second, Location = new Point(280, yb + 4), Size = new Size(220, 34) };
            var c = new Pill { Text = L("Later", "Mais tarde"), Location = new Point(510, yb + 4), Size = new Size(170, 34) };
            a.Click += (s, e) => { answer = 1; f.Close(); };
            b.Click += (s, e) => { answer = 2; f.Close(); };
            c.Click += (s, e) => f.Close();
            f.Controls.Add(a); f.Controls.Add(b); f.Controls.Add(c);
            f.CancelButton = c;
            f.ShowDialog(this);
            f.Dispose();
            return answer;
        }

        // ---------------------------------------------------------------- Hardware & ID page
        Panel HardwarePage()
        {
            var page = Page();
            const int X = 40, W = 900;
            int y = PageTop(page, L("Hardware & ID", "Hardware e ID", "Matériel et ID"), L("Hardware", "Hardware", "Matériel"), L("and your ID.", "e o teu ID.", "et ton ID."), L("Your ID, what this PC is made of, and which engine runs the game.", "O teu ID, de que e feito este PC, e que motor corre o jogo.", "Ton ID, de quoi ce PC est fait, et quel moteur lance le jeu."));
            idLabel = new Label { Location = new Point(X, y), Size = new Size(300, 40), Font = Theme.Mono(18f), ForeColor = Theme.Cyan, BackColor = Theme.Back, Text = "VI-····-····" };
            page.Controls.Add(idLabel);
            var copy = new Pill { Text = L("Copy ID", "Copiar ID"), Location = new Point(X + 310, y + 3), Size = new Size(120, 34) };
            copy.Click += (s, e) => { if (hw != null) { Clipboard.SetText(hw.Id); Log(L("ID copied: ", "ID copiado: ") + hw.Id); } };
            page.Controls.Add(copy);
            y += 44;
            page.Controls.Add(new Label { Location = new Point(X, y), Size = new Size(W, 38), ForeColor = Theme.Dim, BackColor = Theme.Back, Font = Theme.Ui(9f),
                Text = L("Made from parts of your PC (the motherboard and the processor): it stays the same after reinstalling Windows. Put it in your bug reports.",
                         "Feito a partir de pecas do teu PC (placa-mae e processador): fica igual depois de reinstalar o Windows. Poe-no nos teus relatorios de erros.") });
            y += 44;

            page.Controls.Add(new Heading(L("Hardware", "Hardware")) { Location = new Point(X, y), Width = W });
            y += 32;
            hwText = new InfoCard { Location = new Point(X, y), Size = new Size(W, 56), Empty = L("Reading your hardware...", "A ler o teu hardware...", "Lecture de ton matériel...") };
            page.Controls.Add(hwText);
            y += hwText.Height + 12;

            page.Controls.Add(new Heading(L("Engine", "Motor")) { Location = new Point(X, y), Width = W });
            y += 32;
            engineBox = new ComboBox { Location = new Point(X, y), Width = 420, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Theme.Card, ForeColor = Theme.Text };
            engineBox.Items.AddRange(new object[] {
                L("★ edupode (recommended, every card: least video memory, most stable)", "★ edupode (recomendado, todas as placas: menos VRAM, mais estavel)"),
                L("AMD alternative · VIctory-Belobug-AMD by Belobug", "Alternativa AMD · VIctory-Belobug-AMD do Belobug"),
            });
            engineBox.SelectedIndex = EngineChoice == "amd" ? 1 : 0;
            engineBox.SelectedIndexChanged += (s, e) => {
                WriteSetting("engine", engineBox.SelectedIndex == 1 ? "amd" : "edupode");
                Log(L("Engine: ", "Motor: ") + EngineName);
                UpdateBanner(); Refresh2();
            };
            // The choice shows as two pills (as on Play); the list stays, hidden, for the code that sets it.
            engineBox.Visible = false;
            page.Controls.Add(engineBox);
            var hwStd = new Pill { Text = L("edupode · recommended", "edupode · recomendado", "edupode · recommandé"), Location = new Point(X, y), Size = new Size(230, 36) };
            var hwAmd = new Pill { Text = L("Belobug AMD · for Radeon", "Belobug AMD · para Radeon", "Belobug AMD · pour Radeon"), Location = new Point(X + 240, y), Size = new Size(230, 36) };
            hwStd.Click += (s, e) => { if (UseAmdEngine) engineBox.SelectedIndex = 0; };
            hwAmd.Click += (s, e) => { if (!UseAmdEngine) engineBox.SelectedIndex = 1; };
            showEnginePills += () => { hwStd.Main = !UseAmdEngine; hwAmd.Main = UseAmdEngine; hwStd.Invalidate(); hwAmd.Invalidate(); };
            hwStd.Main = !UseAmdEngine; hwAmd.Main = UseAmdEngine;
            page.Controls.Add(hwStd); page.Controls.Add(hwAmd);
            y += 46;
            page.Controls.Add(new Label { Location = new Point(X, y), Size = new Size(W, 52), ForeColor = Theme.Dim, BackColor = Theme.Back, Font = Theme.Ui(9f),
                Text = L("Every card plays with the edupode engine: video memory from ~12 GB down to 6-8 GB, ~4x more textures in it, the most stable. For Radeon cards there is also VIctory-Belobug-AMD, Belobug's fork tuned for Radeon (github.com/Belobug/VIctory-Belobug-AMD, GPL-3.0; all credit for it is Belobug's), which Setup builds once.",
                         "Todas as placas jogam com o motor edupode: VRAM de ~12 GB para 6-8 GB, ~4x mais texturas, o mais estavel. Para placas Radeon ha tambem o VIctory-Belobug-AMD, o fork do Belobug afinado para Radeon (github.com/Belobug/VIctory-Belobug-AMD, GPL-3.0; todo o credito e do Belobug), que o Preparar compila uma vez.") });
            y += 60;

            y = SavesSection(page, X, y, W);
            y = ReferencePcSection(page, X, y, W);
            y = PlayersPcsSection(page, X, y, W);
            page.Controls.Add(new Heading(L("Hardware sharing (only hardware)", "Partilha de hardware (so hardware)")) { Location = new Point(X, y), Width = W });
            y += 32;
            shareBox = new OptionCard { Text = L("Share my hardware with edupode", "Partilhar o meu hardware com o edupode"), Line = L("Off by default. Only the list below; never logs, options, paths, names or your IP.", "Desligado por defeito. So a lista em baixo; nunca logs, opcoes, caminhos, nomes nem o teu IP."),
                Location = new Point(X, y), Size = new Size(W, 58), Checked = ReadSetting("share") == "1" };
            shareBox.CheckedChanged += (s, e) => {
                WriteSetting("share", shareBox.Checked ? "1" : "0");
                if (shareBox.Checked) { WriteSetting("share_sent", ""); StartHardwareShare(); }
                RefreshHardware();
            };
            page.Controls.Add(shareBox);
            y += 66;
            y = SessionLogsSection(page, X, y, W);
            shareStatus = new Label { Location = new Point(X, y), Size = new Size(W, 40), ForeColor = Theme.Dim, BackColor = Theme.Back, Font = Theme.Ui(9f) };
            page.Controls.Add(shareStatus);
            y += 44;
            page.Controls.Add(Small(L("Exactly what is sent:", "Exatamente o que e enviado:"), X, y, W));
            y += 22;
            // The text sits in a rounded card of the theme instead of a stock framed box.
            var jsonCard = new CardPanel { Location = new Point(X, y), Size = new Size(W, 300) };
            hwJson = new TextBox { Location = new Point(14, 12), Size = new Size(W - 24, 276), Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Theme.Card, ForeColor = Theme.Soft, Font = Theme.Mono(8.5f), ScrollBars = ScrollBars.Vertical, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            jsonCard.Controls.Add(hwJson);
            page.Controls.Add(jsonCard);
            page.Controls.Add(new Label { Location = new Point(X, y + 310), Size = new Size(10, 10), BackColor = Theme.Back });
            return page;
        }

        void RefreshHardware()
        {
            new Thread(() => {
                HardwareInfo h = null;
                try { h = ReadHardware(); } catch (Exception) { }
                try
                {
                    BeginInvoke(new Action(() => {
                        if (h == null)
                        {
                            // Nothing could be read: say so instead of leaving the card blank.
                            hwText.Empty = L("The hardware could not be read.", "Nao foi possivel ler o hardware.", "Le matériel n'a pas pu être lu.");
                            hwText.Invalidate();
                            return;
                        }
                        hw = h;
                        idLabel.Text = h.Id;
                        hwText.Text = string.Join("\r\n", new[] {
                            L("Graphics card ", "Placa grafica ") + ": " + h.Gpu + (h.Driver.Length > 0 ? "  (driver " + h.Driver + ")" : "") + "  ·  " + GpuKind(),
                            "Resizable BAR : " + RebarText() + (probeAdapter.Length > 0 ? "  ·  " + L("VIctory runs on ", "o VIctory corre na ") + probeAdapter : ""),
                            L("Processor     ", "Processador   ") + ": " + h.Cpu + "  ·  " + h.Cores + L(" cores, ", " nucleos, ") + h.Threads + " threads",
                            "RAM           : " + h.RamGb + " GB",
                            L("Motherboard   ", "Placa-mae     ") + ": " + h.BoardMaker + " " + h.Board,
                            "Windows       : " + h.Os,
                            L("System disk   ", "Disco         ") + ": " + h.Disk + (h.Laptop ? L("  ·  laptop", "  ·  portatil") : ""),
                            L("Engine        ", "Motor         ") + ": " + EngineName,
                        });
                        GrowTo(hwText, hwText.Fit());
                        hwText.Invalidate();
                        hwJson.Text = HardwareJson(h).Replace("\n", "\r\n");
                        shareStatus.Text = HardwareEndpoint.Length == 0
                            ? L("The database is not online yet: nothing is sent, even with sharing on.", "A base de dados ainda nao esta online: nada e enviado, mesmo com a partilha ligada.")
                            : !shareBox.Checked ? L("Off: nothing is sent.", "Desligado: nada e enviado.")
                            : L("On: sent at most once a day, to a database only edupode can read. Turn it off any time.", "Ligado: enviado no maximo uma vez por dia, para uma base de dados que so o edupode le. Desliga quando quiseres.");
                    }));
                }
                catch (Exception) { }
            }) { IsBackground = true }.Start();
        }
    }
}
