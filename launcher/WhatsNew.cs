// Julia's Launcher: "What's new", shown once after each install or update (launcher.ini's seen_version),
// and any time from Help. What changed, what got better, what was removed, in the player's language.
// The release notes on GitHub (docs/RELEASE-NOTES.md) say the same; keep the two together.
using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyTitle("Julia's Launcher")]
[assembly: AssemblyDescription("Launcher for the VIctory emulator. Open source: github.com/felicienradio/cold-project-vi")]
[assembly: AssemblyCompany("edupode and felicienradio (open source)")]
[assembly: AssemblyProduct("Julia's Launcher")]
[assembly: AssemblyCopyright("Open source, github.com/felicienradio/cold-project-vi")]
[assembly: AssemblyInformationalVersion(VIctoryLauncher.Updater.Version)]
[assembly: AssemblyFileVersion(VIctoryLauncher.Updater.Version)]

namespace VIctoryLauncher
{
    partial class LauncherForm
    {
        static readonly string[] NewEn = {
            "IN 0.3.13",
            "  •  Fixes from the NVIDIA fork (VIctory-NVIDIA, thanks to its authors): violet blotches from the ambient light",
            "     probes, wrong colours in some compressed textures (BC7), descriptors given back to the wrong heap, buffer",
            "     views reaching past their buffer (a cause of the card stopping), helper threads at the right priority.",
            "",
            "IN 0.3.12",
            "  •  The sun is no longer a black disc: a colour too bright to hold is kept at the brightest value instead of",
            "     made black. The game translates its shaders again once after this update (Shaders > Prepare, ~5 min).",
            "",
            "IN 0.3.11",
            "  •  Fewer crashes on 16 GB PCs: when Windows runs out of memory for a moment, VIctory lets go of the",
            "     memory it keeps in reserve and tries again, instead of closing (seen on RX 9070 XT, RX 6600, RTX 4070).",
            "  •  Your saves are copied before every game (the newest 15 are kept): Hardware & ID > Game saves >",
            "     'Restore a backup...' brings any of them back.",
            "",
            "IN 0.3.9",
            "  •  Rainbow textures: the fix announced in 0.3.7 was left out of that build by mistake. It is in now.",
            "  •  '720p' no longer closes the game at start. It is now 'World at 720p (lighter)': the screen stays 1080p",
            "     and the world is drawn at 720p at most (the game itself refuses a 720p screen).",
            "  •  'Resizable BAR in the NVIDIA driver' now works: Windows asks for administrator rights once.",
            "",
            "IN 0.3.7",
            "  •  Rainbow textures: a texture that had not been read yet showed another texture's leftover bytes",
            "     (faces, shirts, walls full of coloured noise). Now it waits, empty, for a moment instead.",
            "  •  Windows 10: the game starts. The D3D12 Agility SDK (Microsoft's) now comes with VIctory.",
            "  •  Your fastest drive: Shaders > 'Move to another drive...' moves the shader cache, the game's data and",
            "     saves, the AMD engine and builds from source to any drive.",
            "",
            "IN 0.3.6",
            "NEW",
            "  •  Output 720p: pick 720p or 1080p (or 2K, 4K) next to the presets. At 720p, Recommended draws",
            "     native 720p and the 540p presets are upscaled to 720p instead of 1080p: a sharper picture for less.",
            "  •  Game logs (you choose): when the game closes, a standard log of that game can go to edupode, so",
            "     the next optimizations come from real PCs. Asked once; change it on Hardware & ID.",
            "  •  This window: what changed, every time the launcher is installed or updated.",
            "  •  Controls page by what you are doing: on foot, talking, fighting, driving, flying, menus.",
            "  •  Hardware & ID: the reference PC the presets are measured on, and the players' PCs (a table and charts,",
            "     updated every 6 hours) to compare yours with.",
            "  •  Older copies of the launcher (Downloads, Desktop) can go to the Recycle Bin; your settings stay.",
            "",
            "BETTER",
            "  •  Resizable BAR: no more 'Fix it' on cards that cannot have it (GTX, RTX 20), on Windows 10 (the BIOS",
            "     has it on, Windows does not offer it to games) or before the check has finished.",
            "  •  Shaders: 'Shaders prepared' no longer turns red while the game or the warm-up writes the cache.",
            "  •  Graphics cards: each card listed once (it showed up to four times), virtual displays left out.",
            "  •  Buttons: long labels fit, keyboard focus is shown, and buttons react when pressed.",
            "",
            "REMOVED",
            "  •  The launcher no longer changes antivirus settings, and no longer runs hidden PowerShell commands.",
            "     Windows settings (power, virtual memory) are now set by the launcher itself.",
            "  •  The deadly virus was removed from the game. (Like Herobrine, it was never there.)",
            "",
            "IS IT SAFE?",
            "  Yes. Every file is built from the open source code by GitHub Actions, with a provenance attestation",
            "  and SHA-256 sums on the release page, and every update is signed. Windows warns about programs that",
            "  are new and not code-signed (a certificate costs money): Help > 'Is it safe?' shows how to check.",
        };

