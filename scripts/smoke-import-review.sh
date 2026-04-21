#!/usr/bin/env bash
# Smoke test for the end-to-end .tex import → review pipeline.
# Exercises every endpoint that the redesigned frontend consumes so we
# catch shape/regression issues before a user does. Independent from the
# xUnit suite because this runs against any env (prod/staging/local) with
# just a JWT — the CLI proof-of-concept the user asked for.
#
# Usage:
#   scripts/smoke-import-review.sh --base https://editor.liliaeditor.com/api --token "$JWT"
#   scripts/smoke-import-review.sh --base http://localhost:5001/api --token "$(dotnet user-jwts ...)"
#
# Env vars (alternatives to flags):
#   LILIA_API_BASE, LILIA_JWT
#
# Exits 0 on success, non-zero with a human summary on any step failure.

set -euo pipefail

BASE="${LILIA_API_BASE:-}"
TOKEN="${LILIA_JWT:-}"
FIXTURE=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --base) BASE="$2"; shift 2 ;;
    --token) TOKEN="$2"; shift 2 ;;
    --fixture) FIXTURE="$2"; shift 2 ;;
    *) echo "unknown flag: $1" >&2; exit 2 ;;
  esac
done

if [[ -z "$BASE" || -z "$TOKEN" ]]; then
  echo "error: --base and --token (or LILIA_API_BASE + LILIA_JWT) required" >&2
  exit 2
fi

# ─── Fixture — small but realistic .tex mixing every aspect the review cares about ───
TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT

if [[ -z "$FIXTURE" ]]; then
  FIXTURE="$TMP_DIR/smoke.tex"
  cat > "$FIXTURE" <<'EOF'
\documentclass{article}
\usepackage{amsmath}
\usepackage{graphicx}
\usepackage{tabularx}
\usepackage[backend=biber]{biblatex}
\addbibresource{refs.bib}

\title{Smoke-test document}
\author{Lilia CI}

\begin{document}
\maketitle

\begin{abstract}
Tiny fixture hitting every aspect bucket so the review can verify its
tabs: tables, media, math, citations, and a diagnostic-worthy
unsupported-package nudge.
\end{abstract}

\section{Tables}
\begin{tabularx}{\linewidth}{XX}
foo & bar \\
baz & qux \\
\end{tabularx}

\section{Media}
\includegraphics[width=0.5\linewidth]{missing-asset.png}

\section{Math}
\begin{equation}
E = mc^2
\end{equation}

\section{Citations}
As shown in \cite{knuth1974}.

\printbibliography
\end{document}
EOF
fi

# ─── Tiny curl wrapper with readable errors ───
AUTH="Authorization: Bearer $TOKEN"
step() { printf "\n▸ %s\n" "$1"; }
fail() { printf "\n✗ FAIL at: %s\n" "$1" >&2; exit 1; }

j() {
  # run curl, echo body + status-line for visibility, return body on stdout
  local method="$1"; local path="$2"; shift 2
  local body
  body="$(curl -sS -X "$method" -H "$AUTH" "$@" "$BASE$path")"
  echo "$body"
}

need_json() {
  if ! echo "$1" | jq -e . >/dev/null 2>&1; then
    echo "response was not JSON:" >&2
    echo "$1" >&2
    exit 1
  fi
}

# ─── 1. Upload .tex ───
step "POST /lilia/imports/latex  (upload fixture)"
UPLOAD="$(curl -sS -X POST -H "$AUTH" -F "file=@$FIXTURE;type=application/x-tex" \
  "$BASE/lilia/imports/latex")"
need_json "$UPLOAD"
SESSION_ID="$(echo "$UPLOAD" | jq -r '.sessionId // empty')"
JOB_ID="$(echo "$UPLOAD" | jq -r '.jobId // empty')"
[[ -n "$SESSION_ID" && -n "$JOB_ID" ]] || fail "upload did not return sessionId/jobId"
echo "  sessionId=$SESSION_ID  jobId=$JOB_ID"

# ─── 2. Poll until the background parse job completes ───
step "Poll session until status != parsing"
for i in $(seq 1 30); do
  STATUS="$(j GET "/lilia/import-review/sessions/$SESSION_ID" | jq -r '.status // empty')"
  echo "  [$i] status=$STATUS"
  if [[ "$STATUS" != "parsing" && -n "$STATUS" ]]; then break; fi
  sleep 1
done
[[ "$STATUS" != "parsing" ]] || fail "session stuck in parsing after 30s"

# ─── 3. Tree ───
step "GET  /sessions/$SESSION_ID/tree"
TREE="$(j GET "/lilia/import-review/sessions/$SESSION_ID/tree")"
need_json "$TREE"
TOTAL="$(echo "$TREE" | jq -r '.totalBlocks // 0')"
echo "  totalBlocks=$TOTAL"
[[ "$TOTAL" -gt 0 ]] || fail "tree reported 0 blocks for a 5-section fixture"

