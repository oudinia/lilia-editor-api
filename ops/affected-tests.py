#!/usr/bin/env python3
"""The API tests a branch's changes can affect (2 Nov 2026: "use a bit more intelligence for CI to
run only tests for which code has been modified").

    ops/affected-tests.py [base] [head]   base: what the branch is compared with (origin/main);
                                          head: a commit to look at instead of the working tree's

Prints a JSON object: {"mode": "all" | "some" | "fast" | "none", "filter": "...", "classes": n,
"why": [...], "admin": bool, "migrations": bool}. ops/ci-local.sh runs the tests it names; the
nightly run (ops/tex-tests.sh on the server) runs every test, so whatever this misses is found
within a day.

How a change reaches a test:
  1. the types the changed files declare;
  2. every source file that uses one of those types, and the types it declares, again and again
     (a change to something used everywhere so reaches everything, as it should);
  3. a test class that names one of the reached types, and a database test that calls the route of a
     reached controller (they reach the code through URLs, not type names).
Shared ground (Program.cs, the database context, project files, the test infrastructure, CI itself)
means every test, as does a selection of most of them.
"""
import json
import os
import re
import subprocess
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
TESTS = "tests/Lilia.Api.Tests/"

# A change here can affect any test: run them all.
EVERYTHING = [
    r"\.csproj$", r"\.slnx?$", r"Directory\.Build\.", r"\.props$", r"\.targets$", r"global\.json$",
    r"(^|/)nuget\.config$", r"^src/Lilia\.Api/Program\.cs$", r"appsettings[^/]*\.json$",
    r"^src/Lilia\.Infrastructure/Data/LiliaDbContext\.cs$",
    r"^tests/Lilia\.Api\.Tests/Integration/Infrastructure/", r"^tests/Lilia\.Api\.Tests/TestAssemblySetup\.cs$",
    r"^tests/Lilia\.Api\.Tests/Fixtures/", r"^ops/ci-local\.sh$", r"^ops/affected-tests\.py$",
]
# The admin console's code and tests: its tests run when they change (and every night).
ADMIN = [r"^src/Lilia\.Admin/", r"^tests/Lilia\.Admin\.Tests/"]
# What the database model is made of: the migrations check runs when one changes.
MODEL = [r"^src/Lilia\.Infrastructure/Migrations/", r"^src/Lilia\.Infrastructure/Data/",
         r"^src/Lilia\.Core/Entities/"]
# Nothing a test runs: documentation, the GitHub workflows (disabled), server scripts.
NOTHING = [r"\.md$", r"^docs/", r"^\.github/", r"^ops/", r"\.gitignore$", r"^LICENSE"]

DEPTH = int(os.environ.get("AFFECTED_DEPTH", "2"))
# The API's code: Lilia.Api, its contracts, and the slices that are projects (2 Nov 2026).
API_CODE = ("src/Lilia.Api/", "src/Lilia.Api.Contracts/", "src/Lilia.Features.")
COMPOSITION_ROOT = {"src/Lilia.Api/Program.cs", "src/Lilia.Admin/Program.cs", "src/Lilia.Tools.Api/Program.cs"}
DECL = re.compile(r"\b(?:class|record|struct|interface|enum)\s+([A-Z][A-Za-z0-9_]*)")
WORD = re.compile(r"[A-Za-z_][A-Za-z0-9_]*")
NAMESPACE = re.compile(r"^\s*namespace\s+([A-Za-z0-9_.]+)", re.M)
ROUTE = re.compile(r'\[Route\("([^"]*)"\)\]')
COMMENT = re.compile(r"//[^\n]*|/\*.*?\*/", re.S)


def git(*args):
    return subprocess.run(["git", "-C", ROOT, *args], capture_output=True, text=True, check=True).stdout


def any_match(path, patterns):
    return any(re.search(p, path) for p in patterns)


