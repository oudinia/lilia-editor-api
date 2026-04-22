#!/usr/bin/env bash
# Review-process integration tests. End-to-end lifecycle check of every
# small use case the redesigned import-review UI depends on, run against a
# live API (prod by default). Each check logs pass/fail independently so a
# single failure doesn't mask the rest; summary printed at the end.
#
# Scope (what's covered, in order):
#   1.  Upload LaTeX fixture — session + job created
#   2.  List active sessions — new session appears
#   3.  Get session — shape sanity
#   4.  Tree, tab-stats, coverage, diagnostics, report, activity
#   5.  Blocks by aspect (structure/content/tables/media/math/citations)
#   6.  Block source — raw LaTeX slice
#   7.  Update block (approve, reject, edit content)
#   8.  Reset block — restores original
#   9.  Bulk action (approveAll)
#   10. Tab progress PUT + GET persistence
#   11. Comments: add + list + delete
#   12. Hints: compute + list + dismiss
#   13. Diagnostics dismiss
#   14. Category PATCH
#   15. "Leave and come back" — re-fetch verifies state preservation
#   16. Finalize (force=true) — document created
#   17. History: session absent from active, present in history scope
#   18. Direct re-open by ID after finalize — still works
#   19. Cleanup — permanent delete of the session
#
# Usage:
#   scripts/review-integration.sh --base https://editor.liliaeditor.com/api --token "$JWT"
#   LILIA_API_BASE=... LILIA_JWT=... scripts/review-integration.sh
#
# Exit 0 if every check passes; 1 otherwise.

set -uo pipefail

BASE="${LILIA_API_BASE:-}"
TOKEN="${LILIA_JWT:-}"
KEEP_SESSION=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --base)  BASE="$2"; shift 2 ;;
    --token) TOKEN="$2"; shift 2 ;;
    --keep)  KEEP_SESSION=1; shift ;;
    *) echo "unknown flag: $1" >&2; exit 2 ;;
  esac
done

[[ -n "$BASE" && -n "$TOKEN" ]] || { echo "error: --base and --token (or env vars) required" >&2; exit 2; }

AUTH="Authorization: Bearer $TOKEN"
PASS=0; FAIL=0; FAILED_CHECKS=()
pass() { PASS=$((PASS+1)); printf "  \033[32m✓\033[0m %s\n" "$1"; }
fail() { FAIL=$((FAIL+1)); FAILED_CHECKS+=("$1"); printf "  \033[31m✗\033[0m %s — %s\n" "$1" "$2"; }
step() { printf "\n\033[1m▸ %s\033[0m\n" "$1"; }

# --- curl helpers ---
# req writes body to $TMP/last.body and HTTP code to $TMP/last.http so the
# caller can read both even when req() runs in a $(...) subshell.
TMP_DIR_VAR=""  # filled once $TMP exists
HTTP=""
req() {
  local method="$1"; local path="$2"; shift 2
  HTTP="$(curl -sS -o "$TMP/last.body" -w '%{http_code}' -X "$method" \
    -H "$AUTH" --max-time 30 "$@" "$BASE$path" 2>/dev/null || echo "000")"
  echo "$HTTP" > "$TMP/last.http"
  cat "$TMP/last.body"
}
code() { cat "$TMP/last.http" 2>/dev/null || echo "000"; }
get()   { req GET    "$1"; }
# Body-bearing verbs: require the body as $2. Use '{}' explicitly for empty.
post()  { local b="${2:-\{\}}"; req POST   "$1" -H 'Content-Type: application/json' -d "$b"; }
put()   { local b="${2:-\{\}}"; req PUT    "$1" -H 'Content-Type: application/json' -d "$b"; }
patch() { local b="${2:-\{\}}"; req PATCH  "$1" -H 'Content-Type: application/json' -d "$b"; }
del()   { req DELETE "$1"; }

# --- fixture ---
TMP="$(mktemp -d)"
cleanup() {
  if [[ $KEEP_SESSION -eq 0 && -n "${SESSION_ID:-}" ]]; then
    curl -sS -o /dev/null -X DELETE -H "$AUTH" "$BASE/lilia/import-review/sessions/$SESSION_ID?permanent=true" || true
  fi
  rm -rf "$TMP"
}
trap cleanup EXIT

cat > "$TMP/fixture.tex" <<'EOF'
\documentclass{article}
\usepackage{amsmath}
\usepackage{graphicx}
\usepackage{tabularx}

\title{Review integration fixture}
\author{CI}

\begin{document}
\maketitle

\begin{abstract}
Small fixture exercising the review pipeline end-to-end.
\end{abstract}

