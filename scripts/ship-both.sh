#!/usr/bin/env bash
# Push API + editor repos to main in a way that DigitalOcean handles
# cleanly. DO's App Platform supersedes in-flight deploys when two
# pushes arrive close together — it CANCELs the older and rebuilds
# everything from main on the newer. That's actually fine (the final
# ACTIVE deploy pulls the latest state of both repos), but seeing
# "CANCELED" in doctl makes it look broken.
#
# This script:
#   1. Verifies both repos have commits ahead of origin
#   2. Pushes API first, watches its deploy, waits for ACTIVE
#   3. Pushes editor, watches that deploy, waits for ACTIVE
#
# Net: two clean ACTIVE deploys, no CANCELED rows, and if the API
# deploy fails the editor push is aborted.
#
# Usage:
#   scripts/ship-both.sh

set -euo pipefail

API_DIR="${API_DIR:-/home/oussama/projects/lilia-editor-api}"
EDITOR_DIR="${EDITOR_DIR:-/home/oussama/projects/lilia-web-editor}"
APP_ID="${APP_ID:-5ace837e-39d3-4b70-a201-0020fcdb7b73}"

# ── Preflight ──────────────────────────────────────────────────────────
for d in "$API_DIR" "$EDITOR_DIR"; do
  [[ -d "$d/.git" ]] || { echo "error: $d is not a git repo" >&2; exit 2; }
done
command -v doctl >/dev/null || { echo "error: doctl required" >&2; exit 2; }

ahead() {
  git -C "$1" rev-list --count @{upstream}..HEAD 2>/dev/null || echo 0
}

API_AHEAD="$(ahead "$API_DIR")"
EDITOR_AHEAD="$(ahead "$EDITOR_DIR")"
echo "API    ahead by $API_AHEAD"
echo "editor ahead by $EDITOR_AHEAD"
[[ "$API_AHEAD" -gt 0 || "$EDITOR_AHEAD" -gt 0 ]] || { echo "nothing to push"; exit 0; }

wait_active() {
  local deploy_id="$1"
  echo "  waiting on deploy $deploy_id"
  for i in $(seq 1 25); do
    local phase
    phase="$(doctl apps get-deployment "$APP_ID" "$deploy_id" --format Phase --no-header 2>&1 | head -1)"
    printf "    [%2d/25] phase=%s\n" "$i" "$phase"
    case "$phase" in
      ACTIVE)   return 0 ;;
      ERROR|CANCELED) echo "    deploy ended as $phase" >&2; return 1 ;;
    esac
    sleep 30
  done
  echo "    timed out" >&2; return 1
}

latest_deploy_for() {
  # Grab the newest deployment ID in any non-terminal phase (BUILDING /
  # DEPLOYING / PENDING). If nothing is in flight, return empty.
  doctl apps list-deployments "$APP_ID" --format ID,Phase --no-header 2>/dev/null \
    | awk '$2=="BUILDING" || $2=="DEPLOYING" || $2=="PENDING" { print $1; exit }'
}

# ── 1. API ─────────────────────────────────────────────────────────────
if [[ "$API_AHEAD" -gt 0 ]]; then
  echo
  echo "▸ pushing API ($API_AHEAD commit(s))"
  git -C "$API_DIR" push origin main
  sleep 10
  API_DEPLOY="$(latest_deploy_for)"
  [[ -n "$API_DEPLOY" ]] || { echo "no API deploy spawned"; exit 1; }
  wait_active "$API_DEPLOY"
fi

# ── 2. Editor ──────────────────────────────────────────────────────────
if [[ "$EDITOR_AHEAD" -gt 0 ]]; then
  echo
  echo "▸ pushing editor ($EDITOR_AHEAD commit(s))"
  git -C "$EDITOR_DIR" push origin main
  sleep 10
  EDITOR_DEPLOY="$(latest_deploy_for)"
  [[ -n "$EDITOR_DEPLOY" ]] || { echo "no editor deploy spawned"; exit 1; }
  wait_active "$EDITOR_DEPLOY"
fi

echo
echo "✓ both repos deployed"
