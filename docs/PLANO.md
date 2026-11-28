# Plano de implementação e melhorias

**Objetivo:** mais FPS e mais estabilidade, tanto em PCs fortes ("Beefy PC mode") como em PCs mais antigos, mantendo a
imagem que existe hoje nos presets fortes e um carregamento normal. Cada mudança é medida nos relatórios dos jogadores
antes de ficar ligada por defeito.

Atualizado a 28/11/2026. Fontes: os relatórios da base de dados (0.3.25: 432 sessões, 79 jogadores), os forks públicos
do [PortWatch](https://portwatch-vi.xyz) (82 repositórios lidos) e os testes neste PC (RTX 5070 Ti Laptop).

## Regras

- **Nada do GDKX** (o SDK da Xbox que vazou) entra no projeto, nem como consulta: um projeto GPL público com material
  sob acordo de confidencialidade fica exposto a remoção, como aconteceu ao Belobug e ao AvatarManz. As compatibilidades
  saem do próprio jogo (os seus bytes, as suas chamadas) e do GDK público.
- Código de outros forks entra com o autor e o commit de origem no crédito.
- Interruptores novos entram **desligados**; só passam a ligados com uma medição antes/depois nos relatórios.
- Antes de cada release: `cargo test`, o launcher compila, alguns minutos de jogo com a build nova e a comparação dos
  relatórios com a release anterior (`scripts/report-stats.py`).

## Feito hoje

| O quê | Onde | Estado |
|---|---|---|
| DLAA: o jogo ficava sem memória de render targets e parava em `+0x2219650` (52 relatórios, 12 jogadores); passa a DLSS qualidade | 0.3.26 | código no GitHub, release bloqueada (ver abaixo) |
| Seletor de línguas, espanhol, traduções em ficheiros (`launcher/lang/*.json`, `scripts/launcher-lang.py`) | 0.3.26 | idem |
| Aviso de jogo incompleto (22 `.rpf` e a pasta `data`): as 4 pastas diferentes falharam 31 de 31 jogos | 0.3.26 | idem |
| Os logs passam a ser lidos pela equipa; quem disse sim antes é perguntado outra vez | 0.3.26 | idem |
| API só de leitura para os colaboradores (`server/hardware-worker/src/team.js`) | 0.3.26 | por publicar na Cloudflare |
| `CONTRIBUTING.md` | 0.3.26 | idem |
| Definições do jogo do VIctory-Hydra (sombras, volumétricos, LOD, objetos pequenos, LOD de veículos) | ramo `phase1` | por testar no jogo |
| Ultra baixo e PC modesto com essas definições; opção "Detalhe do jogo mais leve" para qualquer preset; controlos nos Ajustes avançados | `phase1` | idem |
| "Recomendado" passa a "Beefy PC mode" | `phase1` | idem |
| `VICTORY_STAGING_KEPT`, `VICTORY_MAX_PRELUDES` (RAM em PCs de 16 GB) | `phase1` | idem |

## Bloqueios

1. **Releases no repositório do felicienradio:** desde as 12:47 de 28/11 o passo que cria a release recebe
   `HTTP 403: Resource not accessible by integration` (3 tentativas). O felicienradio tem de ver Settings › Actions ›
   General › Workflow permissions ("Read and write") e se o GitHub mostra algum aviso no repositório.
2. **Windows Defender** apaga ou bloqueia o launcher compilado aqui (falso positivo `!ml`). A solução a sério é assinar
   os executáveis; até lá, as notas explicam "Permitir no dispositivo".
3. **API da equipa:** publicar com `wrangler deploy -c wrangler.team.toml` depois da release que muda o texto de
   privacidade, e só então definir `TEAM_FROM` com a hora dessa release.

## Releases seguintes

### 0.3.27 · fase 1 e línguas
- [ ] Testar no jogo: Beefy PC mode com e sem "Detalhe do jogo mais leve", e Ultra baixo; FPS do F3 e a imagem.
- [ ] Línguas: alemão, italiano, russo (depois turco e polaco, ou quem as quiser fazer com o script).
- [ ] Publicar.

### Urgente, visto com `scripts/report-stats.py` nos relatórios da 0.3.25
- [ ] **Ultra baixo a 0,8 fps medianos no mundo** (85 jogos, 34 jogadores; Beefy PC mode 13,7, PC modesto 8,6): o
      preset "leve" é o mais lento. Suspeitos: níveis de textura a pedido com texturas de 512 a reentrarem sem parar,
      relógio de 30 FPS, ou PCs que já são os mais fracos. Comparar com os mesmos jogadores noutros presets.
- [ ] **"O motor desistiu" (código 1) em 54 jogos de 18 jogadores** (11 numa RX 7900 XT), sem linha de erro no log:
      o motor tem de escrever a razão antes de sair.
- [ ] `fail-fast 0xC0000409` em 14 jogos (portáteis RTX 4050 e 3070).

### 0.3.28 · relatórios muito mais detalhados
O resumo de cada jogo passa a responder sozinho a "porque é que correu mal":
- [ ] **FPS por fase:** mediana, p5 e p1 dos tempos de frame, em menus, cinemáticas e mundo aberto à parte, e os
      picos maiores que 100 ms com o que a CPU, a placa e o streaming faziam nesse momento.
- [ ] **Memória:** pico de VRAM contra o orçamento, RAM livre mínima, commit, quantas vezes houve pressão de memória.
- [ ] **Shaders:** pipelines por fazer ao longo do jogo, draws descartados por falta de pipeline (é o "cache fria" que
      parecia uma regressão no dia 28).
- [ ] **Fim do jogo:** uma causa por cada fim: normal, paragem do jogo (com o sítio), acesso inválido, `device
      removed` (com a classe do DRED), falta de memória, jogo incompleto, ou "por determinar".
- [ ] **Streaming:** primeiro erro de leitura, descodificação ou mapeamento de cada lote (o item 3 do plano do GDKX,
      feito a partir do log que o motor já tem).
- [ ] `scripts/report-stats.py`: tabela por versão, motor, fabricante de placa, preset e causa de fim; as assinaturas
      de crash mais frequentes; comparação com a versão anterior. Já existe a primeira versão (lê os relatórios pela
      API da equipa ou pelo `wrangler`).

### 0.3.29 · CPU (ganha em todos os PCs, sobretudo nos fortes)
Medidos pelos autores; cada um entra desligado, é medido aqui e nos relatórios, e só depois fica ligado.

| Mudança | Fork | Ganho medido por eles |
|---|---|---|
| `BURY_ASIDE`: objetos libertados numa thread à parte | Peppacodes/playablegames-peppa | **−23 ms por frame** |
| `FILL_TITLE=2`: pequenos preenchimentos sem esperar pela cópia de volta | Peppa | −11 ms |
| `LAND_ASIDE`: listas repostas numa thread à parte | Peppa | −2 a −4 ms |
| `MERGE_SUBMISSIONS`: 41 envios por frame passam a 8 | Peppa | −1,9 ms |
| `HeapRanges` de 16 baldes | tiadiff/VIctory-NVIDIA-LowStutter | procura 3× mais rápida; 23,6–26 fps, 0 crashes |
| Texturas numa thread própria, verificações nas auxiliares, mais espaço para cópias em voo | linha fxceless (guia low-end) | +40 % no mundo; −9 ms; −11,5 ms |
| `PORT_STATIC`: listas de comandos estáticas em cache | 0v3rr/Tequesta | 33 000 acertos por frame |

### 0.3.30 · memória (PCs mais antigos)
- [ ] Reserva de render targets limitada a ¼ do orçamento em placas de 9 GB ou menos (Androtim/MiniVI).
- [ ] Staging limitado a 64 MB por bloco e sem penhasco do ficheiro de paginação com 16 GB de RAM
      (elaboratecl/VIctory-AMD-4K: zero `0x8007000E`).
- [ ] Ritmo do staging de texturas contra engasgos (monte-alt/vesper).
- [ ] Os 14 "host command list did not close (`0x8007000E`)" da 0.3.25: ver se desaparecem com os pontos acima.

### AMD
- [ ] `device removed` em 12 jogadores (RX 6800 XT, 6750 XT, 7600, quase todos no preset forte): cruzar driver,
      preset e tempo até ao erro.
- [ ] Do DexNom/VIctory (GPL-3.0, a melhor imagem em AMD, mas 3-4 fps; 340 commits desde a base comum, 71 já
      cá estão, incluindo o `GetDeviceRemovedReason` que fazia o jogo julgar a placa perdida): recuperar os pipelines
      de sombras em Radeon e o streaming de texturas na cidade (`5f4c3df4`), o sampling de quads e as texturas
      esparsas (`921d8d3b`), e a máscara wave64 do shader de visibilidade por voxels (`76749ce5`; muda o tradutor,
      obriga a recompilar a cache). Cada um medido em AMD antes de entrar.
- [ ] Motor Belobug: opções "renderer ports" e "guardar texturas em disco" (com limite de tamanho).
- [ ] Confirmar com log a configuração partilhada que diz dar 102 fps (com relógio de 60 FPS, provavelmente conta
      apresentações).

### Fidelidade
- [ ] Os **29 pipelines** que o jogo pede por `0x1C8`/`0x218` e que hoje se descartam em todos os jogos: a partir dos
      bytes que o próprio jogo envia (o motor já os escreve no log; o `PSO_DUMP` do Linox-IDK/LNX-victory ajuda).
- [ ] DLAA nativo: precisa de aumentar o heap de render targets do jogo.
- [ ] Os 4 shaders wave64 ainda descartados em placas de 32 lanes.

## Como se mede

1. Mesma versão, placa, driver, resolução, preset e percurso; cache quente (a primeira sessão de um motor novo
   recompila os shaders e não conta).
2. FPS mediano, p5/p1 dos tempos de frame, VRAM e RAM, crashes por hora de jogo.
3. Nos relatórios: a mesma tabela por versão (`scripts/report-stats.py`), com pelo menos 50 sessões de cada lado.
