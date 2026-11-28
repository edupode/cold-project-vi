# Contributing to cold-project-vi

Everyone with write access to `felicienradio/cold-project-vi` can change the code, read the players' bug
reports and publish a release. This page says how.

## Code

- Engine: Rust, in `crates/` and `apps/victory`. `cargo test --locked` before a commit; three shader tests on
  WARP (Microsoft's software card) fail on some PCs before any change: they are not yours.
- Launcher: C#, in `launcher/`. Build it with `launcher\build-launcher.cmd` (the .NET Framework compiler that
  comes with Windows; nothing to install). Every text the player sees goes through `L("English", "Português",
  "Français")`.
- A new switch of the engine is off by default (`VICTORY_*`), and is turned on in a preset only with a
  measured run that shows it helps.
- Commit messages say what changed and why, with the measurement when there is one.

## Translations

The launcher's languages beyond English, Portuguese and French are files: `launcher/lang/<code>.json`
(English text -> translation, `_name` is the language's own name). They are built into the launcher, and the
language picker at the bottom of the side bar lists them.

```
python scripts/launcher-lang.py new de Deutsch     # launcher/lang/de.json with every English text
python scripts/launcher-lang.py missing es         # what es.json does not translate yet
python scripts/launcher-lang.py check              # placeholders ({0}, {1:0.0}) match, no stale keys
```

Keep `{0}`, `{1:0.0}` and the line breaks (`\r\n`) as they are. A text missing from a file shows in English.
To try a translation without a build, put the file in a `lang` folder beside `Julia's Launcher.exe`.

## Bug reports

Players send reports from the launcher ("Send errors", and a log of each game for those who said yes). The
team reads them through a read-only API that checks your GitHub account is a collaborator of this repository;
every read is recorded. With the GitHub CLI logged in:

```
curl -H "Authorization: Bearer $(gh auth token)" "https://vi-team.<account>.workers.dev/v1/reports?kind=game&limit=20"
curl -H "Authorization: Bearer $(gh auth token)" "https://vi-team.<account>.workers.dev/v1/reports/2539"
curl -H "Authorization: Bearer $(gh auth token)" -o r.zip "https://vi-team.<account>.workers.dev/v1/reports/2539.zip"
```

Routes and setup: [server/hardware-worker/README.md](server/hardware-worker/README.md). Reports hold no user
names (they are taken out of every path); do not post their contents outside the team, and quote a player by
their `VI-XXXX-XXXX` ID only.

## Releases

A pushed tag `v0.3.N` builds and publishes a release (`.github/workflows/release.yml`): the engine, the
launcher, the installer, signed by the release signer. **Every launcher installs it within 5 minutes**, so a
release reaches all players at once.

1. Set `Version` in `launcher/Updater.cs` to the new version (the workflow refuses a tag that differs).
2. Write `docs/RELEASE-NOTES.md` and `docs/RELEASE-NOTES.pt.md` (shown in the launcher's update box),
   `docs/ANNOUNCEMENT.md`, `.pt.md` and `.fr.md` (posted on Discord), and the "IN 0.3.N" lines of
   `launcher/WhatsNew.cs` (EN, PT, FR).
3. Build the engine and the launcher, run the tests, and play a few minutes with the new build.
4. Commit, then `git tag v0.3.N` and `git push origin v0.3.N`.
5. Watch the run in Actions. A release that went wrong is fixed by the next version, not by moving the tag.
