Julia's Launcher 0.3.18: fewer crashes on 8 GB cards.

- Cards with 8 GB or less (and PCs with 16 GB of RAM) could crash a few minutes in: new textures kept being made after the card was already past the memory Windows gives it, because empty space inside half-empty texture blocks was counted as room. The overflow went into the PC's memory until that ran out too (errors 0x8007000E, then 0x887A0005). Now no new texture is made while the card is past its budget; they wait until there is room.
- Everything from 0.3.17 is unchanged.
