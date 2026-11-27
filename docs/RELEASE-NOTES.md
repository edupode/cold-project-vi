Julia's Launcher 0.3.16: the sun is white again, and you pick your frame generation.

- The sun shows white again instead of a black disc in the sky. Only the colours a pixel writes are held (the 0.3.12 attempt held everything and turned the picture cyan): checked in game, the rest of the picture is unchanged.
- Frame generation engine on the Play page: Automatic (the best your card has), NVIDIA Smooth Motion (RTX 40/50), NVSmooth30 (RTX 30, experimental) or VIctory 2X (any card, experimental). The F3 panel's FRAME GEN line says which one runs, and the panel no longer cuts its last line.
- When Play is greyed out, the button now says why: setup still working, no game chosen, or no engine.
- Setup no longer stays busy (Play greyed out) when one of its steps fails.
- Rust and the C++ Build Tools are asked for only while an engine still has to be compiled (Belobug's AMD engine, before its first build): they were asked again after an update even when installed. Rust is found in CARGO_HOME too, and the C++ tools of a preview Visual Studio count. Playing a release never needs them.
- Shaders are prepared once more: the shader translator changed for the sun.
