"""Players' reports, summed up: how each launcher version ended its games, why, at what frame rate, on what PCs.

  python scripts/report-stats.py --since "2026-11-28 05:51"            reports from then on (the wrangler login)
  python scripts/report-stats.py --api https://vi-team.<account>.workers.dev --since 2026-11-28    (a collaborator, gh login)
  python scripts/report-stats.py --folder reports/                     zips already downloaded (<n>.zip, index.tsv)
  ... --out docs/report-0.3.26.md                                       the summary as Markdown too

Each game gets one cause of its end: normal, stop in the game (int3, with the place), invalid access (with the place),
device removed, out of memory, incomplete game, closed within a minute without a word, or unknown. Frame rates come
from the engine's "N fps, M ms/frame" lines (the first 90 s of a game are left out: shaders compile then).
Downloads go to a folder of their own (default: reports/ in the repository, ignored by git).
"""
import argparse, collections, io, json, os, re, statistics, subprocess, sys, urllib.request, zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
WORKER = os.path.join(ROOT, "server", "hardware-worker")
ANSI = re.compile(r"\x1b\[[0-9;]*m")
EXC = re.compile(r"exception (0x[0-9a-f]+) at ([\w.\-]+?)(?:-[0-9a-f]{16})?(\.dll|\.exe)?\+(0x[0-9a-f]+)")
FPS = re.compile(r"^(\d{4}-\d\d-\d\dT[\d:.]+)Z\s+INFO victory_gpu::host: ([\d.]+) fps, ([\d.]+) ms/frame.*?(\d+) draws/frame, dropped (\d+) without a host pipeline \((\d+) being made\)", re.M)
# The presets by their English name, whatever the launcher's language was.
PRESETS = {"recomendado": "Beefy PC mode", "recommandé": "Beefy PC mode", "recommended": "Beefy PC mode", "beefy pc mode": "Beefy PC mode",
           "ultra baixo": "Ultra Low", "ultra bas": "Ultra Low", "ultra bajo": "Ultra Low", "ultra low": "Ultra Low",
           "nitido": "Sharp", "nítido": "Sharp", "net": "Sharp", "sharp": "Sharp",
           "mais rapido": "Fastest", "le plus rapide": "Fastest", "más rápido": "Fastest", "fastest": "Fastest",
           "pc modesto": "Modest PC", "pc modeste": "Modest PC", "modest pc": "Modest PC",
           "cgbj classico": "Classic cgbj", "cgbj classique": "Classic cgbj", "cgbj clásico": "Classic cgbj", "classic cgbj": "Classic cgbj",
           "personalizado": "Custom", "personnalisé": "Custom", "custom": "Custom"}
# Exit codes with a name.
CODES = {"-1073740791": "fail-fast 0xC0000409", "-1073741571": "stack overflow 0xC00000FD", "-805306369": "code 0xCFFFFFFF", "1": "the engine gave up (code 1)"}


# ---------------------------------------------------------------- fetching
def d1(sql):
    out = subprocess.run(["npx", "--yes", "wrangler", "d1", "execute", "vi-hardware", "--remote", "--json", "--command", sql],
                         cwd=WORKER, capture_output=True, text=True, encoding="utf-8", shell=os.name == "nt").stdout
    return json.loads(out[out.index("["):])[0]["results"]


def fetch_wrangler(since, folder):
    rows = d1(f"SELECT n, created, vi_id, kind FROM reports WHERE created >= '{since}' ORDER BY n")
    todo = [r for r in rows if not os.path.exists(os.path.join(folder, f"{r['n']}.zip"))]
    for i in range(0, len(todo), 25):
        ns = ",".join(str(r["n"]) for r in todo[i:i + 25])
        for r in d1(f"SELECT n, hex(zip) AS z FROM reports WHERE n IN ({ns})"):
            open(os.path.join(folder, f"{r['n']}.zip"), "wb").write(bytes.fromhex(r["z"]))
        print(f"  {min(i + 25, len(todo))} of {len(todo)} downloaded", file=sys.stderr)
    return rows


def fetch_api(api, since, folder):
    token = subprocess.run(["gh", "auth", "token"], capture_output=True, text=True).stdout.strip()
    get = lambda path: urllib.request.urlopen(urllib.request.Request(api.rstrip("/") + path, headers={"Authorization": "Bearer " + token}), timeout=60)
    rows, before = [], ""
    while True:
        page = json.load(get(f"/v1/reports?since={urllib.request.quote(since)}&limit=500{before}"))
        rows += page["reports"]
        if not page["next"]:
            break
        before = f"&before={page['next']}"
    for r in rows:
        path = os.path.join(folder, f"{r['n']}.zip")
        if not os.path.exists(path):
            open(path, "wb").write(get(f"/v1/reports/{r['n']}.zip").read())
    return rows


# ---------------------------------------------------------------- reading one report
def field(text, pattern):
    m = re.search(pattern, text)
    return m.group(1).strip() if m else ""


