Julia's Launcher 0.3.12: the sun is no longer black.

- **Black sun fixed.** The sun's disc is so bright that its colour reached infinity, and VIctory's protection against broken colours (which stops the whole picture going white) turned infinities into black. Infinities are now held at the brightest colour a picture can keep (65504) instead, so the sun is white again and nothing infinite reaches the bloom or the exposure. Broken colours (NaN) still become black, as before.
- **Once after this update, the game translates its shaders again** (the translator changed). Shaders > **Prepare shaders** does it in about 5 minutes; otherwise the first game stutters while it happens.

Is it safe? [docs/SAFETY.md](https://github.com/felicienradio/cold-project-vi/blob/main/docs/SAFETY.md) · [Code signing policy](https://github.com/felicienradio/cold-project-vi/blob/main/docs/CODE-SIGNING-POLICY.md)
