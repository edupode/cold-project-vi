Julia's Launcher 0.3.13: fixes from the NVIDIA fork.

Five engine fixes brought over from [VIctory-NVIDIA](https://github.com/trustno1-coder/VIctory-NVIDIA) (GPL-3.0, like this project; thanks to Ahyildiz, fxceless, wike1701, CuteBooty and Indy2l):
- **Violet blotches**: garbage in the ambient light probes no longer shows as purple patches on lit surfaces.
- **Wrong colours in compressed textures**: BC7 colour rotation only where the format has it (modes 4 and 5).
- **Descriptor slots** are given back only to the heap that handed them out.
- **Buffer views stay inside their buffer** (`VICTORY_RAW_VIEW_CLAMP=0` turns it off): a view reaching past its buffer is one way the card stops.
- **Helper threads** run at the submitting thread's priority.

No shader preparation is needed after this one.

Is it safe? [docs/SAFETY.md](https://github.com/felicienradio/cold-project-vi/blob/main/docs/SAFETY.md) · [Code signing policy](https://github.com/felicienradio/cold-project-vi/blob/main/docs/CODE-SIGNING-POLICY.md)
