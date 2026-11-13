import re
import os
root = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..") + os.sep
s = open(root + "InstallerEngine.cs", encoding="utf-8").read()
a = s.index("public static readonly IReadOnlyList<AppEntry> Catalog")
b = s.index("public static List<AppEntry> CatalogFor")
block = s[a:b]

# split into entries: each starts with "new() {" and ends at the matching "}"
entries = []
i = 0
while True:
    i = block.find("new() {", i)
    if i < 0: break
    depth = 0; j = i
    while True:
        c = block[j]
        if c == "{": depth += 1
        elif c == "}":
            depth -= 1
            if depth == 0: break
        j += 1
    entries.append(block[i:j + 1]); i = j + 1

rows = []
for e in entries:
    name = re.search(r'FriendlyName = "([^"]+)"', e).group(1)
    cat = re.search(r'Category = "([^"]+)"', e).group(1)
    kind = re.search(r"Kind = AppKind\.(\w+)", e).group(1)
    wid = re.search(r'WingetId = "([^"]+)"', e)
    url = re.search(r'PageUrl = "([^"]+)"', e)
    murl = re.search(r'ManualUrl = "([^"]+)"', e)
    m = re.search(r"Profiles = ([^,}\n]+)", e)
    prof = m.group(1).strip() if m else "Gm"
    profs = set()
    for tag, full in (("Gm", "Gaming"), ("Of", "Office"), ("Dv", "Developer")):
        if tag in prof or "Everyone" in prof: profs.add(full)
    if kind == "Winget": how = f"winget `{wid.group(1)}`"
    elif kind == "Bundle": how = "winget bundle"
    elif kind == "Link": how = f"opens [{re.sub(r'^https://([^/]+).*', r'\1', url.group(1))}]({url.group(1)})"
    else: how = "direct download" + (f" (page: [{re.sub(r'^https://([^/]+).*', r'\1', url.group(1))}]({url.group(1)}))" if url else "")
    rows.append((name, cat, how, profs))

out = ["# App catalog", "",
       "Everything GamingStack can install, by setup type. This file is generated from the catalog in",
       "`InstallerEngine.cs`, so it always matches the app. **Nothing is installed unless you say yes** on the",
       "selection screens. \"winget\" entries install silently through Windows' own package manager;",
       "\"opens\" entries have no winget package, so GamingStack just opens the vendor's own download page",
       "in your default browser and you install from there.", ""]
for prof in ("Gaming", "Office", "Developer"):
    items = [r for r in rows if prof in r[3]]
    out += [f"## {prof} ({len(items)} items)", ""]
    cats = []
    for r in items:
        if r[1] not in cats: cats.append(r[1])
    for c in cats:
        out += [f"### {c}", "", "| App | How it installs |", "|---|---|"]
        for r in items:
            if r[1] == c: out.append(f"| {r[0]} | {r[2]} |")
        out.append("")
out += ["---", f"_{len(rows)} entries in total. Some appear in more than one setup type._", ""]
open(root + "docs\\CATALOG.md", "w", encoding="utf-8").write("\n".join(out))
print(len(rows), {p: len([r for r in rows if p in r[3]]) for p in ("Gaming", "Office", "Developer")})
