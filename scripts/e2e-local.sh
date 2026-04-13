#!/usr/bin/env bash
# Run the Lilia.Api.E2E suite against a locally started API + local Postgres (lilia_e2e).
# No Kinde dependency. Auth is handled by DevelopmentAuthMiddleware + DevJwt tokens.
#
# Usage:
#   scripts/e2e-local.sh              # run full suite
#   scripts/e2e-local.sh --filter ... # pass extra args to dotnet test
#
# Assumptions:
#   - Local Postgres running on localhost:5432 with database `lilia_e2e` (schema + templates seeded)
#   - No other process bound to :5001
#   - `~/.dotnet/dotnet` is the dotnet CLI path

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

DOTNET="${DOTNET:-$HOME/.dotnet/dotnet}"
API_PROJECT="src/Lilia.Api"
E2E_PROJECT="tests/Lilia.Api.E2E"
API_URL="http://localhost:5001"
API_LOG="$REPO_ROOT/.e2e-api.log"
API_PID_FILE="$REPO_ROOT/.e2e-api.pid"

export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS="$API_URL"
export ConnectionStrings__LiliaCore="Host=localhost;Database=lilia_e2e;Username=postgres;Password=postgres"
unset Auth__Authority  # ensure lenient JWT validation (accepts unsigned DevJwt tokens)
export Auth__DevelopmentAuth__Disabled=true  # tests mint their own JWTs; anonymous tests must see 401, not fake dev user

export E2E__ApiBaseUrl="$API_URL"
export E2E__AuthMode="DevJwt"

cleanup() {
  if [[ -f "$API_PID_FILE" ]]; then
    local pid
    pid="$(cat "$API_PID_FILE")"
    if kill -0 "$pid" 2>/dev/null; then
      echo "[e2e-local] Stopping API (pid=$pid)"
      kill "$pid" 2>/dev/null || true
      wait "$pid" 2>/dev/null || true
    fi
    rm -f "$API_PID_FILE"
  fi
}
trap cleanup EXIT INT TERM

echo "[e2e-local] Building API + E2E project"
"$DOTNET" build "$API_PROJECT" --nologo --verbosity quiet
"$DOTNET" build "$E2E_PROJECT" --nologo --verbosity quiet

echo "[e2e-local] Starting API (log: $API_LOG)"
: > "$API_LOG"
"$DOTNET" run --project "$API_PROJECT" --no-build --no-launch-profile \
  > "$API_LOG" 2>&1 &
echo $! > "$API_PID_FILE"

echo "[e2e-local] Waiting for API at $API_URL"
for i in {1..60}; do
  if curl -fsS "$API_URL/" -o /dev/null 2>&1 || curl -fsS -o /dev/null -w "%{http_code}" "$API_URL/api/documents" 2>&1 | grep -qE "^(200|401|403)$"; then
    echo "[e2e-local] API is up"
    break
  fi
  if ! kill -0 "$(cat "$API_PID_FILE")" 2>/dev/null; then
    echo "[e2e-local] API process died. Last 40 lines of log:"
    tail -n 40 "$API_LOG"
    exit 1
  fi
  sleep 1
  if [[ $i -eq 60 ]]; then
    echo "[e2e-local] Timed out waiting for API. Last 40 lines of log:"
    tail -n 40 "$API_LOG"
    exit 1
  fi
done

echo "[e2e-local] Running E2E tests"
"$DOTNET" test "$E2E_PROJECT" --no-build "$@"
