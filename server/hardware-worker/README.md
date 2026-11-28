# VI hardware database

Where the launcher's hardware reports go when a player turns **Hardware sharing** on (it is off by default).

- **Only hardware:** graphics card, video memory, driver, Resizable BAR, processor, RAM, motherboard, Windows, disk type, laptop or not, and the `VI-XXXX-XXXX` ID (a hash of PC parts).
- **Never:** logs, options, paths, user names, IP addresses.
- **One way in, nothing out:** the public Worker has a single route, `POST /v1/hw`, and answers with a status code only. There is no route that reads anything.
- **Only you read it:** in the Cloudflare dashboard (D1 > vi-hardware), or with the admin Worker behind Cloudflare Access.
- **Nobody can overwrite someone else's row:** knowing an ID is not enough. The first report for an ID fixes its secret, which only that PC holds.

## How you get the list, and the public averages

- **The list** (one row per PC) stays in the D1 database. You read it in the Cloudflare dashboard (D1 > vi-hardware > Console: `SELECT * FROM hardware`) or on the admin page (step 5), searching by ID.
- **The averages** are published in the repository by the GitHub workflow [`hardware-stats.yml`](../../.github/workflows/hardware-stats.yml), every 6 hours:
  - it reads `GET /v1/stats` with a secret token;
  - it writes `docs/hardware-stats.json` and `docs/HARDWARE-STATS.md`.
- **What the endpoint returns:** only totals and averages, with no ID and no row. A group of fewer than 3 PCs counts as "other", so nobody can be picked out.
- **If the token leaked,** someone would see only the averages, which are public anyway.
- **Asking players:** while sharing is off, the launcher asks before every game. It explains why it helps, what the list is, what is sent and what never is. "Don't ask again" stops it.

## Setting it up (once, about 10 minutes)

1. Make a free Cloudflare account and install Wrangler: `npm i -g wrangler`, then `wrangler login`.
2. Create the database and put its id in both `wrangler.toml` and `wrangler.admin.toml`:
   ```
   wrangler d1 create vi-hardware
   wrangler d1 execute vi-hardware --remote --file schema.sql
   ```
3. Deploy the public Worker:
   ```
   wrangler secret put RATE_SALT
   wrangler secret put STATS_TOKEN
   wrangler deploy
   ```
   When `wrangler secret put` asks for each value, type a different long random text (at least 32 characters). Keep a copy of the `STATS_TOKEN` value for step 6.
4. Put the Worker's address plus `/v1/hw` (for example `https://vi-hardware.<you>.workers.dev/v1/hw`) in `HardwareEndpoint` in `launcher/Hardware.cs`, then publish a release. Until then the launcher sends nothing.
5. Set up the admin Worker:
   1. In Cloudflare Zero Trust, create an Access application for a hostname, for example `vi-admin.<your domain>`, with a policy that lets in only your email and requires 2FA.
   2. Put the team name and the application's AUD tag in `wrangler.admin.toml`.
   3. Deploy it with `wrangler deploy -c wrangler.admin.toml` and add the route for that hostname.
   4. Open the hostname and search by `VI-XXXX-XXXX`.
6. Set up the averages: on GitHub, open the repository's Settings > Secrets and variables > Actions and add two secrets:
   - `VI_STATS_URL`: the Worker's address plus `/v1/stats`;
   - `VI_STATS_TOKEN`: the `STATS_TOKEN` value from step 3.

   Then run **Actions > Hardware averages > Run workflow** once. GitHub runs scheduled workflows only from the repository's default branch, so for the 6-hour schedule keep `hardware-stats.yml` there too.

## Bug reports panel and save gallery

The admin Worker (`src/admin.js`, behind Cloudflare Access) is also the panel for everything players send:

- **Hardware** (`/`): the PCs, searchable by `VI-XXXX-XXXX`; each ID links to that player's reports.
- **Bug reports** (`/reports`): what "Send to edupode" sent, newest first. A report's page shows its
  `summary.txt` and `launcher-activity.txt`; it can be downloaded as its zip or deleted.
- **Save gallery** (`/saves`): saves players shared from the launcher (Hardware & ID > Game saves > Share in the
  gallery). They wait as **pending**; **Approve** makes one public, **Hide** takes it back, **Delete** removes it.

The public Worker serves the gallery itself: `GET /saves` (a page in EN/FR/PT), `GET /v1/saves` (JSON) and
`GET /v1/saves/<n>.zip`, approved saves only. `POST /v1/saves` takes a save from the launcher: at most 1 MB, a
title of 3-80 characters and a note of up to 300, 3 an hour per IP (the same salted-hash limit as reports).

To turn it on (once):
```
wrangler d1 execute vi-hardware --remote --file schema.sql
wrangler deploy
wrangler deploy -c wrangler.admin.toml
```
`schema.sql` only adds the `saves` table; the other tables and their rows stay as they are.

## Public bug reports on GitHub

When a player presses **Send** (not **Send privately**), the launcher adds `X-VI-Public: 1`: the report is kept in
the box as always, and the Worker also opens an issue on `ISSUES_REPO` (wrangler.toml, felicienradio's repository)
with the report's `summary.txt` only (no logs, no motherboard line; `@` and code fences made harmless), labelled
`bug` and `game` or `launcher`. The launcher shows the issue's address. Without a token nothing changes.

The token, once:
1. felicienradio, on GitHub: Settings > Developer settings > Fine-grained tokens > Generate new token. Repository
   access: only `felicienradio/cold-project-vi`; permissions: **Issues: Read and write**, nothing else; an expiry
   date (renew it before then).
2. `wrangler secret put GITHUB_TOKEN` (paste it when asked), then `wrangler deploy`.
   The token can only open and edit issues on that one repository. Pass it privately, never in a channel.

## The team's read-only API (collaborators)

`src/team.js`, a Worker of its own (`wrangler.team.toml`), lets the collaborators of `felicienradio/cold-project-vi`
(write access or more) read the bug reports, without the Cloudflare account. It changes and deletes nothing.

- **Who gets in:** each request carries the caller's own GitHub token (`Authorization: Bearer $(gh auth token)`); the
  Worker asks GitHub who that is and whether they have push access to `TEAM_REPO`. Logins in `TEAM_EXTRA` get in too.
  A token is checked with GitHub once every 10 minutes.
- **What they read:** only reports received from `TEAM_FROM` on (UTC). Until now players were told only edupode reads
  their logs; set `TEAM_FROM` to the publishing time of the launcher release that tells them the project's team does,
  never earlier. Empty, the API serves nothing. The hardware table is not served.
- **Every read is logged** in `team_log` (login, time, path): `SELECT * FROM team_log ORDER BY n DESC`.

Routes: `GET /v1/me`, `GET /v1/reports?id=&kind=&since=&before=&limit=`, `GET /v1/reports/<n>` (row, files, summary),
`GET /v1/reports/<n>/files/<name>`, `GET /v1/reports/<n>.zip`.

Setting it up:
```
wrangler d1 execute vi-hardware --remote --command "CREATE TABLE IF NOT EXISTS team_log (n INTEGER PRIMARY KEY AUTOINCREMENT, at TEXT NOT NULL, login TEXT NOT NULL, path TEXT NOT NULL)"
wrangler deploy -c wrangler.team.toml
```
Tests: `node --test server/hardware-worker/test/team.test.mjs`.

A collaborator, with the GitHub CLI logged in:
```
curl -H "Authorization: Bearer $(gh auth token)" https://vi-team.<account>.workers.dev/v1/reports?kind=game&limit=20
```