        static readonly string[] NewPt = {
            "NA 0.3.13",
            "  •  Correcoes do fork NVIDIA (VIctory-NVIDIA, obrigado aos autores): manchas violeta da luz ambiente, cores",
            "     erradas em algumas texturas comprimidas (BC7), descritores devolvidos ao heap errado, vistas de buffer para",
            "     la do buffer (uma causa de a placa parar), threads auxiliares na prioridade certa.",
            "",
            "NA 0.3.12",
            "  •  O sol deixa de ser um disco preto: uma cor demasiado brilhante fica no valor mais brilhante em vez de",
            "     ficar preta. O jogo traduz os shaders de novo uma vez depois deste update (Shaders > Preparar, ~5 min).",
            "",
            "NA 0.3.11",
            "  •  Menos crashes em PCs de 16 GB: quando o Windows fica sem memoria por um momento, o VIctory liberta a",
            "     memoria que guarda de reserva e tenta de novo, em vez de fechar (visto em RX 9070 XT, RX 6600, RTX 4070).",
            "  •  Os teus saves sao copiados antes de cada jogo (ficam os 15 mais recentes): Hardware e ID > Saves do jogo >",
            "     'Restaurar uma copia...' traz qualquer um de volta.",
            "",
            "NA 0.3.9",
            "  •  Texturas arco-iris: a correcao anunciada na 0.3.7 ficou de fora dessa versao por engano. Agora esta.",
            "  •  O '720p' ja nao fecha o jogo ao arrancar. Passa a ser 'Mundo a 720p (mais leve)': o ecra fica a 1080p",
            "     e o mundo e desenhado a 720p no maximo (o proprio jogo recusa um ecra de 720p).",
            "  •  'Resizable BAR no driver da NVIDIA' funciona: o Windows pede permissoes de administrador uma vez.",
            "",
            "NA 0.3.7",
            "  •  Texturas arco-iris: uma textura ainda nao lida mostrava os restos de outra (caras, camisolas,",
            "     paredes cheias de ruido colorido). Agora fica vazia um instante em vez disso.",
            "  •  Windows 10: o jogo arranca. O D3D12 Agility SDK (da Microsoft) vem agora com o VIctory.",
            "  •  O teu disco mais rapido: Shaders > 'Mudar para outro disco...' muda a cache de shaders, os dados e",
            "     saves do jogo, o motor AMD e as compilacoes para qualquer disco.",
            "",
            "NA 0.3.6",
            "NOVO",
            "  •  Saida 720p: escolhe 720p ou 1080p (ou 2K, 4K) ao lado das predefinicoes. Em 720p, o Recomendado",
            "     desenha 720p nativo e as predefinicoes de 540p sao ampliadas para 720p em vez de 1080p: imagem mais nitida.",
            "  •  Logs do jogo (tu escolhes): quando o jogo fecha, uma log padrao desse jogo pode ir para o edupode,",
            "     para as proximas otimizacoes virem de PCs reais. Perguntado uma vez; muda em Hardware e ID.",
            "  •  Esta janela: o que mudou, sempre que o launcher e instalado ou atualizado.",
            "  •  Pagina de controlos pelo que estas a fazer: a pe, falar, lutar, conduzir, voar, menus.",
            "  •  Hardware e ID: o PC de referencia onde as predefinicoes sao medidas, e os PCs dos jogadores (tabela e",
            "     graficos, atualizados de 6 em 6 horas) para comparares o teu.",
            "  •  Copias antigas do launcher (Transferencias, Ambiente de trabalho) podem ir para a Reciclagem; as definicoes ficam.",
            "",
            "MELHOR",
            "  •  Resizable BAR: acabou o 'Corrigir' em placas que nao o podem ter (GTX, RTX 20), no Windows 10 (a BIOS",
            "     tem-no ligado, o Windows nao o oferece aos jogos) ou antes de a verificacao acabar.",
            "  •  Shaders: 'Shaders preparados' ja nao fica vermelho enquanto o jogo ou o aquecimento escrevem a cache.",
            "  •  Placas graficas: cada placa aparece uma vez (aparecia ate quatro), ecras virtuais ficam de fora.",
            "  •  Botoes: textos longos cabem, o foco do teclado aparece e os botoes reagem ao clique.",
            "",
            "REMOVIDO",
            "  •  O launcher ja nao muda definicoes do antivirus nem corre comandos PowerShell escondidos.",
            "     As definicoes do Windows (energia, memoria virtual) sao feitas pelo proprio launcher.",
            "  •  O virus mortal foi removido do jogo. (Como o Herobrine, nunca la esteve.)",
            "",
            "E SEGURO?",
            "  Sim. Cada ficheiro e compilado a partir do codigo aberto pelo GitHub Actions, com atestado de origem e",
            "  somas SHA-256 na pagina da release, e cada update e assinado. O Windows avisa sobre programas novos e",
            "  sem assinatura de codigo (um certificado custa dinheiro): Ajuda > 'E seguro?' mostra como verificar.",
        };

