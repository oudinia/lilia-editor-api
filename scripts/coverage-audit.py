#!/usr/bin/env python3
"""
Audit: catalog coverage claims vs parser actual handling.

Cross-references latex_tokens against the LatexParser.cs hardcoded
handler sets + regex command references. Output report buckets every
row into:

  A_truthful            — coverage matches parser handler
  B_lying_full          — catalog 'full' with no handler (real bug)
  B_lying_partial       — catalog 'partial' with no handler
  C_underclaimed        — parser handles but catalog says unsupported/none
  D_honest_unsupported  — no handler, catalog agrees
  E_shimmed_trusted     — catalog 'shimmed'; not audited here

Inputs (all under /tmp — regenerate via the helpers below):
  catalog_now.tsv         kind|name|pkg|coverage_level|maps_to_block_type
  parser_known_envs.txt   one env per line
  parser_theorem_envs.txt one env per line
  parser_passthrough_envs.txt
  parser_algo_cmds.txt    one lowercased algorithmic keyword per line
  p_raw.txt               literal \\cmd strings from parser source

To regenerate inputs:

  # Catalog (from prod; swap creds env file as needed)
  set -a; . scripts/backup-db.env; set +a
  PGPASSWORD="$DB_PASSWORD" psql "sslmode=require host=$DB_HOST port=$DB_PORT dbname=$DB_NAME user=$DB_USER" \\
    -At -F '|' -c "SELECT kind, name, COALESCE(package_slug,'kernel'), coverage_level, COALESCE(maps_to_block_type,'') FROM latex_tokens ORDER BY kind, name;" \\
    > /tmp/catalog_now.tsv

  # Parser lists
  P=src/Lilia.Import/Services/LatexParser.cs
  awk '/HashSet<string> KnownEnvironments/,/};/'       $P | grep -oE '"[^"]+"' | tr -d '"' | sort -u > /tmp/parser_known_envs.txt
  awk '/Dictionary<string, TheoremEnvironmentType>/,/};/' $P | grep -oE '"[a-zA-Z]+"' | tr -d '"' | sort -u > /tmp/parser_theorem_envs.txt
  awk '/HashSet<string> PassThroughEnvironments/,/};/' $P | grep -oE '"[^"]+"' | tr -d '"' | sort -u > /tmp/parser_passthrough_envs.txt
  grep -oE '@"\\\\\\([A-Z|]+\\)' $P | head -1 | sed 's/@"\\\\\\(//;s/)$//' | tr '|' '\\n' | tr 'A-Z' 'a-z' | sort -u > /tmp/parser_algo_cmds.txt
  grep -oE '\\[a-zA-Z]+' $P | sort -u > /tmp/p_raw.txt

Output:
  /tmp/audit_report.md with full tables of lying / under-claimed rows.

Related:
  lilia-docs/technical/latex-coverage-architecture.md — target design
  lilia-docs/technical/latex-coverage-diag-cli.md — usage harness
"""
import os, sys, re
from collections import defaultdict

def load_set(path):
    if not os.path.exists(path): return set()
    with open(path) as f:
        return {line.strip() for line in f if line.strip()}

def load_cmd_refs(path):
    """Extract command names from parser source. grep over @"\\cmd..." verbatim
    strings, so each line is '\\\\<letters>' (two literal backslashes)."""
    if not os.path.exists(path): return set()
    cmds = set()
    with open(path) as f:
        for line in f:
            s = line.strip()
            # Accept either one or two backslashes at the start
            if s.startswith('\\\\') and s[2:].isalpha():
                cmds.add(s[2:])
            elif s.startswith('\\') and s[1:].isalpha():
                cmds.add(s[1:])
    return cmds

known_envs = load_set('/tmp/parser_known_envs.txt')
theorem_envs = load_set('/tmp/parser_theorem_envs.txt')
passthrough_envs = load_set('/tmp/parser_passthrough_envs.txt')
algo_cmds_lc = load_set('/tmp/parser_algo_cmds.txt')  # lowercase
parser_cmd_refs = load_cmd_refs('/tmp/p_raw.txt')

# Commands the parser routes explicitly (sourced from LatexParser.cs at
# 2026-04-22; update when the parser changes). Each set maps to a
# distinct handler path — see `handler_kind` below.
PARSER_SECTION = {'section', 'subsection', 'subsubsection', 'paragraph', 'subparagraph'}
PARSER_CITATION = {'cite','citep','citet','citealp','citealt','parencite','textcite',
                   'footcite','autocite','nocite'}
PARSER_PRESERVED_INLINE = {'cite','citep','citet','citeauthor','citeyear','nocite',
                           'ref','pageref','eqref','autoref','cref','Cref',
                           'label','href','url','hyperref',
                           'footnote','footnotemark','footnotetext',
                           'input','include'}
