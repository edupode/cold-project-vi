# Plano de implementação e melhorias

**Objetivo:** mais FPS e mais estabilidade, tanto em PCs fortes ("Beefy PC mode") como em PCs mais antigos, mantendo a
imagem dos presets fortes e um carregamento normal. Cada mudança entra **desligada**, é medida antes/depois e só fica
ligada por defeito quando os relatórios dos jogadores o confirmam.

Atualizado a 28/11/2026. Fontes: os relatórios da base de dados (0.3.25: 432 sessões, 79 jogadores, lidos com
`scripts/report-stats.py`), os logs de jogo deste PC (RTX 5070 Ti Laptop), os 82 forks públicos do
[PortWatch](https://portwatch-vi.xyz), os PRs abertos do [Marvisak/VIctory](https://github.com/Marvisak/VIctory) e o
RFC #3703 ("VRAM-first lazy coherency").

## Onde vai o tempo de um frame hoje

Mundo aberto, preset Beefy PC mode, RTX 5070 Ti Laptop (`session-20261128-003645.log`), com a cache de shaders quente:

| Parte do frame | ms por frame |
|---|---|
| **Total** | **104–120** (8–10 fps) |
| Preparar no processador | **68–77** |
| &nbsp;&nbsp;dos quais: cópias da memória do jogo para a placa (≈52 MB) | **30–35** |
| &nbsp;&nbsp;dos quais: texturas | ~1 |
| Copiar de volta da placa (≈6,9 MB) | 14–18 |
| À espera da placa | **0,0** |

**A placa está parada; o limite é o processador a sincronizar a memória** (o modelo de memória unificada da consola
num PC com RAM e VRAM separadas). É aí que estão quase todos os ganhos possíveis. Destilar texturas na placa ou
"tirar instrumentação dos shaders" não mexem neste número.

## Regras

- **Nada do GDKX** (o SDK da Xbox que vazou) entra no projeto, nem como consulta: um projeto GPL público com material
  sob acordo de confidencialidade fica exposto a remoção, como aconteceu ao Belobug e ao AvatarManz.
- Código de outros forks e PRs entra com o autor, o repositório e o commit/PR de origem no crédito.
- Interruptores novos entram desligados; cada experiência é medida em pares A-B-A-B no mesmo sítio, com cache quente,
  e depois nos relatórios (`scripts/report-stats.py`, 50+ sessões de cada lado).
- Antes de cada release: `cargo test`, o launcher compila, alguns minutos de jogo e a comparação com a anterior.
- Os números de outros forks são deles: servem para escolher a ordem, não para prometer FPS.

## Estado em 28/11

| O quê | Estado |
|---|---|
| 0.3.26 (DLAA, aviso de jogo incompleto, seletor de línguas, logs lidos pela equipa, API da equipa, CONTRIBUTING) e 0.3.27 (definições do jogo do VIctory-Hydra nos presets fracos e como opção, Beefy PC mode, 7 línguas, `STAGING_KEPT`/`MAX_PRELUDES`, plano, `report-stats.py`) | no GitHub (ramo `edupode`, tags `v0.3.26`/`v0.3.27`), **releases bloqueadas** |
| Remote Control da sessão de trabalho | ligado |

## Bloqueios

1. **Releases:** desde as 12:47 de 28/11 o passo que cria a release no repositório do felicienradio recebe
   `HTTP 403: Resource not accessible by integration` (0.3.26 três vezes, 0.3.27 uma). O felicienradio tem de ver
   Settings › Actions › General › Workflow permissions ("Read and write") e se o GitHub mostra algum aviso; depois:
   `gh run rerun 46165913666 -R felicienradio/cold-project-vi --failed`.
2. **Windows Defender** bloqueia o launcher compilado localmente (falso positivo `!ml`). Solução a sério: assinar os
   executáveis.
3. **API da equipa:** `wrangler deploy -c wrangler.team.toml` depois da primeira release publicada que muda o texto
   de privacidade, e `TEAM_FROM` = a hora dessa release.

## Fase A · Urgente (estabilidade, visto nos relatórios)

| Problema | Dados | Primeiro passo |
|---|---|---|
| **Ultra baixo a 0,8 fps** medianos no mundo | 85 jogos, 34 jogadores (Beefy 13,7, PC modesto 8,6) | comparar os mesmos jogadores noutros presets; suspeitos: níveis a pedido com texturas de 512 a reentrarem, relógio de 30, `CARD_BUFFERS_MB=512` |
| **"O motor desistiu" (código 1)** sem razão | 54 jogos, 18 jogadores (11 numa RX 7900 XT) | o motor escreve a razão antes de sair |
| `fail-fast 0xC0000409` | 14 jogos (portáteis RTX 4050/3070) | ver a pilha nos relatórios |
| `device removed` | 39 jogos, 19 jogadores (RX 6750/6800 XT, portáteis RTX) | cruzar driver, preset e minuto; o DRED leve já existe |
| Stop em `+0x2224517` (componente do jogo) | 19 jogos, 2 jogadores (RTX 3060 Ti) | provável jogo incompleto: o aviso da 0.3.26 deve mostrá-lo |
| Pipelines pendentes que bloqueiam frames | Marvisak #3607 limita a 500 ms por pipeline | trazer (pequeno: +22 linhas) |

## Fase B · CPU: o frame do processador (ganha em todos os PCs)

Ordenado pelo ganho medido por quem o fez. Os PRs do Marvisak não se aplicam tal como estão (o nosso `watch.rs` e
`shadow.rs` já se afastaram: não têm `take_split` nem `copy_back_marked`), por isso cada um é reescrito; risco
comum: perder escritas do processador (geometria corrompida, "bonecos esticados").

| # | Mudança | Origem | Medido por eles | Ataca |
|---|---|---|---|---|
| B1 | **Em código, a medir** · `BURY_ASIDE`: objetos que esperaram por um voo libertados numa thread à parte | Peppacodes/playablegames-peppa | **−23 ms** médio | preparar |
| B2 | `FILL_TITLE=2`: pequenos preenchimentos de constantes sem esperar pela cópia de volta (página sem escrita de shader há 600 frames) | Peppa | **−11 ms** | cópia de volta |
| B3 | Um só lock das proteções por lote de refresh (3229 → 31 passagens por frame) | Marvisak #3585 (neonbyte1) | **−8,9 ms** a preparar, −10,5 ms no frame | cópias (30–35 ms) |
| B4 | `VICTORY_OWNER`: páginas que os shaders escreveram ficam na placa até alguém lhes tocar (87 % do que se copia de volta é reescrito pela placa antes de ser lido) | Marvisak #3677 (neonbyte1) | **−9 ms** no frame, −10 ms de cópia de volta | cópia de volta (14–18 ms) |
| B5 | As falhas de escrita leem os bytes que guardam sem o lock das proteções | Marvisak #3754 (revive #3035) | 196,1 → 188,6 ms (**−7,5 ms**) | preparar |
| B6 | `GetWriteWatch` em lote para páginas quentes em vez de falhas `PAGE_READONLY` | Marvisak #3615 (Texbirds) | 22× num teste sintético (3,79 → 0,17 ms por frame) | escritas do jogo |
| B7 | `LAND_ASIDE`: listas repostas numa thread à parte | Peppa | −2 a −4 ms | preparar |
| B8 | `MERGE_SUBMISSIONS`: 41 envios por frame passam a 8 | Peppa | −1,9 ms | envio |
| B9 | `HeapRanges` de 16 baldes | tiadiff/VIctory-NVIDIA-LowStutter | procura 3× mais rápida; 23,6–26 fps, 0 crashes | heap de views |
| B10 | `sync_slots` com um `RwLock` de gerações de views | Marvisak #3679 | por medir | lock do heap |
| B11 | Tabelas só escritas pelo processador em VRAM (`VICTORY_GPU_UPLOAD`) | Marvisak #3642 | por medir | PCIe |
| B12 | Lugar das threads (`TITLE_AFFINITY=ignore`, `CCD_SPLIT` para Ryzen com 2 CCDs) | Marvisak #3635 | 15,7 → 18 fps (Ryzen 9 5900X + RX 6800) | AMD com 2 CCDs |
| B13 | Texturas numa thread própria, verificações nas auxiliares, mais espaço para cópias em voo | linha fxceless (guia low-end) | +40 % no mundo; −9 ms; −11,5 ms | preparar |
| B14 | `PORT_STATIC`: listas de comandos estáticas em cache | 0v3rr/Tequesta | 33 000 acertos por frame | envio |

**O que é realista:** B1–B6 juntos atacam ≈60 ms dos ≈110 ms de um frame no mundo; se metade do ganho anunciado se
confirmar aqui, o PC de referência passa de **~9 para ~14–15 fps** no mundo aberto (e mais em cinemáticas). Os 45–60 fps
do post do Reddit/RFC #3703 são uma previsão, não uma medição.

**Ordem:** B1 → B3 → B4 → B2 → B5 → B6, um de cada vez, cada um com o seu interruptor e a sua medição; depois B7–B14.

## Fase C · Memória (PCs de 16 GB e placas de 4–8 GB)

- [ ] **Pico de memória privada por jogo** no resumo e no `report-stats.py`: saber quem chega aos 20 GB e onde. (No
      log deste PC: 7,7–8,4 GB privados no início; os 20 GB vêm sobretudo da memória do jogo emulada no processo e das
      cópias dela, não das texturas, que já vêm de heaps de 64 MB.)
- [ ] Reserva de render targets limitada a ¼ do orçamento em placas de 9 GB ou menos (Androtim/MiniVI).
- [ ] Staging de 64 MB por bloco sem o penhasco do ficheiro de paginação a 16 GB (elaboratecl/VIctory-AMD-4K: zero
      `0x8007000E`); o `VICTORY_STAGING_KEPT` já existe, falta o valor por preset.
- [ ] Tabelas de páginas de 16 MB reutilizadas entre heaps de views (Marvisak #3662).
- [ ] Ritmo do staging de texturas contra engasgos (monte-alt/vesper).
- [ ] Os 14 "host command list did not close (`0x8007000E`)" da 0.3.25: confirmar que desaparecem.
- [ ] Texturas pretas: views que começam no primeiro nível com memória (`VICTORY_PARTIAL_MIP_CLAMP`, Marvisak #3647).
- [ ] `MipLODBias` +0,5 a +0,75 e anisotropia limitada nos samplers, só nos presets fracos (menos largura de banda
      da VRAM em placas de 4–6 GB; custa nitidez).
- [ ] Prioridade à geometria sobre texturas grandes na fila de cópias (depois de medir B3/B4).

Já feitos e que não vale a pena refazer: texturas em blocos `ID3D12Heap` de 64 MB (`pool.rs`, placed resources),
cópias sem os níveis maiores (`VICTORY_TEXTURE_MAX`), texturas mantidas enquanto as páginas saem (`TEXTURE_AWAY_MS`),
staging devolvido quando a lista é reposta, envios sem esperar pela placa (`VICTORY_LATE_WAIT`, relay), views raw
alinhadas a 16 bytes (o `device removed` do Marvisak #3540).

## Fase D · AMD

- [ ] `device removed` (Fase A) e o `+0xb5a5f8` da RX 6800 XT no motor Belobug.
- [ ] B12 (lugar das threads) primeiro em Ryzen de 2 CCDs.
- [ ] Do DexNom/VIctory (melhor imagem em AMD, 3–4 fps): sombras Radeon e streaming na cidade (`5f4c3df4`), sampling
      de quads e texturas esparsas (`921d8d3b`), máscara wave64 da visibilidade por voxels (`76749ce5`, muda o
      tradutor). O `GetDeviceRemovedReason` dele já cá está.
- [ ] Motor Belobug: opções "renderer ports" e "guardar texturas em disco" (com limite); confirmar com log a
      configuração partilhada que diz dar 102 fps (com relógio de 60, deve contar apresentações).

## Fase E · Relatórios muito mais detalhados

- [ ] FPS por fase (menus, cinemáticas, mundo), p5/p1 e os picos > 100 ms com o que a CPU, a placa e o streaming
      faziam nesse momento.
- [ ] Memória: pico de VRAM contra o orçamento, RAM livre mínima, pico de memória privada, pressão de memória.
- [ ] Shaders: pipelines por fazer, draws sem pipeline (a "cache fria" de um motor novo).
- [ ] Uma causa por fim de jogo (já no `report-stats.py`; passar o motor a escrevê-la ele próprio).
- [ ] Streaming: primeiro erro de leitura, descodificação ou mapeamento de cada lote.
- [ ] Um relatório por release publicado para a equipa (`report-stats.py --out`).

## Fase F · Fidelidade

- [ ] Os **29 pipelines** que o jogo pede por `0x1C8`/`0x218`, hoje descartados em todos os jogos: a partir dos bytes
      que o próprio jogo envia (o motor já os escreve; o `PSO_DUMP` do Linox-IDK/LNX-victory ajuda).
- [ ] DLAA nativo: aumentar o heap de render targets do jogo.
- [ ] Os 4 shaders wave64 ainda descartados em placas de 32 lanes.

## Fora do plano (e porquê)

| Ideia | Porquê não agora |
|---|---|
| Material do GDKX (headers `*_xs.h`, exemplos, docs) | material vazado sob acordo de confidencialidade |
| Patch 0001 do WASDeKeys (loops com uma volta) | muda o tradutor e invalida a cache de todos os jogadores |
| Destiling de texturas na placa | ~1 ms por frame aqui |
| "Tirar instrumentação dos shaders" (+30–50 %) | não encontrei a instrumentação descrita; nada medido |
| Heap único de 2,5 GB com TLSF no lugar dos committed resources | já usamos placed resources em blocos de 64 MB |
| Game OS numa máquina virtual | exigiria reproduzir a consola inteira |
| Merge inteiro do RAZDA4A-XEN | sem histórico comum; ideias concretas entram pela Fase B |

## Como se mede

1. Mesma versão, placa, driver, resolução, preset e percurso; cache de shaders quente (a primeira sessão de um motor
   novo recompila e não conta).
2. Pares A-B-A-B de 5 minutos; mediana e p5/p1 do tempo de frame, as partes do frame (preparar, cópias, cópia de
   volta, placa), VRAM e RAM.
3. Nos relatórios: `scripts/report-stats.py` antes e depois, com pelo menos 50 sessões de cada lado.
