Julia's Launcher 0.3.19: safer updates, honest Setup, and the settings the game really got.

- Updates install as one piece: every file is copied beside the old one first, then swapped. If anything fails (a full disk, a file in use), every file gets its previous version back and the update is tried again later; it never leaves a mix of two versions. It also checks there is enough free space first.
- Setup: a check that could not run (no access, WMI, a file that could not be read) now shows "Could not check" instead of "Not needed", and Setup no longer says "Everything is ready" on top of it.
- Each game's log starts with the settings it really started with, and marks the ones changed in the F1 menu. The activity says when F1 changes one of the launcher's choices (F1 wins; RESET ALL in F1 gives them back).
- 0.3.18's fix for 8 GB cards stays.
