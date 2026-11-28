"""The launcher's translations (launcher/lang/<code>.json, see launcher/Languages.cs).

  python scripts/launcher-lang.py check            every file: its {0} placeholders match the English, no key the code lost
  python scripts/launcher-lang.py missing es       the English texts launcher/lang/es.json does not translate yet
  python scripts/launcher-lang.py new de Deutsch   launcher/lang/de.json with every English text, to translate

A new language: run `new`, translate the values (keep {0}, {1:0.0} and line breaks as they are), run `check`,
build the launcher (launcher/build-launcher.cmd embeds every file in lang/). A text missing from a file shows
in English. Portuguese and French are not files: they are in the code (L's pt argument and French.cs).
"""
import glob, json, os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LANG = os.path.join(ROOT, "launcher", "lang")
S = r'"((?:[^"\\]|\\.)*)"'
CALL = re.compile(r'\bL\(\s*' + S + r'\s*,\s*' + S + r'(?:\s*,\s*' + S + r')?\s*\)')
ESC = {"r": "\r", "n": "\n", "t": "\t", '"': '"', "\\": "\\", "'": "'", "0": "\0"}


def unescape(s):
    return re.sub(r"\\(u[0-9a-fA-F]{4}|.)",
                  lambda m: chr(int(m.group(1)[1:], 16)) if len(m.group(1)) == 5 else ESC.get(m.group(1), "\\" + m.group(1)), s)


def english():
    """Every English text the launcher passes to L(), in source order."""
    seen = {}
    for f in sorted(glob.glob(os.path.join(ROOT, "launcher", "*.cs"))):
        for m in CALL.finditer(open(f, encoding="utf-8").read()):
            seen.setdefault(unescape(m.group(1)), None)
    return list(seen)


def holes(s):
    return sorted(re.findall(r"\{\d+[^}]*\}", s))


def load(code):
    return json.load(open(os.path.join(LANG, code + ".json"), encoding="utf-8"))


def main(argv):
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")  # the Windows console's code page cannot print every text
    if len(argv) >= 2 and argv[0] == "missing":
        texts = load(argv[1])
        for en in english():
            if en not in texts:
                print(json.dumps(en, ensure_ascii=False))
        return 0
    if len(argv) >= 3 and argv[0] == "new":
        path = os.path.join(LANG, argv[1] + ".json")
        if os.path.exists(path):
            print(f"{path} exists"); return 1
        out = {"_name": argv[2], **{en: en for en in english()}}
        json.dump(out, open(path, "w", encoding="utf-8", newline="\n"), ensure_ascii=False, indent=1)
        print(f"{path}: {len(out) - 1} texts to translate"); return 0
    if argv[:1] == ["check"]:
        known, bad = set(english()), 0
        for path in sorted(glob.glob(os.path.join(LANG, "*.json"))):
            texts = load(os.path.splitext(os.path.basename(path))[0])
            if not isinstance(texts.get("_name"), str):
                print(f"{path}: no _name"); bad += 1
            keys = [k for k in texts if not k.startswith("_")]
            for k in keys:
                if holes(k) != holes(texts[k]):
                    print(f"{path}: placeholders differ: {json.dumps(k, ensure_ascii=False)[:80]}"); bad += 1
            stale = [k for k in keys if k not in known]
            print(f"{os.path.basename(path)}: {len(keys) - len(stale)} of {len(known)} texts, {len(stale)} the code no longer has")
        return 1 if bad else 0
    print(__doc__); return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
