#!/usr/bin/env node
/**
 * Black-box smoke test for the editor's render pipeline.
 *
 * For each "recipe" (block content sample), we:
 *   1. PUT the content into a sandbox paragraph block
 *   2. GET /preview/latex   — assert expected/forbidden substrings
 *   3. GET /export/pdf?engine=pdflatex — assert it compiles to valid PDF
 *   4. Optionally: assert Typst translation expectations via the
 *      /preview/typst-source endpoint (if/when added)
 *
 * Designed for the dev loop: when you add a new feature (inline mark,
 * spacing command, block variant…), add a recipe here and run
 *   node scripts/smoke-features.mjs
 * It exits 0 on all-pass, 1 on any failure. Runs in ~10–30s depending
 * on PDF compile cache.
 *
 * Default target: http://localhost:5001. Override with --base-url.
 *
 * Auth: dev middleware authenticates any request without an
 * Authorization header as the dev user (see AuthMiddleware.cs).
 * Override with --user <id> if you need a specific dev user.
 */

import { argv, exit } from "node:process";

// ─── Config ──────────────────────────────────────────────────────────

const args = parseArgs(argv.slice(2));
const BASE_URL = args["base-url"] ?? "http://localhost:5001";
const DEV_USER = args["user"] ?? null;
const VERBOSE = args["verbose"] === true;
const FILTER  = args["filter"] ?? null;

function parseArgs(arr) {
  const out = {};
  for (let i = 0; i < arr.length; i++) {
    const a = arr[i];
    if (a.startsWith("--")) {
      const key = a.slice(2);
      const next = arr[i + 1];
      if (next === undefined || next.startsWith("--")) {
        out[key] = true;
      } else {
        out[key] = next;
        i++;
      }
    }
  }
  return out;
}

// ─── Recipes ─────────────────────────────────────────────────────────
// Each recipe puts `text` into a paragraph block, then asserts:
//   latex.contains: substrings that MUST appear in the LaTeX preview
//   latex.lacks:    substrings that MUST NOT appear (e.g. raw markers)
//   pdf.minBytes:   PDF must be at least this large (sanity: compile worked)