def vendor(gpu):
    g = gpu.lower()
    return "NVIDIA" if any(k in g for k in ("nvidia", "geforce", "rtx", "gtx")) else "AMD" if ("radeon" in g or "amd" in g) else "Intel" if ("intel" in g or "arc" in g) else "other"


def read(path, row):
    z = zipfile.ZipFile(path)
    files = {n: ANSI.sub("", z.read(n).decode("utf-8", "replace")) for n in z.namelist()}
    s = next((v for k, v in files.items() if k.endswith("summary.txt")), "")
    log = "\n".join(v for k, v in files.items() if k.endswith(".log"))
    played = field(s, r"Played:\s*([\d.,]+)").replace(",", ".")
    code = field(s, r"code (-?\d+)")
    excs = collections.Counter(f"{m.group(1)} {m.group(2)}{m.group(3) or ''}+{m.group(4)}" for m in EXC.finditer(log))
    r = {
        "n": row["n"], "vi": row["vi_id"], "kind": row["kind"], "created": row["created"],
        "launcher": field(s, r"Launcher:\s*v([\d.]+)"),
        "engine": "belobug" if "Belobug" in field(s, r"Engine:\s*(.+?)\s{2,}") else "edupode",
        "preset": PRESETS.get(field(s, r"Preset:\s*(.+?)(?:\s{2,}|$)").lower(), field(s, r"Preset:\s*(.+?)(?:\s{2,}|$)")),
        "gpu": field(s, r"GPU:\s*(.+?)\s*\("), "ram": field(s, r"RAM:\s*(\d+)"),
        "played": float(played) if played else None, "code": code,
        "dlss": field(s, r"VICTORY_DLSS_MODE=(\S+)"), "incomplete": "Incomplete game:" in s,
        "excs": dict(excs.most_common(3)),
    }
    r["vendor"] = vendor(r["gpu"])
    # The player's problem marks (Numpad/Alt + 1-9) and the engine's own HITCH / FPS DROP lines (victory-gpu
    # host/marks.rs), from the summary (whole) or else from the log (its end only).
    marks = re.findall(r"MARK (\d) ([A-Z ]+?) at ([\d.]+) min: ([\d.]+) fps", s + "\n" + log)
    r["marks"] = sorted({(int(k), name, float(m), float(fps)) for k, name, m, fps in marks})
    pm = re.search(r"Hitches: (\d+) lines \(the longest (\d+) ms\)\s+FPS drops: (\d+)", s)
    r["hitches"], r["longest_hitch"], r["drops"] = (int(pm.group(1)), int(pm.group(2)), int(pm.group(3))) if pm else (
        len(re.findall(r"HITCH at ", log)), 0, len(re.findall(r"FPS DROP at ", log)))
    # Frame rate in play: the engine's 5-second lines after the first 90 s.
    lines = FPS.findall(log)
    if lines:
        from datetime import datetime
        t0 = datetime.fromisoformat(lines[0][0][:19])
        late = [l for l in lines if (datetime.fromisoformat(l[0][:19]) - t0).total_seconds() >= 90]
        # In play: 1,500 draws a frame or more (the menus draw a few hundred at the title's 30 fps).
        world = [l for l in late if int(l[3]) >= 1500]
        fps = [float(l[1]) for l in world if float(l[1]) > 0.5]
        r["fps"] = statistics.median(fps) if fps else None
        r["fps_low"] = sorted(fps)[max(0, len(fps) // 20 - 1)] if len(fps) >= 5 else None
        r["dropped"] = statistics.mean(int(l[4]) for l in world) if world else None
    # Why it ended.
    if code in ("", "0"):
        r["cause"] = "normal" if code == "0" else "unknown"
    elif r["incomplete"]:
        r["cause"] = "incomplete game"
    elif re.search(r"0x887A0005|DEVICE_REMOVED|device removed", log, re.I) or code == "-2005270523":
        r["cause"] = "device removed"
    elif re.search(r"0x8007000E|did not close \(0x8007000E", log) or "E_OUTOFMEMORY" in log:
        r["cause"] = "out of memory"
    elif code == "-2147483645":
        top = next(iter(r["excs"]), "")
        r["cause"] = "game stop " + top.split(" ", 1)[-1] if top else "game stop"
    elif code == "-1073741819":
        top = next((e for e in r["excs"] if e.startswith("0xc0000005")), "")
        r["cause"] = "invalid access " + top.split(" ", 1)[-1] if top else "invalid access"
    elif code in CODES:
        r["cause"] = CODES[code]
    elif r["played"] is not None and r["played"] < 1:
        r["cause"] = f"closed within a minute (code {code})"
    else:
        r["cause"] = f"error code {code}"
    return r


# ---------------------------------------------------------------- the summary
def table(title, groups, out):
    out.append(f"\n### {title}\n\n| | games | players | normal end | median minutes | median fps (in play) | fps, slowest 5 % |\n|---|---|---|---|---|---|---|")
    for key, rs in sorted(groups.items(), key=lambda kv: -len(kv[1])):
        mins = [r["played"] for r in rs if r["played"] is not None]
        fps = [r["fps"] for r in rs if r.get("fps")]
        low = [r["fps_low"] for r in rs if r.get("fps_low")]
        normal = sum(r["cause"] == "normal" for r in rs)
        out.append(f"| {key or '-'} | {len(rs)} | {len({r['vi'] for r in rs})} | {100 * normal // max(1, len(rs))} % | "
                   f"{statistics.median(mins):.1f} | {statistics.median(fps):.1f} | {statistics.median(low):.1f} |" if fps and low and mins else
                   f"| {key or '-'} | {len(rs)} | {len({r['vi'] for r in rs})} | {100 * normal // max(1, len(rs))} % | "
                   f"{statistics.median(mins):.1f} | - | - |" if mins else f"| {key or '-'} | {len(rs)} | - | - | - | - | - |")


def summary(rs):
    out = [f"# Players' reports: {len(rs)} games, {len({r['vi'] for r in rs})} players",
           f"\n{min(r['created'] for r in rs)} to {max(r['created'] for r in rs)} UTC. One game is one report (a session log, or an error report)."]
    by = lambda key: collections.defaultdict(list, {k: [r for r in rs if key(r) == k] for k in {key(r) for r in rs}})
    table("By launcher version", by(lambda r: r["launcher"]), out)
    table("By engine and card maker", by(lambda r: f"{r['engine']} · {r['vendor']}"), out)
    table("By preset", by(lambda r: r["preset"]), out)
    out.append("\n### Why games ended\n\n| cause | games | players | cards |\n|---|---|---|---|")
    causes = collections.defaultdict(list)
    for r in rs:
        causes[r["cause"]].append(r)
    for c, group in sorted(causes.items(), key=lambda kv: -len(kv[1])):
        cards = collections.Counter(r["gpu"] for r in group).most_common(3)
        out.append(f"| {c} | {len(group)} | {len({r['vi'] for r in group})} | " + "; ".join(f"{g or '?'} ×{n}" for g, n in cards) + " |")
    marked = [(r, m) for r in rs for m in r.get("marks", [])]
    if marked:
        out.append("\n### Problems the players marked (Numpad/Alt + 1-9)\n\n| kind | marks | players | median fps at the mark | presets |\n|---|---|---|---|---|")
        kinds = collections.defaultdict(list)
        for r, m in marked:
            kinds[f"{m[0]} {m[1]}"].append((r, m))
        for k, group in sorted(kinds.items(), key=lambda kv: -len(kv[1])):
            presets = collections.Counter(r["preset"] for r, _ in group).most_common(3)
            out.append(f"| {k} | {len(group)} | {len({r['vi'] for r, _ in group})} | {statistics.median(m[3] for _, m in group):.1f} | "
                       + ", ".join(f"{p} x{n}" for p, n in presets) + " |")
    hours = sum((r["played"] or 0) for r in rs) / 60
    if hours > 0:
        out.append(f"\nHitches (frames far longer than those around them, at most one line in 5 s): {sum(r.get('hitches', 0) for r in rs) / hours:.1f} "
                   f"lines an hour; FPS drops (the last 5 s under 60 % of the half minute before): {sum(r.get('drops', 0) for r in rs) / hours:.1f} an hour.")
    slow = [r for r in rs if r.get("dropped") and r["dropped"] > 50]
    if slow:
        out.append(f"\n{len(slow)} games drew without a pipeline for more than 50 draws a frame after the first 90 s "
                   "(shaders still compiling: a first game on a new engine, or a cache that keeps being thrown away).")
    return "\n".join(out) + "\n"


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--since", default="2026-11-28 00:00")
    ap.add_argument("--api")
    ap.add_argument("--folder", default=os.path.join(ROOT, "reports"))
    ap.add_argument("--offline", action="store_true", help="only the zips already in --folder (with its index.tsv)")
    ap.add_argument("--out")
    a = ap.parse_args()
    os.makedirs(a.folder, exist_ok=True)
    index = os.path.join(a.folder, "index.json")
    if a.offline:
        rows = json.load(open(index, encoding="utf-8")) if os.path.exists(index) else [
            dict(zip(("n", "created", "vi_id", "kind"), l.split("\t"))) for l in open(os.path.join(a.folder, "index.tsv"), encoding="utf-8").read().splitlines()]
    else:
        rows = fetch_api(a.api, a.since, a.folder) if a.api else fetch_wrangler(a.since, a.folder)
        json.dump(rows, open(index, "w", encoding="utf-8"))
    rs = []
    for row in rows:
        path = os.path.join(a.folder, f"{row['n']}.zip")
        if os.path.exists(path) and row["created"] >= a.since:
            try:
                rs.append(read(path, row))
            except (zipfile.BadZipFile, KeyError):
                pass
    if not rs:
        print("no reports"); return 1
    text = summary(rs)
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    print(text)
    if a.out:
        open(a.out, "w", encoding="utf-8", newline="\n").write(text)
    json.dump(rs, open(os.path.join(a.folder, "parsed.json"), "w", encoding="utf-8"), indent=0)
    return 0


if __name__ == "__main__":
    sys.exit(main())