        static readonly string[] NewFr = {
            "DANS LA 0.3.13",
            "  •  Correctifs du fork NVIDIA (VIctory-NVIDIA, merci à ses auteurs) : taches violettes de la lumière ambiante,",
            "     couleurs fausses dans certaines textures compressées (BC7), descripteurs rendus au mauvais tas, vues de",
            "     tampon débordant de leur tampon (une cause d'arrêt de la carte), threads d'aide à la bonne priorité.",
            "",
            "DANS LA 0.3.12",
            "  •  Le soleil n'est plus un disque noir : une couleur trop lumineuse est gardée à la valeur la plus claire au lieu",
            "     de devenir noire. Le jeu retraduit ses shaders une fois après cette mise à jour (Shaders > Préparer, ~5 min).",
            "",
            "DANS LA 0.3.11",
            "  •  Moins de plantages sur les PC de 16 Go : quand Windows manque de mémoire un instant, VIctory libère la",
            "     mémoire qu'il garde en réserve et réessaie, au lieu de se fermer (vu sur RX 9070 XT, RX 6600, RTX 4070).",
            "  •  Tes sauvegardes sont copiées avant chaque partie (les 15 plus récentes sont gardées) : Matériel et ID >",
            "     Sauvegardes du jeu > 'Restaurer une copie...' en ramène n'importe laquelle.",
            "",
            "DANS LA 0.3.9",
            "  •  Textures arc-en-ciel : le correctif annoncé dans la 0.3.7 avait été oublié dans cette version. Il y est.",
            "  •  Le '720p' ne ferme plus le jeu au démarrage. Il devient 'Monde en 720p (plus léger)' : l'écran reste en",
            "     1080p et le monde est dessiné en 720p au plus (le jeu lui-même refuse un écran 720p).",
            "  •  'Resizable BAR dans le pilote NVIDIA' fonctionne : Windows demande une fois les droits administrateur.",
            "",
            "DANS LA 0.3.7",
            "  •  Textures arc-en-ciel : une texture pas encore lue montrait les restes d'une autre (visages, t-shirts,",
            "     murs pleins de bruit coloré). Elle reste maintenant vide un instant à la place.",
            "  •  Windows 10 : le jeu démarre. Le D3D12 Agility SDK (de Microsoft) est fourni avec VIctory.",
            "  •  Ton disque le plus rapide : Shaders > 'Déplacer vers un autre disque...' déplace le cache de shaders,",
            "     les données et sauvegardes, le moteur AMD et les compilations vers n'importe quel disque.",
            "",
            "DANS LA 0.3.6",
            "NOUVEAU",
            "  •  Sortie 720p : choisis 720p ou 1080p (ou 2K, 4K) à côté des préréglages. En 720p, Recommandé dessine",
            "     en 720p natif et les préréglages 540p sont agrandis en 720p au lieu de 1080p : une image plus nette.",
            "  •  Logs du jeu (au choix) : à la fermeture du jeu, un log standard de la partie peut partir chez edupode,",
            "     pour que les prochaines optimisations viennent de vrais PC. Demandé une fois ; modifiable dans Matériel et ID.",
            "  •  Cette fenêtre : ce qui a changé, à chaque installation ou mise à jour du launcher.",
            "  •  Page Commandes selon ce que tu fais : à pied, parler, combat, conduite, vol, menus.",
            "  •  Matériel et ID : le PC de référence des préréglages, et les PC des joueurs (tableau et graphiques,",
            "     mis à jour toutes les 6 heures) pour comparer le tien.",
            "  •  Les anciennes copies du launcher (Téléchargements, Bureau) peuvent aller à la Corbeille ; tes réglages restent.",
            "",
            "MIEUX",
            "  •  Resizable BAR : plus de 'Corriger' sur les cartes qui ne l'ont pas (GTX, RTX 20), sous Windows 10 (activé",
            "     dans le BIOS, mais Windows ne le propose pas aux jeux) ni avant la fin de la vérification.",
            "  •  Shaders : 'Shaders préparés' ne passe plus au rouge pendant que le jeu ou le préchauffage écrit le cache.",
            "  •  Cartes graphiques : chaque carte une seule fois (jusqu'à quatre avant), écrans virtuels exclus.",
            "  •  Boutons : les textes longs tiennent, le focus clavier se voit, les boutons réagissent au clic.",
            "",
            "RETIRÉ",
            "  •  Le launcher ne modifie plus les réglages de l'antivirus et ne lance plus de commandes PowerShell cachées.",
            "     Les réglages Windows (alimentation, mémoire virtuelle) sont faits par le launcher lui-même.",
            "  •  Le virus mortel a été retiré du jeu. (Comme Herobrine, il n'a jamais existé.)",
            "",
            "EST-CE SÛR ?",
            "  Oui. Chaque fichier est compilé depuis le code open source par GitHub Actions, avec une attestation",
            "  d'origine et des sommes SHA-256 sur la page de la release, et chaque mise à jour est signée. Windows met",
            "  en garde contre les programmes récents sans signature de code : Aide > 'Est-ce sûr ?' montre comment vérifier.",
        };

