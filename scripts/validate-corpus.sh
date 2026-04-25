#!/usr/bin/env bash
# Validate that every .tex fixture under latex-corpus/ is syntactically
# valid LaTeX by running pdflatex in non-stop mode and checking exit 0.
# Output goes to a scratch dir; the script prints a pass/fail table.
#
# Usage:
#   scripts/validate-corpus.sh           # all fixtures
#   scripts/validate-corpus.sh curated   # just curated/
#
# Opt-in — not wired into the test runner. Takes ~30s per fixture for
# large CV templates that pull many packages.

set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
CORPUS="$ROOT/tests/Lilia.Api.Tests/Fixtures/latex-corpus"
SUBDIR="${1:-}"
SCRATCH=$(mktemp -d)
trap 'rm -rf "$SCRATCH"' EXIT

if [[ -n "$SUBDIR" ]]; then
  SEARCH="$CORPUS/$SUBDIR"
else
  SEARCH="$CORPUS"
fi

if [[ ! -d "$SEARCH" ]]; then
  echo "Search dir not found: $SEARCH" >&2
  exit 2
fi

if ! command -v pdflatex >/dev/null 2>&1; then
  echo "pdflatex not found on PATH. Install TeX Live (apt install texlive-latex-recommended)." >&2
  exit 3
fi

PASS=0
FAIL=0
FAILED_FILES=()

printf "%-70s | %-6s | %s\n" "Fixture" "Result" "Note"
printf "%s\n" "---------------------------------------------------------------------------------------"

while IFS= read -r -d '' f; do
  rel="${f#$CORPUS/}"
  # Skip fixtures flagged as known-malformed — pdflatex will legitimately
  # fail these; they exist to test parser tolerance.
  if grep -q "^% KNOWN-INVALID" "$f"; then
    printf "%-70s | %-6s | %s\n" "$rel" "SKIP" "marked KNOWN-INVALID"
    continue
  fi

  log="$SCRATCH/$(basename "$f").log"
  cd "$SCRATCH"
  if pdflatex -interaction=nonstopmode -halt-on-error -output-directory "$SCRATCH" "$f" > "$log" 2>&1; then
    PASS=$((PASS + 1))
    printf "%-70s | %-6s |\n" "$rel" "OK"
  else
    FAIL=$((FAIL + 1))
    FAILED_FILES+=("$rel")
    # Extract first error line to give a hint
    note=$(grep -m 1 "^! " "$log" 2>/dev/null | head -c 80 || echo "")
    printf "%-70s | %-6s | %s\n" "$rel" "FAIL" "$note"
  fi
done < <(find "$SEARCH" -name "*.tex" -print0)

echo ""
echo "Summary: $PASS passed, $FAIL failed"
if [[ $FAIL -gt 0 ]]; then
  echo "Failed fixtures:"
  printf '  - %s\n' "${FAILED_FILES[@]}"
  exit 1
fi
