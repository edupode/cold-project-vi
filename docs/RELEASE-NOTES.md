Julia's Launcher 0.3.26: no more stop a second in with DLSS at full scale, a warning for an incomplete game, and a language picker with Spanish.

- With DLSS asked for at the output's own size (DLAA: the Sharp, Classic cgbj and some Custom presets), the game ran out of its render-target memory and closed a second after starting. It was the most common crash of 0.3.25 (52 reports, 12 players). DLAA is played as DLSS quality now, from the launcher and from the game's F1 menu.
- The launcher checks the game's folder against the complete game (its 22 archives and its data folder). The four players whose folder differed closed within seconds in every game; the Play page now says what is missing, and PLAY asks before starting.
- The language buttons are one picker now, with Spanish added. New languages are files anyone can add (CONTRIBUTING.md). On a first start the launcher uses Windows' own language when it has it.
- Game logs are read by edupode and the project's team (the collaborators of the repository), every read recorded. Players who said yes before are asked once more; nothing is sent until they answer. Hardware sharing stays edupode's only.
