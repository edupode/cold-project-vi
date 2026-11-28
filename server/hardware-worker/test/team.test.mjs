// node --test server/hardware-worker/test
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { deflateRawSync } from 'node:zlib';
import { handle } from '../src/team.js';
import { zipEntries, zipFile } from '../src/zip.js';

// A zip of { name: text } entries, the first stored and the rest deflated.
function makeZip(files) {
  const locals = [], centrals = [];
  let offset = 0;
  Object.entries(files).forEach(([name, body], i) => {
    const raw = Buffer.from(body), packed = i === 0 ? raw : deflateRawSync(raw), method = i === 0 ? 0 : 8;
    const nm = Buffer.from(name);
    const local = Buffer.alloc(30); local.writeUInt32LE(0x04034b50, 0); local.writeUInt16LE(method, 8);
    local.writeUInt32LE(packed.length, 18); local.writeUInt32LE(raw.length, 22); local.writeUInt16LE(nm.length, 26);
    const central = Buffer.alloc(46); central.writeUInt32LE(0x02014b50, 0); central.writeUInt16LE(method, 10);
    central.writeUInt32LE(packed.length, 20); central.writeUInt32LE(raw.length, 24); central.writeUInt16LE(nm.length, 28);
    central.writeUInt32LE(offset, 42);
    locals.push(local, nm, packed); centrals.push(central, nm);
    offset += 30 + nm.length + packed.length;
  });
  const dir = Buffer.concat(centrals);
  const end = Buffer.alloc(22); end.writeUInt32LE(0x06054b50, 0); end.writeUInt16LE(Object.keys(files).length, 8);
  end.writeUInt16LE(Object.keys(files).length, 10); end.writeUInt32LE(dir.length, 12); end.writeUInt32LE(offset, 16);
  return new Uint8Array(Buffer.concat([...locals, dir, end]));
}

const ZIP = makeZip({ 'summary.txt': 'Launcher: v0.3.25\n', 'session-1.log': 'exception 0x80000003 at x.dll+0x10\n' });

// A D1 stand-in: answers the statements team.js makes, and records them.
function fakeDb() {
  const calls = [], log = [];
  const rows = [{ n: 7, created: '2026-11-28 06:00:00', vi_id: 'VI-AAAA-BBBB', kind: 'session', bytes: ZIP.length, zip: ZIP.buffer }];
  return {
    calls, log,
    prepare(sql) {
      return {
        bind(...args) {
          calls.push({ sql, args });
          return {
            all: async () => ({ results: rows.filter(r => r.created >= args[0]).map(({ zip, ...r }) => r) }),
            first: async () => (sql.includes('FROM reports') ? rows.find(r => r.n === args[0] && r.created >= args[1]) ?? null : null),
            run: async () => { if (sql.startsWith('INSERT INTO team_log')) log.push(args); return {}; },
          };
        },
      };
    },
  };
}

// GitHub stand-in: token "t-collab..." has push, "t-reader..." only pull, anything else is refused.
function fakeGitHub() {
  let n = 0;
  const f = async (url, init) => {
    n++;
    const token = init.headers.Authorization.slice(7);
    const who = token.startsWith('t-collab') ? 'alice' : token.startsWith('t-reader') ? 'bob' : null;
    if (!who) return new Response('', { status: 401 });
    if (url.endsWith('/user')) return Response.json({ login: who });
    return Response.json({ permissions: { pull: true, push: who === 'alice' } });
  };
  f.count = () => n;
  return f;
}

const env = (db, from = '2026-11-28 00:00:00') => ({ DB: db, TEAM_REPO: 'felicienradio/cold-project-vi', TEAM_EXTRA: 'Edupode', TEAM_FROM: from });
const get = (path, token) => new Request(`https://vi-team.example${path}`, { headers: token ? { Authorization: `Bearer ${token}` } : {} });

