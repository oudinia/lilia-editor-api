#!/usr/bin/env bash
# validation-watch/pull-errors.sh
#
# Pulls block-validation errors/warnings from the PROD DigitalOcean Postgres
# that have landed since the last run (watermark), joined to the offending
# block's content + the document title, so an agent can analyze the LaTeX
# failures. Advances the watermark to the newest row it printed.
#
# Usage:
#   pull-errors.sh                       # new errors since watermark, advance it
#   pull-errors.sh --since '2026-06-01'  # override watermark (no advance)
#   pull-errors.sh --warnings            # include warnings too (default: errors)
#   pull-errors.sh --no-commit           # print but do NOT advance the watermark
#
# No secrets are stored: the connection URI is fetched fresh from doctl.
set -euo pipefail

DBID=cdefbbfd-7d4e-4075-9b9e-2ba34cdb45cb
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
STATE="$DIR/state-watermark.txt"

SINCE=""
COMMIT=1
STATUS_FILTER="v.status = 'error'"
while [ $# -gt 0 ]; do
  case "$1" in
    --since)     SINCE="${2:-}"; COMMIT=0; shift 2 ;;
    --warnings)  STATUS_FILTER="v.status IN ('error','warning')"; shift ;;
    --no-commit) COMMIT=0; shift ;;
    *) shift ;;
  esac
done

URI=$(doctl databases connection "$DBID" --format URI --no-header 2>/dev/null)
if [ -z "$URI" ]; then echo "ERROR: could not get DB URI from doctl" >&2; exit 1; fi

# Watermark: explicit --since, else state file, else 24h ago.
if [ -z "$SINCE" ]; then
  if [ -f "$STATE" ]; then SINCE="$(cat "$STATE")"; else SINCE="$(date -u -d '24 hours ago' '+%Y-%m-%d %H:%M:%S')"; fi
fi

echo "## validation-watch pull"
echo "watermark (since): $SINCE  |  filter: $STATUS_FILTER  |  commit: $COMMIT"
echo

# 1) Grouped summary — normalized LaTeX error line -> count. The normalized
#    line keeps the "...Error: ..." span so the same class collapses across
#    blocks.
echo "### Error classes since watermark (newest activity)"
psql "$URI" -At -F $'\t' <<SQL
WITH recent AS (
  SELECT v.status,
         COALESCE(
           (regexp_match(v.error_message, '!\s*(.*Error:[^\n]*)'))[1],
           (regexp_match(v.error_message, '!\s*([^\n]+)'))[1],
           left(v.error_message, 80)
         ) AS err_class
  FROM block_validations v
  WHERE $STATUS_FILTER AND v.validated_at > '$SINCE'
)
SELECT count(*) AS n, status, err_class
FROM recent
GROUP BY status, err_class
ORDER BY n DESC
LIMIT 40;
SQL
echo

# 2) Detail rows — each new error with block type, content snippet, doc title.
echo "### Error detail rows (max 60, newest first)"
psql "$URI" -At -F $'\037' <<SQL
SELECT v.validated_at,
       v.validator,
       v.status,
       b.type AS block_type,
       d.title AS doc_title,
       v.document_id,
       v.block_id,
       left(regexp_replace(v.error_message, '\s+', ' ', 'g'), 400) AS error_message,
       left(b.content::text, 600) AS block_content
FROM block_validations v
LEFT JOIN blocks b    ON b.id = v.block_id
LEFT JOIN documents d ON d.id = v.document_id
WHERE $STATUS_FILTER AND v.validated_at > '$SINCE'
ORDER BY v.validated_at DESC
LIMIT 60;
SQL
echo

# 3) Advance the watermark to the newest row we just printed.
if [ "$COMMIT" = "1" ]; then
  NEWMAX=$(psql "$URI" -At -c "SELECT max(v.validated_at) FROM block_validations v WHERE $STATUS_FILTER AND v.validated_at > '$SINCE';")
  if [ -n "$NEWMAX" ]; then echo "$NEWMAX" > "$STATE"; echo "watermark advanced to: $NEWMAX"; else echo "no new rows; watermark unchanged"; fi
fi