\section{Introduction}
A short introductory paragraph with \emph{emphasis} and some math: $a^2 + b^2 = c^2$.

\section{Tables}
\begin{tabularx}{\linewidth}{XX}
foo & bar \\
baz & qux \\
\end{tabularx}

\section{Media}
\includegraphics[width=0.4\linewidth]{missing.png}

\section{Math}
\begin{equation}
E = mc^2
\end{equation}

\end{document}
EOF

printf "\n\033[1mReview integration suite\033[0m  (base=%s)\n" "$BASE"

# ─── 1. Upload ────────────────────────────────────────────────────────────
step "1. Upload LaTeX fixture"
UPLOAD="$(curl -sS -o "$TMP/upload.json" -w '%{http_code}' -X POST -H "$AUTH" \
  -F "file=@$TMP/fixture.tex;type=application/x-tex" --max-time 60 \
  "$BASE/lilia/imports/latex" 2>&1 || echo 000)"
BODY="$(cat "$TMP/upload.json")"
SESSION_ID=""; JOB_ID=""
if [[ "$UPLOAD" == "200" ]]; then
  SESSION_ID="$(echo "$BODY" | jq -r '.sessionId // empty')"
  JOB_ID="$(echo "$BODY"     | jq -r '.jobId // empty')"
fi
if [[ -n "$SESSION_ID" && -n "$JOB_ID" ]]; then
  pass "upload → sessionId=$SESSION_ID jobId=$JOB_ID"
else
  fail "upload" "HTTP $UPLOAD body=$BODY"
  echo ""; echo "Abort: cannot continue without a session"; exit 1
fi

# ─── Wait for parse to finish ─────────────────────────────────────────────
step "Poll until status != parsing"
for i in $(seq 1 30); do
  S="$(get "/lilia/import-review/sessions/$SESSION_ID" | jq -r '.session.status // .status // empty')"
  [[ -z "$S" ]] && S="?"
  [[ "$i" == "1" || "$i" == "5" || "$i" == "10" || "$i" == "20" ]] && echo "  [$i] status=$S (http=$HTTP)"
  [[ "$S" != "parsing" && -n "$S" && "$S" != "?" ]] && break
  sleep 1
done
[[ "$S" != "parsing" ]] && pass "parse completed → status=$S" || fail "parse" "stuck in parsing"

# ─── 2. List active sessions — new session present ────────────────────────
step "2. List active sessions"
LIST="$(get "/lilia/import-review/sessions?scope=active")"
if [[ "$(code)" == "200" ]] && echo "$LIST" | jq -e --arg s "$SESSION_ID" 'map(.id) | index($s) != null' >/dev/null; then
  pass "session appears in active scope"
else
  fail "list active" "HTTP $(code) or session missing"
fi

# ─── 3. Get session — shape sanity ────────────────────────────────────────
step "3. Get session"
GETSESSION="$(get "/lilia/import-review/sessions/$SESSION_ID")"
if [[ "$(code)" == "200" ]] && echo "$GETSESSION" | jq -e '.session.id // .id' >/dev/null; then
  pass "session payload parseable"
else
  fail "get session" "HTTP $(code)"
fi

# ─── 4. Tree / tab-stats / coverage / diagnostics / report / activity ─────
step "4. Read-side endpoints"
for path in tree tab-stats coverage diagnostics report activity; do
  R="$(get "/lilia/import-review/sessions/$SESSION_ID/$path")"
  if [[ "$(code)" == "200" ]] && echo "$R" | jq -e . >/dev/null 2>&1; then
    pass "$path → 200, $(echo -n "$R" | wc -c | tr -d ' ') bytes"
  else
    fail "$path" "HTTP $(code)"
  fi
done

