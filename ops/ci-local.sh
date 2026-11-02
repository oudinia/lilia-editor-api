#!/usr/bin/env bash
# CI on this machine (11 Oct 2026: the user prefers it to GitHub's for now).
# The same checks as .github/workflows/tests.yml and ef-migrations-check.yml:
#   tests       every test but Category=RequiresTeX and Category=Nightly (both run nightly:
#               ops/tex-tests.sh; Nightly holds measures and full audits, 1 Nov 2026),
#               with a 3-minute hang guard so a stuck test is named, not waited on
#   admin       the admin console's tests (tests/Lilia.Admin.Tests)
#   migrations  the EF model has no change missing a migration, and every
#               migration has its .Designer.cs
#
# Built once, then the three checks run side by side (2 Nov 2026: 449 s, of which two more builds of
# the same code and the admin and migrations checks waiting for the tests).
#
# Only what the branch can affect (2 Nov 2026, the user: "run only tests for which code has been
# modified"): ops/affected-tests.py picks the API test classes the changed code reaches; the admin's
# tests run when the admin console changed, the migrations check when the database model did. Every
# test runs each night (ops/tex-tests.sh on the server), so what a selection misses is found by the
# morning. CI_ALL=1 runs everything, as before.
#
# Integration tests start their own Postgres (Testcontainers); nothing here
# touches Neon or the API on 5001. Prints one line per check and a verdict;
# exits non-zero on any failure. Logs: $CI_LOG_DIR/api-<branch>-<check>.log.
set -u
cd "$(dirname "$0")/.."
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$HOME/.local/bin:$PATH"
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"  # dotnet-ef finds the runtime by it
# The always-on test Postgres (lilia-test-pg), when this machine has one: the database tests use it
# instead of starting a container of their own (TestDatabaseFixture).
[ -f "$HOME/.config/lilia/test-pg.env" ] && { set -a; . "$HOME/.config/lilia/test-pg.env"; set +a; }
LOG_DIR=${CI_LOG_DIR:-/tmp/claude-1001/ci-local}
mkdir -p "$LOG_DIR"
BRANCH=api-$(git rev-parse --abbrev-ref HEAD | tr '/' '-')
START=$SECONDS
fail=0

build() {
  local log="$LOG_DIR/$BRANCH-build.log" t=$SECONDS
  # The API's tests build the API and everything it uses; the admin's add the admin console.
  if timeout 900 dotnet build -v q tests/Lilia.Api.Tests >"$log" 2>&1 \
     && timeout 600 dotnet build -v q tests/Lilia.Admin.Tests >>"$log" 2>&1; then
    echo "pass build $((SECONDS - t))s"
  else
    echo "FAIL build $((SECONDS - t))s  ($log)"
    grep -E "error [A-Z]+[0-9]+" "$log" | sort -u | head -10 | sed 's/^/     /'
    return 1
  fi
}

tests() {
  local log="$LOG_DIR/$BRANCH-tests.log" t=$SECONDS flt="Category!=RequiresTeX&Category!=Nightly"
  case "$SEL_MODE" in
    none) echo "skip tests: $SEL_WHY"; rm -f "$log"; return 0 ;;
    fast) flt="$flt&Category!=Integration" ;;
    some) flt="$SEL_FILTER&$flt" ;;
  esac
  timeout 1500 dotnet test tests/Lilia.Api.Tests --no-build --filter "$flt" \
    --blame-hang-timeout 3m --logger "console;verbosity=minimal" >"$log" 2>&1
  # The summary line is the verdict (a pipe's exit code has lied before).
  local summary
  summary=$(grep -E "^(Passed|Failed)! " "$log" | tail -1)
  # An aborted run (a crashed or hung test host) still prints "Passed!" for the
  # tests that ran before it died (11 Oct: 4,371 of 4,560, Failed: 0). Not a pass.
  if grep -qE "Test Run Aborted|test run was aborted" "$log"; then
    echo "FAIL tests $((SECONDS - t))s  ABORTED after: ${summary#*- }  ($log)"
    grep -E "aborted|Blame|hang" "$log" | head -5 | sed 's/^/     /'
    return 1
  fi
  if [[ "$summary" == Passed!*"Failed:     0,"* ]]; then
    echo "pass tests $((SECONDS - t))s  ${summary#*- }  [$SEL_MODE: $SEL_WHY]"
  else
    echo "FAIL tests $((SECONDS - t))s  ${summary:-no summary (build error or hang)}  ($log)"
    grep -E "^\s+Failed [A-Za-z]|error CS|hang" "$log" | head -10 | sed 's/^/     /'
    return 1
  fi
}