test('zip entries and both stored and deflated files read back', async () => {
  assert.deepEqual(zipEntries(ZIP).map(e => e.name), ['summary.txt', 'session-1.log']);
  assert.equal(await zipFile(ZIP, 'summary.txt'), 'Launcher: v0.3.25\n');
  assert.match(await zipFile(ZIP, 'session-1.log'), /0x80000003/);
  assert.equal(await zipFile(ZIP, 'missing.txt'), null);
});

test('no token, a bad token and a reader without push are refused, and nothing is logged', async () => {
  const db = fakeDb(), gh = fakeGitHub();
  for (const t of [null, 'short', 't-nobody-xxxxxxxxxxxxxxxxx', 't-reader-xxxxxxxxxxxxxxxxx'])
    assert.equal((await handle(get('/v1/reports', t), env(db), null, gh)).status, 401, String(t));
  assert.equal(db.log.length, 0);
});

test('a collaborator reads the list, one report, one of its files and the zip; each read is logged', async () => {
  const db = fakeDb(), gh = fakeGitHub(), t = 't-collab-xxxxxxxxxxxxxxxxx';
  const list = await (await handle(get('/v1/reports?kind=session&since=2026-11-28', t), env(db), null, gh)).json();
  assert.equal(list.reports[0].n, 7);
  assert.ok(!('zip' in list.reports[0]));
  const one = await (await handle(get('/v1/reports/7', t), env(db), null, gh)).json();
  assert.equal(one.summary, 'Launcher: v0.3.25\n');
  assert.deepEqual(one.files.map(f => f.name), ['summary.txt', 'session-1.log']);
  const log = await handle(get('/v1/reports/7/files/session-1.log', t), env(db), null, gh);
  assert.match(await log.text(), /0x80000003/);
  const zip = await handle(get('/v1/reports/7.zip', t), env(db), null, gh);
  assert.equal(zip.headers.get('content-type'), 'application/zip');
  assert.equal(db.log.length, 4);
  assert.equal(db.log[0][1], 'alice');
  // the token was checked with GitHub once (user + repo), then remembered
  assert.equal(gh.count(), 2);
});

test('a login in TEAM_EXTRA gets in without push', async () => {
  const db = fakeDb();
  const gh = async (url) => (url.endsWith('/user') ? Response.json({ login: 'edupode' }) : Response.json({ permissions: {} }));
  const me = await (await handle(get('/v1/me', 'gho_extraxxxxxxxxxxxxxxxxxx'), env(db), null, gh)).json();
  assert.equal(me.login, 'edupode');
});

test('only GET, and bad filters are refused before the database', async () => {
  const db = fakeDb(), gh = fakeGitHub(), t = 't-collab-yyyyyyyyyyyyyyyyy';
  const post = new Request('https://vi-team.example/v1/reports/7', { method: 'POST', headers: { Authorization: `Bearer ${t}` } });
  assert.equal((await handle(post, env(db), null, gh)).status, 405);
  for (const q of ['?id=nope', '?kind=all', "?since=2026' OR 1=1", '?before=x'])
    assert.equal((await handle(get('/v1/reports' + q, t), env(db), null, gh)).status, 400, q);
  assert.equal((await handle(get('/v1/reports/7/files/..%2F..%2Fx', t), env(db), null, gh)).status, 400);
  assert.ok(db.calls.every(c => !c.sql.includes("'")), 'values are bound, never pasted into SQL');
});

test('nothing is served without TEAM_FROM, and reports from before it are not found', async () => {
  const db = fakeDb(), gh = fakeGitHub(), t = 't-collab-zzzzzzzzzzzzzzzzz';
  assert.equal((await handle(get('/v1/reports', t), env(db, ''), null, gh)).status, 503);
  const later = env(db, '2026-11-29 00:00:00');
  assert.deepEqual((await (await handle(get('/v1/reports', t), later, null, gh)).json()).reports, []);
  for (const p of ['/v1/reports/7', '/v1/reports/7.zip', '/v1/reports/7/files/summary.txt'])
    assert.equal((await handle(get(p, t), later, null, gh)).status, 404, p);
  assert.equal((await handle(get('/v1/hardware/VI-AAAA-BBBB', t), later, null, gh)).status, 404);
});