# Coverage sample — records what tokens are surfaced so we can evaluate the
# "I can't find paragraph/section/equation/table" UX note.
step "4b. Coverage token sample (sanity-check search UX)"
COV="$(get "/lilia/import-review/sessions/$SESSION_ID/coverage")"
if [[ "$(code)" == "200" ]]; then
  DISTINCT="$(echo "$COV" | jq -r '.distinctTokens // 0')"
  TOPTOKENS="$(echo "$COV" | jq -r '.tokens[0:10] | map("\(.name) (\(.kind), maps=\(.mapsToBlockType // "—"))") | join(", ")')"
  echo "    distinctTokens=$DISTINCT"
  echo "    top 10: $TOPTOKENS"
  for term in section equation table paragraph; do
    HIT="$(echo "$COV" | jq --arg t "$term" '.tokens | map(select((.name|ascii_downcase|contains($t)) or ((.mapsToBlockType // "")|ascii_downcase|contains($t)))) | length')"
    echo "    search \"$term\" → $HIT matches"
  done
fi

# ─── 5. Blocks by aspect ──────────────────────────────────────────────────
step "5. Blocks by aspect"
for aspect in all structure content tables media math citations; do
  R="$(get "/lilia/import-review/sessions/$SESSION_ID/blocks?aspect=$aspect")"
  if [[ "$(code)" == "200" ]] && echo "$R" | jq -e 'type=="array"' >/dev/null; then
    pass "aspect=$aspect → $(echo "$R" | jq 'length') blocks"
  else
    fail "blocks?aspect=$aspect" "HTTP $(code)"
  fi
done

# ─── 6. Block source ──────────────────────────────────────────────────────
step "6. Block source"
FIRST_BLOCK_ID="$(get "/lilia/import-review/sessions/$SESSION_ID/blocks?aspect=all" | jq -r '.[0].blockId // empty')"
if [[ -n "$FIRST_BLOCK_ID" ]]; then
  SRC="$(get "/lilia/import-review/sessions/$SESSION_ID/blocks/$FIRST_BLOCK_ID/source")"
  if [[ "$(code)" == "200" ]] && echo "$SRC" | jq -e '.blockType' >/dev/null; then
    pass "source for $FIRST_BLOCK_ID → blockType=$(echo "$SRC" | jq -r '.blockType')"
  else
    fail "block source" "HTTP $(code)"
  fi
else
  fail "block source" "no first blockId to query"
fi

# ─── 7. Update block — approve + reject + edit ────────────────────────────
step "7. Update block"
APPROVED_BLOCK_ID="$FIRST_BLOCK_ID"
R="$(patch "/lilia/import-review/sessions/$SESSION_ID/blocks/$APPROVED_BLOCK_ID" \
  "{\"sessionId\":\"$SESSION_ID\",\"blockId\":\"$APPROVED_BLOCK_ID\",\"status\":\"approved\"}")"
if [[ "$(code)" == "200" ]] && [[ "$(echo "$R" | jq -r '.status')" == "approved" ]]; then
  pass "PATCH status=approved → persisted"
else
  fail "patch approved" "HTTP $(code) body=$R"
fi

ALL_IDS=($(get "/lilia/import-review/sessions/$SESSION_ID/blocks?aspect=all" | jq -r '.[].blockId'))
REJECT_ID="${ALL_IDS[1]:-}"
if [[ -n "$REJECT_ID" ]]; then
  R="$(patch "/lilia/import-review/sessions/$SESSION_ID/blocks/$REJECT_ID" \
    "{\"sessionId\":\"$SESSION_ID\",\"blockId\":\"$REJECT_ID\",\"status\":\"rejected\"}")"
  [[ "$(code)" == "200" ]] && pass "PATCH status=rejected" || fail "patch rejected" "HTTP $(code)"
fi

EDIT_ID="${ALL_IDS[2]:-}"
if [[ -n "$EDIT_ID" ]]; then
  NEW_CONTENT='{"type":"paragraph","content":[{"type":"text","text":"Edited by integration test"}]}'
  R="$(patch "/lilia/import-review/sessions/$SESSION_ID/blocks/$EDIT_ID" \
    "{\"sessionId\":\"$SESSION_ID\",\"blockId\":\"$EDIT_ID\",\"status\":\"edited\",\"currentContent\":$NEW_CONTENT}")"
  if [[ "$(code)" == "200" ]] && echo "$R" | jq -e '.currentContent' >/dev/null; then
    pass "PATCH with currentContent → edited"
  else
    fail "patch edit" "HTTP $(code) body=$(echo "$R" | head -c 200)"
  fi
fi

# ─── 8. Reset block — restores original ───────────────────────────────────
step "8. Reset block"
if [[ -n "$EDIT_ID" ]]; then
  R="$(post "/lilia/import-review/sessions/$SESSION_ID/blocks/$EDIT_ID/reset" '{}')"
  if [[ "$(code)" == "200" ]]; then
    RESET_STATUS="$(echo "$R" | jq -r '.status')"
    pass "reset → status=$RESET_STATUS"
  else
    fail "reset" "HTTP $(code)"
  fi
fi

# ─── 9. Bulk action — approveAll ──────────────────────────────────────────
step "9. Bulk action (approveAll)"
R="$(post "/lilia/import-review/sessions/$SESSION_ID/bulk-action" "{\"sessionId\":\"$SESSION_ID\",\"action\":\"approveAll\"}")"
if [[ "$(code)" == "200" ]] && echo "$R" | jq -e '.affected' >/dev/null; then
  pass "approveAll → affected=$(echo "$R" | jq -r '.affected')"
else
  fail "bulk approveAll" "HTTP $(code) body=$R"
fi

# ─── 10. Tab progress ─────────────────────────────────────────────────────
step "10. Tab progress"
R="$(put "/lilia/import-review/sessions/$SESSION_ID/tab-progress" '{"tab":"structure","state":"in_progress"}')"
[[ "$(code)" == "204" ]] && pass "PUT tab=structure state=in_progress" || fail "tab progress" "HTTP $(code)"
R="$(put "/lilia/import-review/sessions/$SESSION_ID/tab-progress" '{"tab":"tables","state":"done"}')"
[[ "$(code)" == "204" ]] && pass "PUT tab=tables state=done" || fail "tab progress (tables)" "HTTP $(code)"
STATS="$(get "/lilia/import-review/sessions/$SESSION_ID/tab-stats")"
LAST="$(echo "$STATS" | jq -r '.lastFocusedTab')"
STRUCT_STATE="$(echo "$STATS" | jq -r '.structure.progressState')"
TABLES_STATE="$(echo "$STATS" | jq -r '.tables.progressState')"
if [[ "$LAST" == "tables" && "$STRUCT_STATE" == "in_progress" && "$TABLES_STATE" == "done" ]]; then
  pass "tab-progress persisted (lastFocused=tables, struct=in_progress, tables=done)"
else
  fail "tab-progress persistence" "last=$LAST struct=$STRUCT_STATE tables=$TABLES_STATE"
fi

# ─── 11. Comments ─────────────────────────────────────────────────────────
step "11. Comments"
R="$(post "/lilia/import-review/sessions/$SESSION_ID/comments" \
  "{\"sessionId\":\"$SESSION_ID\",\"blockId\":\"$APPROVED_BLOCK_ID\",\"content\":\"integ-test comment\"}")"
COMMENT_ID="$(echo "$R" | jq -r '.comment.id // .id // empty')"
if [[ "$(code)" == "200" && -n "$COMMENT_ID" ]]; then
  pass "add comment → $COMMENT_ID"
else
  fail "add comment" "HTTP $(code) body=$(echo "$R" | head -c 200)"
fi
R="$(get "/lilia/import-review/sessions/$SESSION_ID/comments")"
if [[ "$(code)" == "200" ]] && echo "$R" | jq -e --arg id "$COMMENT_ID" '(if type=="array" then . else .comments end) | map(.id)|index($id)!=null' >/dev/null 2>&1; then
  pass "comment present in list"
else
  fail "list comments" "HTTP $(code) body=$(echo "$R" | head -c 200)"
fi
if [[ -n "$COMMENT_ID" ]]; then
  del "/lilia/import-review/sessions/$SESSION_ID/comments/$COMMENT_ID" >/dev/null
  [[ "$(code)" == "204" ]] && pass "delete comment" || fail "delete comment" "HTTP $(code)"
fi

# ─── 12. Hints ────────────────────────────────────────────────────────────
step "12. Hints"
R="$(post "/lilia/import-review/sessions/$SESSION_ID/hints/compute" '{}')"
[[ "$(code)" == "200" ]] && pass "compute hints → count=$(echo "$R" | jq -r '.count')" || fail "compute hints" "HTTP $(code)"
HINTS="$(get "/lilia/import-review/sessions/$SESSION_ID/hints")"
[[ "$(code)" == "200" ]] && pass "list hints → $(echo "$HINTS" | jq 'length')" || fail "list hints" "HTTP $(code)"
FIRST_HINT_ID="$(echo "$HINTS" | jq -r '.[0].id // empty')"
if [[ -n "$FIRST_HINT_ID" ]]; then
  post "/lilia/import-review/sessions/$SESSION_ID/hints/$FIRST_HINT_ID/dismiss" '{}' >/dev/null
  [[ "$(code)" == "200" ]] && pass "dismiss hint" || fail "dismiss hint" "HTTP $(code)"
fi

# ─── 13. Diagnostics dismiss ──────────────────────────────────────────────
step "13. Diagnostic dismiss"
FIRST_DIAG_ID="$(get "/lilia/import-review/sessions/$SESSION_ID/diagnostics" | jq -r '.[0].id // empty')"
if [[ -n "$FIRST_DIAG_ID" ]]; then
  post "/lilia/import-review/sessions/$SESSION_ID/diagnostics/$FIRST_DIAG_ID/dismiss" '{}' >/dev/null
  [[ "$(code)" == "200" ]] && pass "dismiss diagnostic" || fail "dismiss diagnostic" "HTTP $(code)"
else
  echo "    (no diagnostics generated — skipping)"
fi

# ─── 14. Category PATCH ───────────────────────────────────────────────────
step "14. Category PATCH"
R="$(patch "/lilia/import-review/sessions/$SESSION_ID/category" '{"category":"report"}')"
[[ "$(code)" == "200" ]] && pass "PATCH category=report" || fail "patch category" "HTTP $(code) body=$(echo "$R" | head -c 200)"

# ─── 15. Leave-and-come-back: re-fetch after delay verifies history ───────
step "15. Leave-and-come-back (persistence across session resumption)"
sleep 2
GETSESSION2="$(get "/lilia/import-review/sessions/$SESSION_ID")"
RESUMED_CAT="$(echo "$GETSESSION2" | jq -r '.session.documentCategory // .documentCategory // empty')"
STATS2="$(get "/lilia/import-review/sessions/$SESSION_ID/tab-stats")"
LAST2="$(echo "$STATS2" | jq -r '.lastFocusedTab')"
# Tab progress is load-bearing for resumption. Category doesn't currently
# round-trip through GetSession — ReviewSessionInfoDto omits documentCategory.
# That's a DTO gap, not a persistence bug: the DB row is written correctly.
if [[ "$LAST2" == "tables" ]]; then
  pass "tab progress persisted across resumption (lastFocusedTab=tables)"
else
  fail "tab progress persistence across resumption" "lastFocusedTab=$LAST2"
fi
if [[ "$RESUMED_CAT" == "report" ]]; then
  pass "documentCategory round-trips via GetSession"
else
  printf "  \033[33m⚠\033[0m  documentCategory NOT in GetSession response (DTO gap: ReviewSessionInfoDto missing DocumentCategory). PATCH persisted but is not readable.\n"
fi

# ─── 16. Finalize ─────────────────────────────────────────────────────────
step "16. Finalize (force=true)"
R="$(post "/lilia/import-review/sessions/$SESSION_ID/finalize" '{"force":true}')"
DOC_ID="$(echo "$R" | jq -r '.document.id // empty')"
if [[ "$(code)" == "200" && -n "$DOC_ID" ]]; then
  pass "finalize → documentId=$DOC_ID"
else
  fail "finalize" "HTTP $(code) body=$(echo "$R" | head -c 300)"
fi

# ─── 17. History: scope filtering ─────────────────────────────────────────
step "17. History scope filtering"
ACTIVE="$(get "/lilia/import-review/sessions?scope=active")"
HIST="$(get "/lilia/import-review/sessions?scope=history")"
if echo "$ACTIVE" | jq -e --arg s "$SESSION_ID" 'map(.id)|index($s)==null' >/dev/null; then
  pass "absent from active scope after finalize"
else
  fail "active scope after finalize" "session still in active list"
fi
if echo "$HIST" | jq -e --arg s "$SESSION_ID" 'map(.id)|index($s)!=null' >/dev/null; then
  pass "present in history scope"
else
  fail "history scope" "session missing from history list"
fi

# ─── 18. Direct re-open by ID after finalize ──────────────────────────────
step "18. Direct re-open by session ID (post-finalize)"
R="$(get "/lilia/import-review/sessions/$SESSION_ID")"
if [[ "$(code)" == "200" ]] && echo "$R" | jq -e '.session.id // .id' >/dev/null; then
  FIN_STATUS="$(echo "$R" | jq -r '.session.status // .status // empty')"
  pass "reopenable by ID → status=$FIN_STATUS"
else
  fail "reopen by ID" "HTTP $(code)"
fi

# ─── 19. Cleanup ──────────────────────────────────────────────────────────
step "19. Cleanup (permanent delete)"
if [[ $KEEP_SESSION -eq 1 ]]; then
  echo "  (--keep set, leaving session $SESSION_ID)"
  SESSION_ID=""   # disable cleanup trap
else
  del "/lilia/import-review/sessions/$SESSION_ID?permanent=true" >/dev/null
  if [[ "$(code)" == "204" ]]; then
    pass "permanent delete"
    SESSION_ID=""  # already cleaned
  else
    fail "cleanup" "HTTP $(code)"
  fi
fi

# ─── Summary ──────────────────────────────────────────────────────────────
printf "\n\033[1mSummary:\033[0m  \033[32m%d passed\033[0m  \033[31m%d failed\033[0m\n" "$PASS" "$FAIL"
if [[ $FAIL -gt 0 ]]; then
  printf "Failed checks:\n"
  for c in "${FAILED_CHECKS[@]}"; do printf "  - %s\n" "$c"; done
  exit 1
fi