def sources(prefix):
    out = []
    for top, _, files in os.walk(os.path.join(ROOT, prefix)):
        if "/bin" in top or "/obj" in top:
            continue
        out += [os.path.relpath(os.path.join(top, f), ROOT) for f in files if f.endswith(".cs")]
    return out


def read(path):
    try:
        with open(os.path.join(ROOT, path), encoding="utf-8", errors="replace") as f:
            return f.read()
    except FileNotFoundError:
        return ""


def route_pattern(template, controller):
    """A controller's route as a pattern for the URLs tests write: a parameter is any segment."""
    template = template.strip("/~").replace("[controller]", controller)
    parts = []
    for seg in template.split("/"):
        parts.append(r'[^/"\s]+' if seg.startswith("{") else re.escape(seg))
    return re.compile("(?i)" + "/".join(parts) + r'(?=[/"?\s{]|$)')


BASES = re.compile(r"\b(?:class|record|struct)\s+([A-Z]\w*)(?:<[^>{]*>)?(?:\([^){]*\))?\s*:\s*([^{;]+)")
INDEX = {}


def index():
    """Every source and test file: what it declares, the words it uses (comments aside), and what each
    declared type implements or extends."""
    if INDEX:
        return INDEX
    src_files, test_files = sources("src"), sources(TESTS)
    words, declares, bases = {}, {}, {}
    for f in src_files + test_files:
        text = COMMENT.sub("", read(f))
        words[f] = set(WORD.findall(text))
        declares[f] = set(DECL.findall(text))
        if f in src_files:
            for name, base_list in BASES.findall(text):
                bases.setdefault(name, set()).update(re.findall(r"\b([A-Z]\w*)", re.sub(r"\bwhere\b.*", "", base_list)))
    seen = {}
    for f in src_files:
        for d in declares[f]:
            seen[d] = seen.get(d, 0) + 1
    INDEX.update(src=src_files, tests=test_files, words=words, declares=declares, bases=bases, seen=seen)
    return INDEX


