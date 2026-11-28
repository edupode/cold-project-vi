-- wrangler d1 execute vi-hardware --remote --file schema.sql
CREATE TABLE IF NOT EXISTS hardware (
  id TEXT PRIMARY KEY,          -- VI-XXXX-XXXX (a hash of PC parts; the parts never leave the PC)
  secret_hash TEXT NOT NULL,    -- SHA-256 of the secret only that PC holds
  report TEXT NOT NULL,         -- the hardware JSON, the launcher's fields only
  first_seen TEXT NOT NULL,
  last_seen TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS reports (
  n INTEGER PRIMARY KEY AUTOINCREMENT,
  created TEXT NOT NULL,
  vi_id TEXT NOT NULL,          -- the launcher's ID, to match a report with a player on Discord
  kind TEXT NOT NULL,           -- game or launcher
  bytes INTEGER NOT NULL,
  zip BLOB NOT NULL             -- the package the player sent (user name taken out of every path)
);
CREATE TABLE IF NOT EXISTS saves (
  n INTEGER PRIMARY KEY AUTOINCREMENT,
  created TEXT NOT NULL,
  vi_id TEXT NOT NULL,          -- who shared it (shown nowhere public; for moderation)
  title TEXT NOT NULL,          -- the player's words, shown in the gallery once approved
  note TEXT NOT NULL,
  bytes INTEGER NOT NULL,
  zip BLOB NOT NULL,            -- the launcher's save export (data\7952987A\saves)
  state TEXT NOT NULL DEFAULT 'pending',   -- pending, approved or hidden: only approved ones are public
  downloads INTEGER NOT NULL DEFAULT 0
);
CREATE TABLE IF NOT EXISTS team_log (
  n INTEGER PRIMARY KEY AUTOINCREMENT,
  at TEXT NOT NULL,             -- when (UTC)
  login TEXT NOT NULL,          -- the collaborator's GitHub login (team Worker, src/team.js)
  path TEXT NOT NULL            -- what they read
);
CREATE TABLE IF NOT EXISTS rate (
  who TEXT PRIMARY KEY,         -- salted hash of IP and hour; rows go after the hour
  hour INTEGER NOT NULL,
  n INTEGER NOT NULL
);
