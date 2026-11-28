Julia's Launcher 0.3.24: the updated engine really runs, and two more "closes after a second" fixed.

- A launcher that is not run from inside a source folder now always plays the emulator its updates install. Some PCs kept starting an older victory.exe found elsewhere (a downloaded source folder, a copy beside the game), so engine fixes never reached them.
- The game's stops after one of its components did not start are stepped over at three places now (0.3.23 had one): the RTX 4060 and RX 6500 XT one, an RTX 3050 laptop one, and one seen on Belobug's engine. Only a start that fails there reaches them; other PCs run as before.
- Each game's log and every report say which victory.exe started (path, size, date).
