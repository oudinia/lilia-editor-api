#!/usr/bin/env bash
# Log a shipped fix/feature to the prod changelog (surfaces on /whats-new).
# Usage: log-fix.sh <area> <fix|feature> "<title-en>" "<detail-en>" [verified] [shot_url] [date]
# fr/es are added later by the translation pass; the page falls back to en.
set -euo pipefail
AREA="${1:?area}"; KIND="${2:?fix|feature}"; TITLE="${3:?title}"; DETAIL="${4:?detail}"
VERIFIED="${5:-false}"; SHOT="${6:-}"; DT="${7:-$(date -u +%F)}"
DBID=cdefbbfd-7d4e-4075-9b9e-2ba34cdb45cb
URI=$(doctl databases connection "$DBID" --format URI --no-header 2>/dev/null)
TITLE_J=$(python3 -c "import json,sys;print(json.dumps({'en':sys.argv[1]}))" "$TITLE")
DETAIL_J=$(python3 -c "import json,sys;print(json.dumps({'en':sys.argv[1]}))" "$DETAIL")
SHOT_SQL="NULL"; [ -n "$SHOT" ] && SHOT_SQL="'$SHOT'"
psql "$URI" -v ON_ERROR_STOP=1 -c \
"INSERT INTO changelog_entries (entry_date, area, kind, status, title, detail, verified, shot_url, sort)
 VALUES (DATE '$DT', '$AREA', '$KIND', 'shipped', '$TITLE_J'::jsonb, '$DETAIL_J'::jsonb, $VERIFIED, $SHOT_SQL,
         COALESCE((SELECT max(sort)+10 FROM changelog_entries WHERE entry_date=DATE '$DT'),10));"
echo "logged: [$AREA/$KIND] $TITLE"
