// The team's read-only API to the bug reports, a Worker of its own (wrangler.team.toml).
// Who may read: anyone with write access to TEAM_REPO on GitHub (its collaborators), checked on every
// request with the caller's own GitHub token, plus the logins in TEAM_EXTRA. Nothing here changes or
// deletes anything; every request that gets in is written to team_log (who, when, what).
//
//   Authorization: Bearer <a GitHub token of yours>   (`gh auth token` prints one)
//   GET /v1/me                                 who you are to the API
//   GET /v1/reports?id=&kind=&since=&before=&limit=   reports, newest first (no contents), at most 500
//   GET /v1/reports/<n>                        one report: its row, its files and summary.txt
//   GET /v1/reports/<n>/files/<name>           one file of the report, as text
//   GET /v1/reports/<n>.zip                    the whole report
//
// What the reports hold: what players sent (logs, options, a summary; user names taken out of every
// path). Only reports received from TEAM_FROM on are served: before the launcher release that tells
// players the project's team reads them, players were told only edupode does, and those reports stay
// his. With TEAM_FROM unset nothing is served. The hardware table is not served either (players were
// told only edupode sees that list).

import { zipEntries, zipFile } from './zip.js';

const ID = /^VI-[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}$/;
const KINDS = new Set(['game', 'session', 'launcher']);
const STAMP = /^\d{4}-\d{2}-\d{2}( \d{2}:\d{2}(:\d{2})?)?$/;
const FILE = /^[\w.\- ]{1,120}$/;
const TTL_MS = 10 * 60 * 1000;

// token hash -> { login, until }: one GitHub check per token every 10 minutes, per Worker instance.
const seen = new Map();

const json = (body, status = 200) =>
  new Response(JSON.stringify(body, null, 1), { status, headers: { 'content-type': 'application/json; charset=utf-8', 'cache-control': 'no-store' } });
const text = (body, status = 200) =>
  new Response(body, { status, headers: { 'content-type': 'text/plain; charset=utf-8', 'cache-control': 'no-store' } });

async function sha256(s) {
  const d = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(s));
  return [...new Uint8Array(d)].map(b => b.toString(16).padStart(2, '0')).join('');
}

// The caller's GitHub login when they may read, else null. `fetcher` is fetch (tests give their own).
export async function member(request, env, fetcher = fetch) {
  const auth = request.headers.get('Authorization') || '';
  const m = auth.match(/^(?:Bearer|token) +([\w-]{20,255})$/);
  if (!m) return null;
  const key = await sha256(m[1]);
  const hit = seen.get(key);
  if (hit && hit.until > Date.now()) return hit.login;
  const gh = (path) => fetcher(`https://api.github.com${path}`, {
    headers: { Authorization: `Bearer ${m[1]}`, Accept: 'application/vnd.github+json', 'User-Agent': 'vi-team-api', 'X-GitHub-Api-Version': '2022-11-28' },
  });
  const user = await gh('/user');
  if (!user.ok) return null;
  const login = (await user.json()).login;
  if (!login) return null;
  const extra = String(env.TEAM_EXTRA || '').split(',').map(s => s.trim().toLowerCase()).filter(Boolean);
  let ok = extra.includes(login.toLowerCase());
  if (!ok) {
    const repo = await gh(`/repos/${env.TEAM_REPO}`);
    if (repo.ok) {
      const p = (await repo.json()).permissions || {};
      ok = !!(p.admin || p.maintain || p.push);
    }
  }
  if (!ok) return null;
  seen.set(key, { login, until: Date.now() + TTL_MS });
  if (seen.size > 500) seen.delete(seen.keys().next().value);
  return login;
}

// The first moment whose reports the team may read (`YYYY-MM-DD HH:MM:SS`, UTC), or null.
function from(env) {
  const f = String(env.TEAM_FROM || '').trim();
  return STAMP.test(f) ? f : null;
}