const RECIPES = [
  // ── Inline marks ─────────────────────────────────────────────────
  {
    name: "bold + italic",
    text: "Hello **bold** and *italic* world",
    latex: { contains: ["\\textbf{bold}", "\\textit{italic}"] },
    pdf:   { minBytes: 1000 },
  },
  {
    name: "inline code preserves quotes verbatim",
    text: 'Say `console.log("hi")` to debug.',
    latex: {
      // Inside `…` the smart-quote rule should NOT fire — straight
      // `"` must reach the output unchanged.
      contains: ['\\texttt{console.log("hi")}'],
      lacks: ["console.log(``hi", "console.log\\textbackslash"],
    },
    pdf: { minBytes: 1000 },
  },
  {
    name: "smart quotes — straight pairs",
    text: 'She said "hello" to me.',
    latex: { contains: ["``hello''"] },
    pdf:   { minBytes: 1000 },
  },
  {
    name: "comment marker inline",
    text: "Keep [%muted%] here.",
    latex: { contains: ["\\iffalse muted\\fi"], lacks: ["[%muted%]", "[\\%"] },
    pdf:   { minBytes: 1000 },
  },
  {
    name: "comment marker multi-line",
    text: "Before [%line1\nline2\nline3%] after",
    latex: { contains: ["\\begin{comment}", "\\end{comment}"], lacks: ["[%line1"] },
    pdf:   { minBytes: 1000 },
  },
  {
    name: "textcolor + highlight + size",
    text: "Plain {\\large bigword} and \\textcolor{red}{redword} and \\hl[blue]{bluemark}",
    latex: { contains: ["{\\large bigword}", "\\textcolor{red}{redword}", "\\hl[blue]{bluemark}"] },
    pdf:   { minBytes: 1000 },
  },
  {
    name: "superscript + subscript + smallcaps",
    text: "x^2^ H%%2%%O ^^NASA^^",
    latex: {
      contains: ["\\textsuperscript{2}", "\\textsubscript{2}", "\\textsc{NASA}"],
      lacks: ["^2^", "%%2%%", "^^NASA^^"],
    },
    pdf: { minBytes: 1000 },
  },

  // ── Spacing (PR 1 + PR 2) ────────────────────────────────────────
  {
    name: "hspace small/med/large",
    text: "a\\hspace{1em}b\\hspace{2em}c\\hspace{3em}d",
    latex: { contains: ["\\hspace{1em}", "\\hspace{2em}", "\\hspace{3em}"] },
    pdf:   { minBytes: 1000 },
  },
  {
    name: "hfill terminator survives storage",
    text: "Left\\hfill{}Right",
    latex: { contains: ["\\hfill"], lacks: ["\\hfillRight"] },
    pdf:   { minBytes: 1000 },
  },
  {
    name: "vertical skips (smallskip / medskip / bigskip)",
    text: "p1\\smallskip{} p2\\medskip{} p3\\bigskip{} p4",
    latex: { contains: ["\\smallskip", "\\medskip", "\\bigskip"] },
    pdf:   { minBytes: 1000 },
  },
  {
    name: "vspace + vfill",
    text: "Top\\vspace{2em} mid\\vfill{} bottom",
    latex: { contains: ["\\vspace{2em}", "\\vfill"] },
    pdf:   { minBytes: 1000 },
  },

  // ── Heading-bypass regression (comment inside heading) ─────────────
  // The heading path used to call EscapeLatex directly which mangled
  // `[%foo%]` to `[\%foo\%]`. This recipe ensures it's still routed
  // through ProcessLatexText.
  {
    name: "comment marker inside heading text",
    type: "heading",
    level: 2,
    text: "[%hidden heading%]",
    latex: { contains: ["\\subsection{\\iffalse hidden heading\\fi"], lacks: ["[\\%"] },
    pdf:   { minBytes: 1000 },
  },

  // ── List labelFormat + start (Phase 1) ───────────────────────────
  // Verifies that the enumitem options exposed in the modal survive
  // serialization → /preview/latex → PDF export.
  {
    name: "list: ordered with labelFormat=Alpha and start=3",
    type: "list",
    content: { items: ["x", "y", "z"], ordered: true, labelFormat: "Alpha", start: 3 },
    latex: {
      contains: ["\\begin{enumerate}[label=(\\Alph*), start=3]", "\\item x", "\\item y"],
      lacks: ["\\begin{itemize}"],
    },
    pdf: { minBytes: 1000 },
  },
  {
    name: "list: ordered with labelFormat=roman (no start)",
    type: "list",
    content: { items: ["a", "b"], ordered: true, labelFormat: "roman" },
    latex: {
      contains: ["\\begin{enumerate}[label=(\\roman*)]", "\\item a", "\\item b"],
      lacks: ["start="],
    },
    pdf: { minBytes: 1000 },
  },
  {
    name: "list: unordered ignores labelFormat",
    type: "list",
    content: { items: ["alpha", "beta"], ordered: false, labelFormat: "Alpha", start: 5 },
    latex: {
      contains: ["\\begin{itemize}", "\\item alpha", "\\item beta"],
      lacks: ["label=", "start=", "\\begin{enumerate}"],
    },
    pdf: { minBytes: 1000 },
  },

  // ── Phase 2: description lists (kind: "description") ─────────────
  {
    name: "list: description — basic term/desc PDF compiles",
    type: "list",
    content: {
      kind: "description",
      items: [
        { text: "paralist", description: "compact lists and inline lists" },
        { text: "enumitem", description: "control labels and lengths in lists" },
      ],
    },
    latex: {
      contains: [
        "\\begin{description}",
        "\\item[paralist] compact lists and inline lists",
        "\\item[enumitem] control labels and lengths in lists",
        "\\end{description}",
      ],
      lacks: ["\\begin{itemize}", "\\begin{enumerate}"],
    },
    pdf: { minBytes: 1000 },
  },
  {
    name: "list: description — kind overrides ordered=true",
    type: "list",
    content: {
      kind: "description",
      ordered: true,
      items: [{ text: "alpha", description: "first" }],
    },
    latex: {
      contains: ["\\begin{description}", "\\item[alpha] first"],
      lacks: ["\\begin{enumerate}"],
    },
    pdf: { minBytes: 1000 },
  },
];

// ─── HTTP helpers ────────────────────────────────────────────────────

function authHeaders() {
  const headers = { "Content-Type": "application/json" };
  if (DEV_USER) headers["X-Development-User-Id"] = DEV_USER;
  return headers;
}

async function api(path, opts = {}) {
  const url = `${BASE_URL}${path}`;
  const res = await fetch(url, { ...opts, headers: { ...authHeaders(), ...(opts.headers || {}) } });
  return res;
}

// ─── Sandbox doc + block ─────────────────────────────────────────────
//
// Strategy: list user's docs, pick the first one as the sandbox.
// Inside it, find or create a paragraph block we treat as our test
// subject. We restore it to a known placeholder at the end so the
// user's real doc isn't left polluted.

let SANDBOX_DOC_ID = null;
let SANDBOX_BLOCK_ID = null;
let ORIGINAL_BLOCK = null;

async function setupSandbox() {
  const docsRes = await api("/api/documents");
  if (!docsRes.ok) {
    fail(`Failed to list documents: ${docsRes.status} ${await docsRes.text()}`);
    return false;
  }
  const docs = await docsRes.json();
  const list = Array.isArray(docs) ? docs : docs.items || [];
  if (list.length === 0) {
    fail("No documents found for dev user — create one in the editor first.");
    return false;
  }
  SANDBOX_DOC_ID = list[0].id;

  const blocksRes = await api(`/api/documents/${SANDBOX_DOC_ID}/blocks`);
  if (!blocksRes.ok) {
    fail(`Failed to fetch blocks: ${blocksRes.status}`);
    return false;
  }
  const blocks = await blocksRes.json();
  const blockList = Array.isArray(blocks) ? blocks : blocks.items || [];
  const para = blockList.find((b) => b.type === "paragraph") || blockList.find((b) => b.type === "heading");
  if (!para) {
    fail("No paragraph block found in sandbox doc.");
    return false;
  }
  SANDBOX_BLOCK_ID = para.id;
  ORIGINAL_BLOCK = { type: para.type, content: para.content };
  if (VERBOSE) console.log(`[setup] doc=${SANDBOX_DOC_ID.slice(0, 8)} block=${SANDBOX_BLOCK_ID.slice(0, 8)} originalType=${para.type}`);
  return true;
}