PARSER_CODE_INLINE = {'texttt','inlinecode','code','cmdname','macroname','pkgname',
                      'filename','path','envname','lstinline','mintinline'}
PARSER_MARKDOWN_WRAPPERS = {'textbf','textit','emph','underline'}
PARSER_METADATA_EXTRACT = {'title','author','date','caption','thanks','affil','affiliation'}

# Full set of commands with a proven, specific parser handler.
cmd_handled_set = (
    parser_cmd_refs
    | PARSER_SECTION
    | PARSER_CITATION
    | PARSER_PRESERVED_INLINE
    | PARSER_CODE_INLINE
    | PARSER_MARKDOWN_WRAPPERS
    | PARSER_METADATA_EXTRACT
)

# Handler-kind lookup (first match wins)
def handler_for_cmd(name):
    if name in PARSER_METADATA_EXTRACT: return 'metadata-extract'
    if name in PARSER_SECTION: return 'section-regex'
    if name in PARSER_CITATION: return 'citation-regex'
    if name in PARSER_PRESERVED_INLINE: return 'inline-preserved'
    if name in PARSER_CODE_INLINE: return 'inline-code'
    if name in PARSER_MARKDOWN_WRAPPERS: return 'inline-markdown'
    if name in parser_cmd_refs: return 'parser-regex'
    if name.lower() in algo_cmds_lc: return 'algo-regex'
    return None

# Case-insensitive algo match (parser uses IgnoreCase)
def algo_handled(name):
    return name.lower() in algo_cmds_lc

# Structurally-handled envs = known + theorem + passthrough
env_handled = known_envs | theorem_envs | passthrough_envs

# Commands handled by parser (direct regex references + algorithmic keywords)
# We treat the parser_cmd_refs as the authoritative set for text-mode handling.
cmd_handled = parser_cmd_refs

# Math-context commands: anything inside $...$ or equation envs — parser
# preserves math via balanced $-matching, then KaTeX renders. We can't
# enumerate "all math commands" but can mark this as a semi-handled class.
# For audit purposes, we flag known math commands separately so the bucket
# isn't called "lying" when it's "KaTeX-handled in math mode".
MATH_COMMANDS = {
  # Greek lowercase + uppercase
  'alpha','beta','gamma','delta','epsilon','zeta','eta','theta','iota','kappa','lambda',
  'mu','nu','xi','pi','rho','sigma','tau','upsilon','phi','chi','psi','omega',
  'Gamma','Delta','Theta','Lambda','Xi','Pi','Sigma','Upsilon','Phi','Psi','Omega',
  # Operators
  'sum','prod','int','iint','iiint','oint','bigcup','bigcap','bigoplus','bigotimes',
  'lim','liminf','limsup','max','min','sup','inf',
  'log','ln','sin','cos','tan','sec','csc','cot','arcsin','arccos','arctan',
  'sinh','cosh','tanh','exp','det','dim','ker','deg','Pr','gcd',
  # Relations
  'le','ge','ne','equiv','sim','simeq','approx','cong','propto','prec','succ',
  'preceq','succeq','ll','gg',
  # Sets
  'in','notin','subset','subseteq','supset','supseteq','cup','cap','setminus',
  'emptyset','varnothing',
  # Arrows
  'to','rightarrow','leftarrow','leftrightarrow','Rightarrow','Leftarrow','Leftrightarrow',
  'mapsto','hookrightarrow','twoheadrightarrow','implies','iff','uparrow','downarrow',
  'gets',
  # Logic
  'forall','exists','nexists','neg','land','lor','top','bot','vdash','models',
  # Binary ops / symbols
  'cdot','times','div','pm','mp','ast','star','circ','bullet','oplus','otimes',
  'ominus','odot',
  # Constants
  'infty','partial','nabla','prime','aleph','hbar','degree','angle',
  # Fractions / roots / binomials
  'frac','dfrac','tfrac','binom','dbinom','sqrt',
  # Dots
  'dots','ldots','cdots','vdots','ddots',
  # Math fonts
  'mathbb','mathbf','mathcal','mathrm','mathit','mathfrak','mathscr','mathsf',
  # Delimiters
  'left','right','langle','rangle','lceil','rceil','lfloor','rfloor',
  # Accents
  'hat','tilde','bar','vec','dot','ddot','breve','check','overline','overbrace',
  'underbrace','widehat','widetilde',
  # Utilities
  'ensuremath','notag','text','quad','qquad',
}

MATH_ENVS = {
  'cases','pmatrix','bmatrix','vmatrix','Vmatrix','smallmatrix','array','subequations',
}