async function list(env, q) {
  const where = ['created >= ?'], args = [from(env)];
  const id = q.get('id'), kind = q.get('kind'), since = q.get('since'), before = q.get('before');
  if (id) { if (!ID.test(id.toUpperCase())) return json({ error: 'id: not VI-XXXX-XXXX' }, 400); where.push('vi_id = ?'); args.push(id.toUpperCase()); }
  if (kind) { if (!KINDS.has(kind)) return json({ error: 'kind: game, session or launcher' }, 400); where.push('kind = ?'); args.push(kind); }
  if (since) { if (!STAMP.test(since)) return json({ error: 'since: YYYY-MM-DD[ HH:MM[:SS]] (UTC)' }, 400); where.push('created >= ?'); args.push(since); }
  if (before) { if (!/^\d{1,9}$/.test(before)) return json({ error: 'before: a report number' }, 400); where.push('n < ?'); args.push(Number(before)); }
  const limit = Math.min(500, Math.max(1, Number(q.get('limit')) || 100));
  const sql = `SELECT n, created, vi_id, kind, bytes FROM reports WHERE ${where.join(' AND ')} ORDER BY n DESC LIMIT ${limit}`;
  const rows = (await env.DB.prepare(sql).bind(...args).all()).results;
  return json({ reports: rows, next: rows.length === limit ? rows[rows.length - 1].n : null });
}

async function row(env, n) {
  return env.DB.prepare('SELECT n, created, vi_id, kind, bytes, zip FROM reports WHERE n = ? AND created >= ?').bind(n, from(env)).first();
}

async function one(env, n) {
  const r = await row(env, n);
  if (!r) return json({ error: 'no such report' }, 404);
  const zip = new Uint8Array(r.zip);
  const files = (zipEntries(zip) || []).map(e => ({ name: e.name, bytes: e.unpacked }));
  let summary = null;
  try { summary = await zipFile(zip, 'summary.txt'); } catch { }
  return json({ n: r.n, created: r.created, vi_id: r.vi_id, kind: r.kind, bytes: r.bytes, files, summary });
}

async function file(env, n, name) {
  if (!FILE.test(name)) return json({ error: 'not a file name' }, 400);
  const r = await row(env, n);
  if (!r) return json({ error: 'no such report' }, 404);
  let body = null;
  try { body = await zipFile(new Uint8Array(r.zip), name); } catch { }
  return body == null ? json({ error: 'no such file in the report' }, 404) : text(body);
}

export async function handle(request, env, ctx, fetcher = fetch) {
  if (request.method !== 'GET') return json({ error: 'read only' }, 405);
  if (!from(env)) return json({ error: 'not open yet: TEAM_FROM is not set' }, 503);
  const login = await member(request, env, fetcher);
  if (!login) return json({ error: `a GitHub token of a collaborator of ${env.TEAM_REPO} is needed (Authorization: Bearer ...)` }, 401);
  const url = new URL(request.url);
  const path = decodeURIComponent(url.pathname);
  const logged = env.DB.prepare('INSERT INTO team_log (at, login, path) VALUES (?, ?, ?)')
    .bind(new Date().toISOString(), login, (path + url.search).slice(0, 300)).run().catch(() => { });
  if (ctx && ctx.waitUntil) ctx.waitUntil(logged); else await logged;

  if (path === '/v1/me') return json({ login, repo: env.TEAM_REPO });
  if (path === '/v1/reports') return list(env, url.searchParams);
  let m = path.match(/^\/v1\/reports\/(\d{1,9})$/);
  if (m) return one(env, Number(m[1]));
  m = path.match(/^\/v1\/reports\/(\d{1,9})\/files\/(.+)$/);
  if (m) return file(env, Number(m[1]), m[2]);
  m = path.match(/^\/v1\/reports\/(\d{1,9})\.zip$/);
  if (m) {
    const r = await row(env, Number(m[1]));
    return r
      ? new Response(r.zip, { headers: { 'content-type': 'application/zip', 'content-disposition': `attachment; filename="report-${m[1]}-${r.vi_id}-${r.kind}.zip"`, 'cache-control': 'no-store' } })
      : json({ error: 'no such report' }, 404);
  }
  return json({ error: 'no such route' }, 404);
}

export default { fetch: (request, env, ctx) => handle(request, env, ctx) };
