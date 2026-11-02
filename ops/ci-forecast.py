#!/usr/bin/env python3
"""How long CI will take for a branch, before running it (2 Nov 2026, the owner: "can you give me a ci
forecast now", "in terms of timing", "if you modify a common").

    ops/ci-forecast.py [base] [head]       the same arguments as ops/affected-tests.py
    ops/ci-forecast.py --file <path> …     as if only these files had changed

It asks ops/affected-tests.py which test classes the change reaches, then adds up their measured times
(ops/test-durations.json: seconds per test class, from a full run with the trx logger). The test step
is the host's start plus the classes' time shared over the parallel workers, and never less than the
slowest class, which runs on one worker. The build is the measured time of a build after a change in
the project the changed files belong to (the slowest of them).

Refresh the durations after a big change to the tests:
    dotnet test tests/Lilia.Api.Tests --filter "Category!=Nightly" --logger "trx;LogFileName=all.trx" \\
        --results-directory /tmp/trx && ops/ci-forecast.py --refresh /tmp/trx/all.trx
"""
import importlib.util
import json
import os
import re
import sys
import xml.etree.ElementTree as ET

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
DURATIONS = os.path.join(ROOT, "ops", "test-durations.json")

# Seconds to build the test project after a one-line change in each project (2 Nov, this server, warm).
# Measured: Lilia.Api 13.3-13.7, a slice project 5.2-5.5, Engines 10.2, Core 6.2, a test file 19.8.
# Estimated (not timed yet): Contracts, Import, Infrastructure, Tools.Api, Admin. A change to a project's
# public surface also recompiles what depends on it, so these are lower bounds for such changes.
BUILD = {
    "src/Lilia.Api/": 13.5, "src/Lilia.Features.": 5.5, "src/Lilia.Api.Contracts/": 6.0,
    "src/Lilia.Engines/": 10.2, "src/Lilia.Core/": 6.2, "src/Lilia.Import/": 9.0,
    "src/Lilia.Infrastructure/": 9.0, "src/Lilia.Tools.Api/": 6.0, "src/Lilia.Admin/": 5.0,
    "tests/": 19.8,
}
NO_CHANGE_BUILD = 4.5
# The test step (fitted on 2 Nov to six timed runs, from one class to all 357): the test host's start
# (5 s), the first database test class's start of the web host and its database copy (16 s, paid once,
# then in parallel), then the selected classes over the parallel workers, never less than the slowest.
HOST_START = 5.0
DB_START = 16.0


def database_classes():
    """Test classes that run against the database (IntegrationTestBase and its kin): they pay DB_START."""
    out = set()
    for top, dirs, files in os.walk(os.path.join(ROOT, "tests", "Lilia.Api.Tests")):
        dirs[:] = [d for d in dirs if d not in ("bin", "obj", "Fixtures")]
        for f in files:
            if not f.endswith(".cs"):
                continue
            text = open(os.path.join(top, f), encoding="utf-8", errors="replace").read()
            ns = re.search(r"^namespace\s+([\w.]+)", text, re.M)
            for m in re.finditer(r"class\s+(\w+)[^{]*?:\s*(\w+)", text):
                if m.group(2) in ("IntegrationTestBase", "FourCallersTestBase") and ns:
                    out.add(f"{ns.group(1)}.{m.group(1)}")
    return out


def selector():
    spec = importlib.util.spec_from_file_location("affected", os.path.join(ROOT, "ops", "affected-tests.py"))
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    return m


def refresh(trx):
    ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    root = ET.parse(trx).getroot()
    cls = {d.get("id"): d.find("t:TestMethod", ns).get("className")
           for d in root.iterfind(".//t:UnitTest", ns)}
    per = {}
    for r in root.iterfind(".//t:UnitTestResult", ns):
        h, m, s = r.get("duration", "0:0:0").split(":")
        secs = int(h) * 3600 + int(m) * 60 + float(s)
        c = cls.get(r.get("testId"))
        if c:
            per[c] = per.get(c, 0) + secs
    with open(DURATIONS, "w") as f:
        json.dump({k: round(v, 2) for k, v in sorted(per.items())}, f, indent=0)
    print(f"{len(per)} classes, {sum(per.values()):.0f} s of tests: {DURATIONS}")


def workers():
    # Parallel workers: the full run's class time over its measured wall time less the host's start.
    with open(DURATIONS) as f:
        total = sum(json.load(f).values())
    return max(1.0, total / (FULL_RUN_WALL - HOST_START - DB_START))


FULL_RUN_WALL = 239.0  # the test step of a full run (5,488 tests), measured with the durations on 2 Nov


def forecast(changed):
    m = selector()
    sel = m.select(changed)
    with open(DURATIONS) as f:
        dur = json.load(f)
    build = max([BUILD[next((p for p in sorted(BUILD, key=len, reverse=True) if c.startswith(p)), "")]
                 for c in changed if any(c.startswith(p) for p in BUILD)] or [NO_CHANGE_BUILD])
    if sel["mode"] == "none":
        # Only the admin console changed: its build and its tests (67 s in CI, 2 Nov).
        return (sel, BUILD["src/Lilia.Admin/"], 62.0) if sel.get("admin") else (sel, 0.0, 0.0)
    if sel["mode"] == "all":
        classes = list(dur)
    elif sel["mode"] == "fast":
        return sel, build, 120.0  # the fast lane: every test outside the database, TeX and nightly ones
    else:
        names = [c.rstrip(".") for c in sel["classes"]]
        classes = [k for k in dur if any(k == n or k.startswith(n + "+") for n in names)]
        # A class not timed yet (new since the durations were taken) counts as a typical one.
        classes += [n for n in names if not any(k == n or k.startswith(n + "+") for k in dur)]
    typical = sorted(dur.values())[len(dur) // 2]
    secs = [dur.get(c, typical) for c in classes]
    start = HOST_START + (DB_START if set(classes) & database_classes() else 0)
    tests = start + max(max(secs, default=0), sum(secs) / workers())
    return sel, build, tests


def main():
    args = sys.argv[1:]
    if args[:1] == ["--refresh"]:
        return refresh(args[1])
    if args[:1] == ["--file"]:
        changed = args[1:]
    else:
        m = selector()
        base = args[0] if args else "origin/main"
        head = args[1] if len(args) > 1 else "HEAD"
        changed = [l for l in m.git("diff", "--name-only", f"{base}...{head}").splitlines() if l]
    sel, build, tests = forecast(changed)
    n = len(sel.get("classes", [])) if sel["mode"] == "some" else {"all": "all", "fast": "fast lane", "none": "no"}[sel["mode"]]
    print(f"{n} test classes ({sel['why']})")
    print(f"forecast: build {build:.0f} s + tests {tests:.0f} s = about {build + tests:.0f} s"
          + ("; the admin tests run beside them (about 67 s)" if sel.get("admin") and sel["mode"] != "none" else ""))


if __name__ == "__main__":
    main()
