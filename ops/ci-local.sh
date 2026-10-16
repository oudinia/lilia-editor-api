#!/usr/bin/env bash
# CI on this machine (11 Oct 2026: the user prefers it to GitHub's for now).
# The same checks as .github/workflows/tests.yml and ef-migrations-check.yml:
#   tests       every test but Category=RequiresTeX (those run nightly: ops/tex-tests.sh),
#               with a 3-minute hang guard so a stuck test is named, not waited on
#   admin       the admin console's tests (tests/Lilia.Admin.Tests)
#   migrations  the EF model has no change missing a migration, and every
#               migration has its .Designer.cs
#
# Integration tests start their own Postgres (Testcontainers); nothing here
# touches Neon or the API on 5001. Prints one line per check and a verdict;
# exits non-zero on any failure. Logs: $CI_LOG_DIR/api-<branch>-<check>.log.
set -u
cd "$(dirname "$0")/.."
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$HOME/.local/bin:$PATH"
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"  # dotnet-ef finds the runtime by it
LOG_DIR=${CI_LOG_DIR:-/tmp/claude-1001/ci-local}
mkdir -p "$LOG_DIR"
BRANCH=api-$(git rev-parse --abbrev-ref HEAD | tr '/' '-')
START=$SECONDS
fail=0

tests() {
  local log="$LOG_DIR/$BRANCH-tests.log" t=$SECONDS
  timeout 1500 dotnet test tests/Lilia.Api.Tests --filter "Category!=RequiresTeX" \
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
    echo "pass tests $((SECONDS - t))s  ${summary#*- }"
  else
    echo "FAIL tests $((SECONDS - t))s  ${summary:-no summary (build error or hang)}  ($log)"
    grep -E "^\s+Failed [A-Za-z]|error CS|hang" "$log" | head -10 | sed 's/^/     /'
    return 1
  fi
}

migrations() {
  local log="$LOG_DIR/$BRANCH-migrations.log" t=$SECONDS orphans=""
  for cs in src/Lilia.Infrastructure/Migrations/*.cs; do
    case "$cs" in *.Designer.cs|*ModelSnapshot.cs) continue ;; esac
    [ -f "${cs%.cs}.Designer.cs" ] || orphans="$orphans ${cs##*/}"
  done
  if [ -n "$orphans" ]; then
    echo "FAIL migrations: no .Designer.cs for$orphans"; return 1
  fi
  if dotnet ef migrations has-pending-model-changes --project src/Lilia.Infrastructure \
       --startup-project src/Lilia.Api >"$log" 2>&1; then
    echo "pass migrations $((SECONDS - t))s"
  else
    echo "FAIL migrations $((SECONDS - t))s: model changes without a migration  ($log)"; return 1
  fi
}

# The admin console (src/Lilia.Admin, 16 Oct 2026): its own test project, its own Postgres.
admin() {
  local log="$LOG_DIR/$BRANCH-admin.log" t=$SECONDS summary
  timeout 600 dotnet test tests/Lilia.Admin.Tests --blame-hang-timeout 3m --logger "console;verbosity=minimal" >"$log" 2>&1
  summary=$(grep -E "^(Passed|Failed)! " "$log" | tail -1)
  if ! grep -qE "Test Run Aborted|test run was aborted" "$log" && [[ "$summary" == Passed!*"Failed:     0,"* ]]; then
    echo "pass admin $((SECONDS - t))s  ${summary#*- }"
  else
    echo "FAIL admin $((SECONDS - t))s  ${summary:-no summary (build error or hang)}  ($log)"
    grep -E "^\s+Failed [A-Za-z]|error CS|hang|aborted" "$log" | head -10 | sed 's/^/     /'
    return 1
  fi
}

tests || fail=1
admin || fail=1
migrations || fail=1
if [ $fail -ne 0 ]; then echo "LOCAL CI: FAIL ($((SECONDS - START))s)"; exit 1; fi
echo "LOCAL CI: PASS ($((SECONDS - START))s)"
