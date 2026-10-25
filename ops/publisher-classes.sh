#!/usr/bin/env bash
# The publisher classes that are not in TeX Live (IOP's iopjournal, SIAM's siamart): is the publisher's
# current version the one installed in TEXMFLOCAL? (Publisher templates phase 3, the template update
# service, 25 Oct 2026.)
#
#   ops/publisher-classes.sh            check only: downloads to a temporary folder, compares, changes nothing
#   ops/publisher-classes.sh install --yes
#                                       installs what changed, unchanged, keeping the previous files
#                                       beside them (<dir>.prev-<date>) for a rollback; then mktexlsr
#
# After an install: run the publisher tests with TeX (they fail on a class whose version no profile was
# verified with: PublisherClassVersionsTests), update PublisherProfiles' TemplateVersion, and for SIAM a
# new release renames the class (siamart<yymmdd>.cls): add it to PublisherClasses.Names, the allow-list
# and the profiles. The classes' licences: IOP's is the LPPL; SIAM's set may only be passed on whole.
set -euo pipefail

MODE="${1:-check}"
CONFIRM="${2:-}"
LOCAL="$(kpsewhich -var-value=TEXMFLOCAL)"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
UA="Mozilla/5.0 (X11; Linux x86_64) Lilia publisher-classes check"
CHANGED=0

IOP_ZIP="https://publishingsupport.iopscience.iop.org/wp-content/uploads/2025/07/ioplatextemplate.zip"
SIAM_BASE="https://epubs.siam.org/pb-assets/macros/standard"
SIAM_FILES="siamplain.bst docsiamart.tex docsiamart.pdf references.bib ex_article.tex ex_supplement.tex ex_shared.tex"

say() { printf '%s\n' "$*"; }
sha() { sha256sum "$1" | cut -d' ' -f1; }

# ── IOP ──────────────────────────────────────────────────────────────
mkdir -p "$WORK/iop"
if curl -fsSL -A "$UA" -o "$WORK/iop.zip" "$IOP_ZIP" && unzip -oq "$WORK/iop.zip" -d "$WORK/iop"; then
  NEW="$WORK/iop/iopjournal.cls"; OLD="$LOCAL/tex/latex/iopjournal/iopjournal.cls"
  if [[ -f "$OLD" && "$(sha "$NEW")" == "$(sha "$OLD")" ]]; then
    say "✓ IOP iopjournal.cls: up to date ($(grep -m1 -o 'ProvidesClass{iopjournal}\[[^]]*' "$OLD" | cut -d[ -f2))"
  else
    say "· IOP iopjournal.cls: the publisher's differs ($(grep -m1 -o 'ProvidesClass{iopjournal}\[[^]]*' "$NEW" | cut -d[ -f2))"
    CHANGED=1
  fi
  say "  note: the archive's address is the July 2025 one; IOP may publish a newer archive under another address (https://publishingsupport.iopscience.iop.org/?p=413)."
else
  say "✗ IOP: could not download $IOP_ZIP"
fi

# ── SIAM: the current class's name is in its documentation ───────────
if curl -fsSL -A "$UA" -o "$WORK/docsiamart.tex" "$SIAM_BASE/docsiamart.tex"; then
  CLS="$(grep -o 'siamart[0-9]\{6\}' "$WORK/docsiamart.tex" | sort -u | tail -1)"
  INSTALLED="$(ls "$LOCAL/tex/latex/siam/" 2>/dev/null | grep -o 'siamart[0-9]\{6\}' | sort -u | tail -1 || true)"
  if [[ -z "$CLS" ]]; then
    say "✗ SIAM: no class name in docsiamart.tex"
  elif [[ "$CLS" == "$INSTALLED" ]]; then
    curl -fsSL -A "$UA" -o "$WORK/$CLS.cls" "$SIAM_BASE/$CLS.cls"
    if [[ "$(sha "$WORK/$CLS.cls")" == "$(sha "$LOCAL/tex/latex/siam/$CLS.cls")" ]]; then
      say "✓ SIAM $CLS.cls: up to date"
    else
      say "· SIAM $CLS.cls: same name, different file"; CHANGED=1
    fi
  else
    say "· SIAM: the publisher's class is $CLS (installed: ${INSTALLED:-none}): a new release"; CHANGED=1
  fi
