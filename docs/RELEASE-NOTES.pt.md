Julia's Launcher 0.3.13: correções do fork NVIDIA.

Cinco correções do motor trazidas do [VIctory-NVIDIA](https://github.com/trustno1-coder/VIctory-NVIDIA) (GPL-3.0, como este projeto; obrigado a Ahyildiz, fxceless, wike1701, CuteBooty e Indy2l):
- **Manchas violeta**: lixo nas sondas de luz ambiente deixa de aparecer como manchas roxas em superfícies iluminadas.
- **Cores erradas em texturas comprimidas**: a rotação de cor BC7 só onde o formato a tem (modos 4 e 5).
- **Slots de descritores** só são devolvidos ao heap que os deu.
- **As vistas de buffer ficam dentro do buffer** (`VICTORY_RAW_VIEW_CLAMP=0` desliga): uma vista para lá do buffer é uma das formas de a placa parar.
- **Threads auxiliares** correm na prioridade da thread que submete.

Não é preciso preparar os shaders depois desta.

É seguro? [docs/SAFETY.md](https://github.com/felicienradio/cold-project-vi/blob/main/docs/SAFETY.md) · [Política de assinatura de código](https://github.com/felicienradio/cold-project-vi/blob/main/docs/CODE-SIGNING-POLICY.md)