def select(changed):
    """What to run for these changed paths: {"mode", "why", "classes": [...], "admin", "migrations"}."""
    out = {"mode": "none", "why": "", "classes": [],
           "admin": any(any_match(c, ADMIN) for c in changed),
           "migrations": any(any_match(c, MODEL) for c in changed)}
    if not changed:
        return {**out, "why": "no change"}
    for c in changed:
        if any_match(c, EVERYTHING):
            return {**out, "mode": "all", "why": f"{c} is shared ground"}
    code = [c for c in changed if not any_match(c, NOTHING) and not any_match(c, ADMIN)]
    if not code:
        return {**out, "why": "only documentation, scripts or the admin console changed"}

    ix = index()
    src_files, test_files, words, declares, bases, seen = (ix[k] for k in ("src", "tests", "words", "declares", "bases", "seen"))

    def with_contracts(types):
        # A class reaches the interfaces and base classes it implements that Lilia declares: the
        # controllers use ICollaboratorService, not CollaboratorService (2 Nov: the implementation's
        # change reached no test).
        return types | {b for t in types for b in bases.get(t, ()) if seen.get(b, 0) == 1 or contract(b)}

    def contract(t):
        # An interface (IName) Lilia declares: followed even when another project declares the same
        # name (Lilia.Tools.Api has its own IEntitlementService), unlike a plain name such as Entry.
        return seen.get(t, 0) >= 1 and len(t) > 1 and t[0] == "I" and t[1].isupper()

    # 1. What changed: the types the changed .cs files declare; a resource (a .sty, a skill's .md,
    #    a template) by its file name, which the code that loads it spells out.
    reached = set()
    for c in code:
        if c.startswith("src/") and c.endswith(".cs"):
            reached |= declares.get(c, set()) or {os.path.basename(c)[:-3]}
        elif c.startswith("src/"):
            name = os.path.basename(c)
            reached |= {next(iter(declares[f]), "") for f in src_files
                        if name in read(f) and declares[f]} - {""}
    reached = with_contracts(reached)
    # 2. Through the code, DEPTH levels out: the direct users and their users. Further than that
    #    nearly every change reached the entities and the database context and so every test (a
    #    rendering change reached 353 of 382 classes); the nightly full run covers the rest. A name
    #    declared in more than one file (Entry, Result, Options) carries nothing past the change.
    frontier = set(reached)
    for _ in range(DEPTH):
        # Program.cs registers every service, so it "uses" everything: through it, any change reached
        # the 12 test classes built on Program (14% of test time). A change to Program.cs itself runs
        # everything (EVERYTHING above).
        users = {f for f in src_files if words[f] & frontier and f not in COMPOSITION_ROOT}
        new = with_contracts(set().union(*(declares[f] for f in users))) - reached if users else set()
        reached |= new
        frontier = {t for t in new if seen.get(t, 0) == 1 or contract(t)}

    # 3. The tests: changed test files, tests naming a reached type, database tests calling the route
    #    of a reached controller (in Controllers/ or a feature's folder).
    routes = []
    for f in src_files:
        if f.startswith(API_CODE) and declares[f] & reached and any(d.endswith("Controller") for d in declares[f]):
            name = next(d for d in declares[f] if d.endswith("Controller"))
            for t in ROUTE.findall(read(f)):
                routes.append(route_pattern(t, name[: -len("Controller")].lower()))
    classes = set()
    for f in test_files:
        if f in changed or words[f] & reached or (routes and any(r.search(read(f)) for r in routes)):
            text = read(f)
            ns = NAMESPACE.search(text)
            for cls in re.findall(r"^\s*public\s+(?:sealed\s+|partial\s+)*class\s+([A-Za-z0-9_]+)", text, re.M):
                classes.add(f"{ns.group(1)}.{cls}." if ns else f".{cls}.")
    all_classes = sum(len(re.findall(r"^\s*public\s+(?:sealed\s+|partial\s+)*class\s+\w+", read(f), re.M))
                      for f in test_files)
    if not classes:
        return {**out, "mode": "fast", "why": "code changed but no test names what it reaches: the fast lane"}
    # The slices' boundary test reads the source, not a type: it names nothing a change reaches.
    if any(c.startswith(API_CODE) for c in changed):
        classes.add("Lilia.Api.Tests.Shared.SliceBoundaryTests.")
    if len(classes) > 0.7 * all_classes:
        return {**out, "mode": "all", "why": f"{len(classes)} of {all_classes} test classes reached: all of them"}
    return {**out, "mode": "some", "classes": sorted(classes),
            "why": f"{len(reached)} types reached, {len(routes)} routes; {len(classes)} of {all_classes} test classes"}


def main():
    base = sys.argv[1] if len(sys.argv) > 1 else "origin/main"
    head = sys.argv[2] if len(sys.argv) > 2 else "HEAD"
    changed = [l for l in git("diff", "--name-only", f"{base}...{head}").splitlines() if l]
    if head == "HEAD":  # and what isn't committed yet
        changed += [l for l in git("diff", "--name-only", "HEAD").splitlines() if l and l not in changed]
    sel = select(changed)
    flt = "(" + "|".join(f"FullyQualifiedName~{c}" for c in sel["classes"]) + ")" if sel["classes"] else ""
    print(json.dumps({"mode": sel["mode"], "filter": flt, "classes": len(sel["classes"]), "why": [sel["why"]],
                      "changed": len(changed), "admin": sel["admin"], "migrations": sel["migrations"]}))


if __name__ == "__main__":
    try:
        main()
    except SystemExit:
        raise
    except Exception as e:  # never fewer tests because the selector broke
        print(json.dumps({"mode": "all", "filter": "", "classes": 0, "why": [f"the selector failed: {e}"],
                          "changed": 0, "admin": True, "migrations": True}))