else
  say "✗ SIAM: could not download $SIAM_BASE/docsiamart.tex"
fi

if [[ "$MODE" != "install" ]]; then
  [[ $CHANGED == 1 ]] && say "Something changed. To install it: ops/publisher-classes.sh install --yes" || say "Nothing to install."
  exit 0
fi
if [[ "$CONFIRM" != "--yes" ]]; then say "install needs --yes"; exit 2; fi
[[ $CHANGED == 0 ]] && { say "Nothing to install."; exit 0; }

STAMP="$(date +%Y%m%d-%H%M)"
backup() { [[ -d "$1" ]] && cp -a "$1" "$1.prev-$STAMP" && say "  previous files kept in $1.prev-$STAMP"; return 0; }

# IOP, unchanged.
if [[ -f "$WORK/iop/iopjournal.cls" ]]; then
  backup "$LOCAL/tex/latex/iopjournal"
  mkdir -p "$LOCAL/tex/latex/iopjournal" "$LOCAL/doc/latex/iopjournal"
  cp "$WORK/iop/iopjournal.cls" "$WORK/iop/orcid.pdf" "$LOCAL/tex/latex/iopjournal/"
  cp "$WORK/iop/"*.tex "$WORK/iop/"*guidelines*.pdf "$LOCAL/doc/latex/iopjournal/" 2>/dev/null || true
  { echo "Installed $(date -u +%FT%TZ) by ops/publisher-classes.sh, unchanged."; echo "IOP: $IOP_ZIP (LPPL 1.3c)";
    (cd "$LOCAL" && sha256sum tex/latex/iopjournal/*); } > "$LOCAL/doc/latex/iopjournal/SOURCE.txt"
  say "✓ IOP installed"
fi

# SIAM: the whole set, as its licence asks.
if [[ -n "${CLS:-}" ]]; then
  for f in "$CLS.cls" $SIAM_FILES; do curl -fsSL -A "$UA" -o "$WORK/siam-$f" "$SIAM_BASE/$f"; done
  backup "$LOCAL/tex/latex/siam"; backup "$LOCAL/bibtex/bst/siam"
  mkdir -p "$LOCAL/tex/latex/siam" "$LOCAL/bibtex/bst/siam" "$LOCAL/doc/latex/siam"
  cp "$WORK/siam-$CLS.cls" "$LOCAL/tex/latex/siam/$CLS.cls"
  cp "$WORK/siam-siamplain.bst" "$LOCAL/bibtex/bst/siam/siamplain.bst"
  for f in docsiamart.tex docsiamart.pdf references.bib ex_article.tex ex_supplement.tex ex_shared.tex; do cp "$WORK/siam-$f" "$LOCAL/doc/latex/siam/$f"; done
  { echo "Installed $(date -u +%FT%TZ) by ops/publisher-classes.sh, unchanged: the complete SIAM standard macro set."; echo "SIAM: $SIAM_BASE/ ($CLS). Not to be changed or distributed apart from the full set.";
    (cd "$LOCAL" && sha256sum tex/latex/siam/* bibtex/bst/siam/*); } > "$LOCAL/doc/latex/siam/SOURCE.txt"
  say "✓ SIAM $CLS installed (an older siamart class stays installed: documents that use it keep compiling)"
fi

mktexlsr "$LOCAL" >/dev/null 2>&1 || true
say "Next: run the publisher tests with TeX, update PublisherProfiles (TemplateVersion; for a new SIAM class, Names and the allow-list), then restart the API."