        public const string SafetyUrl = "https://github.com/felicienradio/cold-project-vi/blob/main/docs/SAFETY.md";

        // Once per version (after an install or an update), when the window is up.
        void ShowWhatsNewOnce()
        {
            if (ReadSetting("seen_version") == Updater.Version) return;
            WriteSetting("seen_version", Updater.Version);
            ShowWhatsNew();
        }

        void ShowWhatsNew()
        {
            var f = new Form { Text = L("What's new", "Novidades", "Nouveautés") + " · v" + Updater.Version, ClientSize = new Size(780, 600), BackColor = Theme.Back, ForeColor = Theme.Text,
                Font = Theme.Ui(9.75f), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, Icon = Icon };
            f.Controls.Add(new Heading(string.Format(L("What's new in v{0}", "Novidades da v{0}", "Nouveautés de la v{0}"), Updater.Version)) { Location = new Point(20, 16), Width = 740 });
            var text = new TextBox { Location = new Point(20, 52), Size = new Size(740, 470), Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Theme.Card, ForeColor = Theme.Text,
                Font = Theme.Ui(9.75f), ScrollBars = ScrollBars.Vertical, TabStop = false };
            text.Text = string.Join("\r\n", Fr ? NewFr : Pt ? NewPt : NewEn);
            f.Controls.Add(text);
            var ok = new Pill { Main = true, Text = L("Got it", "Percebi", "Compris"), Location = new Point(600, 538), Size = new Size(160, 42) };
            ok.Click += (s, e) => f.Close();
            var safe = new Pill { Text = L("Is it safe?", "E seguro?", "Est-ce sûr ?"), Location = new Point(20, 542), Size = new Size(160, 34) };
            safe.Click += (s, e) => OpenUrl(SafetyUrl);
            var notes = new Pill { Text = L("All releases", "Todas as versoes", "Toutes les versions"), Location = new Point(190, 542), Size = new Size(160, 34) };
            notes.Click += (s, e) => OpenUrl("https://github.com/felicienradio/cold-project-vi/releases");
            f.Controls.Add(ok); f.Controls.Add(safe); f.Controls.Add(notes);
            f.AcceptButton = ok;
            f.ShowDialog(this);
        }
    }
}