# Load catalog
rows = []
with open('/tmp/catalog_now.tsv') as f:
    for line in f:
        parts = line.rstrip('\n').split('|')
        if len(parts) < 4: continue
        kind, name, pkg, cov = parts[0], parts[1], parts[2], parts[3]
        maps = parts[4] if len(parts) > 4 else ''
        rows.append((kind, name, pkg, cov, maps))

# Classify each row
buckets = defaultdict(list)

for kind, name, pkg, cov, maps in rows:
    if kind == 'environment':
        handled_struct = name in env_handled
        handled_math = name in MATH_ENVS
        handler_kind = (
            'known-structural' if name in known_envs else
            'theorem-like' if name in theorem_envs else
            'pass-through' if name in passthrough_envs else
            'math-env (KaTeX)' if handled_math else
            # Parser's unknown-env catch-all (LatexParser.cs line 1088)
            # preserves every other env as ImportLatexPassthrough. That's
            # a weak handler — content round-trips, preview doesn't render —
            # so it supports a 'partial' claim but not 'full'.
            'passthrough' if cov in ('partial','unsupported','none') else
            None
        )
        handled = handler_kind is not None
    else:  # command
        handler_kind = handler_for_cmd(name)
        handled = handler_kind is not None
        if not handled and name in MATH_COMMANDS:
            handled = True
            handler_kind = 'math (KaTeX)'
        # NormaliseInlineCommands catch-all: any `\cmd{arg}` that isn't
        # otherwise handled gets its arg extracted. Weak handler; supports
        # 'partial', not 'full'.
        if not handled and cov in ('partial','unsupported','none'):
            handled = True
            handler_kind = 'inline-catch-all'

    # Bucket:
    #  A = truthful full/partial (handled & claimed ≥ partial)
    #  B = lying full (claimed full but no handler — except math cases, which are half-handled)
    #  C = under-claimed (handled but sitting at unsupported/none)
    #  D = honest unsupported (no handler, claimed unsupported/none)
    #  E = shimmed (handled via explicit shim path — we don't audit these here; trust)
    if cov == 'shimmed':
        buckets['E_shimmed_trusted'].append((kind, name, pkg, cov, handler_kind))
    elif cov in ('full','partial') and handled:
        buckets['A_truthful'].append((kind, name, pkg, cov, handler_kind))
    elif cov == 'full' and not handled:
        buckets['B_lying_full'].append((kind, name, pkg, cov, handler_kind))
    elif cov == 'partial' and not handled:
        buckets['B_lying_partial'].append((kind, name, pkg, cov, handler_kind))
    elif cov in ('unsupported','none') and handled:
        buckets['C_underclaimed'].append((kind, name, pkg, cov, handler_kind))
    elif cov in ('unsupported','none') and not handled:
        buckets['D_honest_unsupported'].append((kind, name, pkg, cov, handler_kind))
    else:
        buckets['Z_unclassified'].append((kind, name, pkg, cov, handler_kind))

# Report
out = []
out.append('# Catalog-vs-parser audit report\n')
out.append(f'Catalog rows: **{len(rows)}**\n')
for k in ['A_truthful','B_lying_full','B_lying_partial','C_underclaimed',
          'D_honest_unsupported','E_shimmed_trusted','Z_unclassified']:
    out.append(f'- {k}: **{len(buckets[k])}**')
out.append('')

def dump_bucket(tag, title):
    items = sorted(buckets[tag], key=lambda r: (r[0], r[1]))
    if not items: return
    out.append(f'## {title} ({len(items)})\n')
    out.append('| kind | name | pkg | claim | handler |')
    out.append('|---|---|---|---|---|')
    for kind, name, pkg, cov, hk in items:
        out.append(f'| {kind} | `{name}` | {pkg} | {cov} | {hk or "—"} |')
    out.append('')

dump_bucket('B_lying_full',    'B — lying full (catalog claims full, parser has no handler)')
dump_bucket('B_lying_partial', 'B — lying partial (catalog claims partial, parser has no handler)')
dump_bucket('C_underclaimed',  'C — under-claimed (parser handles, catalog says unsupported/none)')
dump_bucket('D_honest_unsupported', 'D — honest unsupported (parser has no handler, catalog agrees)')
dump_bucket('E_shimmed_trusted',    'E — shimmed (not audited — class-shim path)')

report = '\n'.join(out)
with open('/tmp/audit_report.md','w') as f:
    f.write(report)

# Console summary
print(f'Catalog rows: {len(rows)}')
for k in ['A_truthful','B_lying_full','B_lying_partial','C_underclaimed',
          'D_honest_unsupported','E_shimmed_trusted','Z_unclassified']:
    print(f'  {k}: {len(buckets[k])}')
print('\nFull report at /tmp/audit_report.md')
