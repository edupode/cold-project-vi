Julia's Launcher 0.3.16: o sol volta a ser branco, e escolhes a tua geração de frames.

- O sol volta a aparecer branco em vez de um disco preto no céu. Só as cores que um pixel escreve são seguradas (a tentativa da 0.3.12 segurava tudo e deixou a imagem verde-azulada): testado no jogo, o resto da imagem não muda.
- Motor de geração de frames na página Jogar: Automático (o melhor da tua placa), NVIDIA Smooth Motion (RTX 40/50), NVSmooth30 (RTX 30, experimental) ou VIctory 2X (qualquer placa, experimental). A linha FRAME GEN do painel F3 diz qual corre, e o painel já não corta a última linha.
- Quando Jogar está cinzento, o botão diz agora porquê: o Preparar ainda a trabalhar, nenhum jogo escolhido, ou sem motor.
- O Preparar já não fica ocupado (Jogar cinzento) quando um dos passos falha.
- O Rust e o C++ Build Tools só são pedidos enquanto um motor ainda tem de ser compilado (o motor AMD do Belobug, antes da primeira compilação): eram pedidos outra vez depois de um update, mesmo instalados. O Rust também é encontrado no CARGO_HOME, e as ferramentas C++ de um Visual Studio em pré-visualização contam. Jogar uma release nunca precisa deles.
- Os shaders são preparados mais uma vez: o tradutor de shaders mudou por causa do sol.