migrations() {
  local log="$LOG_DIR/$BRANCH-migrations.log" t=$SECONDS orphans=""
  [ "$SEL_MIGRATIONS" = 1 ] || { echo "skip migrations: the database model did not change"; return 0; }
  for cs in src/Lilia.Infrastructure/Migrations/*.cs; do
    case "$cs" in *.Designer.cs|*ModelSnapshot.cs) continue ;; esac
    [ -f "${cs%.cs}.Designer.cs" ] || orphans="$orphans ${cs##*/}"
  done
  if [ -n "$orphans" ]; then
    echo "FAIL migrations: no .Designer.cs for$orphans"; return 1
  fi
  if dotnet ef migrations has-pending-model-changes --project src/Lilia.Infrastructure \
       --startup-project src/Lilia.Api --no-build >"$log" 2>&1; then
    echo "pass migrations $((SECONDS - t))s"
  else
    echo "FAIL migrations $((SECONDS - t))s: model changes without a migration  ($log)"; return 1
  fi
}

# The admin console (src/Lilia.Admin, 16 Oct 2026): its own test project, its own Postgres.
admin() {
  local log="$LOG_DIR/$BRANCH-admin.log" t=$SECONDS summary
  [ "$SEL_ADMIN" = 1 ] || { echo "skip admin: the admin console did not change (its tests run nightly)"; return 0; }
  timeout 600 dotnet test tests/Lilia.Admin.Tests --no-build --blame-hang-timeout 3m --logger "console;verbosity=minimal" >"$log" 2>&1
  summary=$(grep -E "^(Passed|Failed)! " "$log" | tail -1)
  if ! grep -qE "Test Run Aborted|test run was aborted" "$log" && [[ "$summary" == Passed!*"Failed:     0,"* ]]; then
    echo "pass admin $((SECONDS - t))s  ${summary#*- }"
  else
    echo "FAIL admin $((SECONDS - t))s  ${summary:-no summary (build error or hang)}  ($log)"
    grep -E "^\s+Failed [A-Za-z]|error CS|hang|aborted" "$log" | head -10 | sed 's/^/     /'
    return 1
  fi
}

# What to run (ops/affected-tests.py; CI_ALL=1: everything).
git fetch -q origin main 2>/dev/null || true
SEL=$(python3 ops/affected-tests.py origin/main 2>/dev/null || echo '{"mode":"all","why":["the selector did not run"],"admin":true,"migrations":true,"filter":""}')
[ "${CI_ALL:-0}" = 1 ] && SEL='{"mode":"all","why":["CI_ALL=1"],"admin":true,"migrations":true,"filter":""}'
SEL_MODE=$(python3 -c 'import json,sys; print(json.loads(sys.argv[1])["mode"])' "$SEL")
SEL_WHY=$(python3 -c 'import json,sys; print("; ".join(json.loads(sys.argv[1])["why"]))' "$SEL")
SEL_FILTER=$(python3 -c 'import json,sys; print(json.loads(sys.argv[1])["filter"])' "$SEL")
SEL_ADMIN=$(python3 -c 'import json,sys; print(int(json.loads(sys.argv[1])["admin"]))' "$SEL")
SEL_MIGRATIONS=$(python3 -c 'import json,sys; print(int(json.loads(sys.argv[1])["migrations"]))' "$SEL")
echo "selected: $SEL_MODE ($SEL_WHY); admin $SEL_ADMIN; migrations $SEL_MIGRATIONS"

if ! build; then echo "LOCAL CI: FAIL ($((SECONDS - START))s)"; exit 1; fi
# Side by side, each into its own result file; printed in a fixed order once all three are done.
RES=$(mktemp -d)
{ tests >"$RES/1" 2>&1; echo $? >"$RES/1.code"; } &
{ admin >"$RES/2" 2>&1; echo $? >"$RES/2.code"; } &
{ migrations >"$RES/3" 2>&1; echo $? >"$RES/3.code"; } &
wait
for i in 1 2 3; do cat "$RES/$i"; [ "$(cat "$RES/$i.code")" = 0 ] || fail=1; done
rm -rf "$RES"
# Ryuk, Testcontainers' cleaner, is off (~/.testcontainers.properties): it listened on every interface
# and deletes any container it is asked to (19 Oct). A test database a crashed run left behind goes here.
docker ps -aq --filter label=org.testcontainers=true 2>/dev/null | xargs -r docker rm -f >/dev/null 2>&1 || true
if [ $fail -ne 0 ]; then echo "LOCAL CI: FAIL ($((SECONDS - START))s)"; exit 1; fi
echo "LOCAL CI: PASS ($((SECONDS - START))s)"
