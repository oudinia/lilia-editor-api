#!/usr/bin/env bash
# coverage-diag — CLI for the LaTeX coverage diagnostic endpoints.
#
# Everything routes through /api/public/latex-coverage/diag on the API.
# The routes are [AllowAnonymous] for the duration of coverage debugging;
# no JWT needed. See lilia-docs/technical/latex-coverage-diag-cli.md.
#
# Subcommands:
#   state                     Show catalog counts, coverage distribution, last 5 tex sessions
#   run <file.tex>            Run the full import pipeline; deletes the throwaway session by default
#   run --keep <file.tex>     Same, but preserve the session for inspection
#   self-test <file.tex> <session-id>
#                             Scan + lookup + force RecordUsageAsync against an existing session
#   cleanup                   Remove every session whose title starts with '[diag]'
#
# Environment:
#   LILIA_API_BASE            Default: https://editor.liliaeditor.com/api
#
# Requires: curl, jq, python3 (for robust JSON body packing from file content).

set -uo pipefail

BASE="${LILIA_API_BASE:-https://editor.liliaeditor.com/api}"
DIAG="$BASE/public/latex-coverage/diag"

die() { printf 'error: %s\n' "$*" >&2; exit 2; }
need() { command -v "$1" >/dev/null || die "missing dependency: $1"; }
need curl; need jq; need python3

usage() {
  grep -E '^# ' "$0" | sed 's/^# \{0,1\}//'
  exit 1
}

pack_body() {
  # Read a file's content into a JSON body. Optional extra k/v pairs go in $2.
  # Usage: pack_body <file> '{"sessionId":"..."}' -> JSON with rawSource + merged
  local file="$1"; shift
  local extra="${1:-{\}}"
  python3 - "$file" "$extra" <<'PY'
import json, sys
raw = open(sys.argv[1]).read()
extra = json.loads(sys.argv[2])
extra["rawSource"] = raw
print(json.dumps(extra))
PY
}

cmd_state() {
  curl -sS --max-time 15 "$DIAG/state" | jq .
}

cmd_run() {
  local keep=0
  if [[ "${1:-}" == "--keep" ]]; then keep=1; shift; fi
  local file="${1:-}"
  [[ -f "$file" ]] || die "usage: run [--keep] <file.tex>"

  local body
  body="$(pack_body "$file" '{}')"
  local qs=""
  [[ $keep -eq 1 ]] && qs="?keep=true"

  curl -sS --max-time 60 \
    -H 'Content-Type: application/json' \
    -X POST --data "$body" \
    "$DIAG/run-import$qs" | jq .
}

cmd_self_test() {
  local file="${1:-}"; local sid="${2:-}"
  [[ -f "$file" && -n "$sid" ]] || die "usage: self-test <file.tex> <session-id>"

  local body
  body="$(pack_body "$file" "{\"sessionId\":\"$sid\"}")"
  curl -sS --max-time 30 \
    -H 'Content-Type: application/json' \
    -X POST --data "$body" \
    "$DIAG/self-test" | jq .
}

cmd_cleanup() {
  curl -sS --max-time 30 \
    -H 'Content-Type: application/json' \
    -X POST --data '{}' \
    "$DIAG/cleanup" | jq .
}

main() {
  [[ $# -ge 1 ]] || usage
  local sub="$1"; shift
  case "$sub" in
    state)     cmd_state "$@" ;;
    run)       cmd_run "$@" ;;
    self-test) cmd_self_test "$@" ;;
    cleanup)   cmd_cleanup "$@" ;;
    -h|--help|help) usage ;;
    *) die "unknown subcommand: $sub (try --help)" ;;
  esac
}

main "$@"