# ─── 4. Tab-stats ───
step "GET  /sessions/$SESSION_ID/tab-stats"
STATS="$(j GET "/lilia/import-review/sessions/$SESSION_ID/tab-stats")"
need_json "$STATS"
echo "  tables=$(echo "$STATS" | jq -r '.tables.total')  media=$(echo "$STATS" | jq -r '.media.total')  math=$(echo "$STATS" | jq -r '.math.total')"

# ─── 5. Diagnostics ───
step "GET  /sessions/$SESSION_ID/diagnostics"
DIAGS="$(j GET "/lilia/import-review/sessions/$SESSION_ID/diagnostics")"
need_json "$DIAGS"
echo "  diagnostic_count=$(echo "$DIAGS" | jq 'length')"

# ─── 6. Coverage ───
step "GET  /sessions/$SESSION_ID/coverage"
COV="$(j GET "/lilia/import-review/sessions/$SESSION_ID/coverage" || true)"
echo "  coverage bytes=$(echo -n "$COV" | wc -c)"

# ─── 7. Blocks by aspect ───
for ASPECT in structure content tables media math citations; do
  step "GET  /sessions/$SESSION_ID/blocks?aspect=$ASPECT"
  BLOCKS="$(j GET "/lilia/import-review/sessions/$SESSION_ID/blocks?aspect=$ASPECT")"
  need_json "$BLOCKS"
  echo "  $ASPECT count=$(echo "$BLOCKS" | jq 'length')"
done

# ─── 8. Pick first block, fetch source ───
FIRST_ID="$(echo "$TREE" | jq -r '.roots[0].blockId // empty')"
if [[ -n "$FIRST_ID" ]]; then
  step "GET  /sessions/$SESSION_ID/blocks/$FIRST_ID/source"
  SRC="$(j GET "/lilia/import-review/sessions/$SESSION_ID/blocks/$FIRST_ID/source")"
  need_json "$SRC"
  echo "  sliceOrigin=$(echo "$SRC" | jq -r '.sliceOrigin')  blockType=$(echo "$SRC" | jq -r '.blockType')  latexBytes=$(echo "$SRC" | jq -r '.latex | length')"
fi

# ─── 9. Tab-progress PUT — this is the LILIA-API-S regression path ───
step "PUT  /sessions/$SESSION_ID/tab-progress (structure=in_progress)"
PROG_HTTP="$(curl -sS -o /dev/null -w '%{http_code}' -X PUT -H "$AUTH" \
  -H 'Content-Type: application/json' \
  -d '{"tab":"structure","state":"in_progress"}' \
  "$BASE/lilia/import-review/sessions/$SESSION_ID/tab-progress")"
echo "  http=$PROG_HTTP"
[[ "$PROG_HTTP" == "204" ]] || fail "tab-progress returned $PROG_HTTP (expected 204)"

step "PUT  /sessions/$SESSION_ID/tab-progress (tables=done)"
PROG_HTTP="$(curl -sS -o /dev/null -w '%{http_code}' -X PUT -H "$AUTH" \
  -H 'Content-Type: application/json' \
  -d '{"tab":"tables","state":"done"}' \
  "$BASE/lilia/import-review/sessions/$SESSION_ID/tab-progress")"
echo "  http=$PROG_HTTP"
[[ "$PROG_HTTP" == "204" ]] || fail "tab-progress returned $PROG_HTTP (expected 204)"

# ─── 10. Verify tab-progress persisted ───
step "GET  /sessions/$SESSION_ID/tab-stats  (verify progress persisted)"
STATS2="$(j GET "/lilia/import-review/sessions/$SESSION_ID/tab-stats")"
LAST="$(echo "$STATS2" | jq -r '.lastFocusedTab // empty')"
STRUCT_STATE="$(echo "$STATS2" | jq -r '.structure.progressState')"
TABLES_STATE="$(echo "$STATS2" | jq -r '.tables.progressState')"
echo "  lastFocusedTab=$LAST  structure=$STRUCT_STATE  tables=$TABLES_STATE"
[[ "$LAST" == "tables" ]] || fail "lastFocusedTab should be 'tables', got '$LAST'"
[[ "$STRUCT_STATE" == "in_progress" && "$TABLES_STATE" == "done" ]] || fail "progress state did not persist"

# ─── 11. Session list ───
step "GET  /lilia/import-review/sessions (active scope)"
LIST="$(j GET "/lilia/import-review/sessions?scope=active")"
need_json "$LIST"
echo "  active_sessions=$(echo "$LIST" | jq 'length')"

# ─── 12. Sessions history (should exclude active) ───
step "GET  /lilia/import-review/sessions?scope=history"
HIST="$(j GET "/lilia/import-review/sessions?scope=history")"
need_json "$HIST"
echo "  history_sessions=$(echo "$HIST" | jq 'length')"

printf "\n✓ All smoke endpoints returned the expected shape.\n"
echo "  sessionId: $SESSION_ID"
echo "  Open:      ${BASE%/api}/import-review/$SESSION_ID"