async function restoreSandbox() {
  if (!SANDBOX_DOC_ID || !SANDBOX_BLOCK_ID || !ORIGINAL_BLOCK) return;
  await api(`/api/documents/${SANDBOX_DOC_ID}/blocks/${SANDBOX_BLOCK_ID}`, {
    method: "PUT",
    body: JSON.stringify(ORIGINAL_BLOCK),
  });
  if (VERBOSE) console.log("[teardown] restored original block content");
}

async function setBlock(recipe) {
  const type = recipe.type ?? "paragraph";
  // Recipes can either provide a shaped `content` object directly
  // (e.g. list with items/ordered/labelFormat) or fall back to the
  // legacy text-shape used by paragraph/heading recipes.
  const content = recipe.content
    ?? (type === "heading"
      ? { text: recipe.text, level: recipe.level ?? 1 }
      : { text: recipe.text });
  const res = await api(`/api/documents/${SANDBOX_DOC_ID}/blocks/${SANDBOX_BLOCK_ID}`, {
    method: "PUT",
    body: JSON.stringify({ type, content }),
  });
  if (!res.ok) throw new Error(`PUT block failed: ${res.status} ${await res.text()}`);
}

async function fetchLatex() {
  const res = await api(`/api/documents/${SANDBOX_DOC_ID}/preview/latex?refresh=${Date.now()}`);
  if (!res.ok) throw new Error(`GET latex failed: ${res.status}`);
  const data = await res.json();
  return data.content || "";
}

async function fetchPdfBytes() {
  const res = await api(`/api/documents/${SANDBOX_DOC_ID}/export/pdf?engine=pdflatex&refresh=${Date.now()}`);
  if (!res.ok) {
    const body = await res.text();
    throw new Error(`GET pdf failed: ${res.status} — ${body.slice(0, 200)}`);
  }
  const buf = await res.arrayBuffer();
  return buf.byteLength;
}

// ─── Runner ──────────────────────────────────────────────────────────

const results = [];
let failures = 0;

function fail(msg) {
  console.error(`✘ ${msg}`);
  failures++;
}

function pass(name, detail = "") {
  console.log(`✓ ${name}${detail ? ` — ${detail}` : ""}`);
  results.push({ name, status: "pass" });
}

function failRecipe(name, msg) {
  console.error(`✘ ${name}\n  ${msg}`);
  results.push({ name, status: "fail", reason: msg });
  failures++;
}

async function runRecipe(recipe) {
  if (FILTER && !recipe.name.toLowerCase().includes(FILTER.toLowerCase())) return;

  try {
    await setBlock(recipe);
    const latex = await fetchLatex();

    if (recipe.latex?.contains) {
      for (const needle of recipe.latex.contains) {
        if (!latex.includes(needle)) {
          return failRecipe(recipe.name, `LaTeX missing expected: ${JSON.stringify(needle)}`);
        }
      }
    }
    if (recipe.latex?.lacks) {
      for (const needle of recipe.latex.lacks) {
        if (latex.includes(needle)) {
          return failRecipe(recipe.name, `LaTeX contains forbidden: ${JSON.stringify(needle)}`);
        }
      }
    }

    if (recipe.pdf) {
      const bytes = await fetchPdfBytes();
      if (recipe.pdf.minBytes && bytes < recipe.pdf.minBytes) {
        return failRecipe(recipe.name, `PDF too small: ${bytes} bytes (min ${recipe.pdf.minBytes})`);
      }
      return pass(recipe.name, `pdf=${bytes}B`);
    }
    pass(recipe.name);
  } catch (e) {
    failRecipe(recipe.name, e.message || String(e));
  }
}

// ─── Main ────────────────────────────────────────────────────────────

async function main() {
  console.log(`smoke-features — base=${BASE_URL}${FILTER ? ` filter=${FILTER}` : ""}`);
  if (!(await setupSandbox())) {
    exit(1);
  }
  try {
    for (const recipe of RECIPES) {
      await runRecipe(recipe);
    }
  } finally {
    await restoreSandbox();
  }
  const total = results.length;
  const passed = total - results.filter((r) => r.status === "fail").length;
  console.log(`\n${passed}/${total} passed${failures ? ` — ${failures} failed` : ""}`);
  exit(failures === 0 ? 0 : 1);
}

main().catch((e) => {
  console.error(`runner crashed: ${e?.stack || e}`);
  exit(2);
});
