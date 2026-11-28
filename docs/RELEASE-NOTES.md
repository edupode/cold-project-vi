Julia's Launcher 0.3.23: the game no longer closes a second after it opens on some PCs.

- On some PCs (an RTX 4060 since 0.3.16 with every preset, an RX 6500 XT) the game opened for a second and closed (0x80000003 at +0x1dac734): the game stopped itself when one of its components, under its FidelityFX setup, did not start. VIctory now lets it go on without that component. Only a start that fails there reaches this; every other PC runs exactly as before.
- The 0.3.22 fixes stay: no crash loop on the first start of a version, and your preset stays yours.
