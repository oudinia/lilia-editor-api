#!/usr/bin/env node
// Scrape https://tug.org/FontCatalogue/ into a structured JSON catalogue.
// Output: ../tug-fonts.json   (alongside this script's parent dir)
// Cache:  ../.tug-cache/<key>.html
// Warnings: ../.tug-scrape-warnings.log
//
// Usage: node scrape-tug-fonts.mjs
// Polite scraping: 1s sleep between network fetches (cache hits skip the sleep).
// Re-runs are fast because every HTML page is cached on disk.

import { promises as fs } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import https from 'node:https';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const DATA_DIR = path.resolve(__dirname, '..');
const CACHE_DIR = path.join(DATA_DIR, '.tug-cache');
const OUTPUT_PATH = path.join(DATA_DIR, 'tug-fonts.json');
const WARNINGS_PATH = path.join(DATA_DIR, '.tug-scrape-warnings.log');

const BASE_URL = 'https://tug.org/FontCatalogue/';
const USER_AGENT = 'lilia-editor/1.0 (font catalogue importer for https://liliaeditor.com)';
const SLEEP_MS = 1000;

const CATEGORY_PAGES = {
  serif: 'seriffonts.html',
  sansserif: 'sansseriffonts.html',
  typewriter: 'typewriterfonts.html',
  calligraphical: 'calligraphicalfonts.html',
  uncial: 'uncialfonts.html',
  blackletter: 'blackletterfonts.html',
  other: 'otherfonts.html',
  // mathfonts handled separately — drives `hasMath` flag, not a category
};
const MATH_PAGE = 'mathfonts.html';
const ALPHA_PAGE = 'alphfonts.html';

const warnings = [];
function warn(msg) {
  warnings.push(msg);
  console.warn('[warn]', msg);
}

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function cacheKeyFor(urlPath) {
  // urlPath examples: 'alphfonts.html', 'accanthis/', 'mathfonts.html'
  let key = urlPath.replace(/\/$/, '/index').replace(/\//g, '__');
  if (!key.endsWith('.html')) key += '.html';
  return path.join(CACHE_DIR, key);
}

async function ensureDirs() {
  await fs.mkdir(CACHE_DIR, { recursive: true });
  await fs.mkdir(path.dirname(OUTPUT_PATH), { recursive: true });
}

function fetchUrl(url) {
  return new Promise((resolve, reject) => {
    const req = https.get(
      url,
      {
        headers: {
          'User-Agent': USER_AGENT,
          Accept: 'text/html,application/xhtml+xml',
        },
      },
      (res) => {
        if (res.statusCode && res.statusCode >= 300 && res.statusCode < 400 && res.headers.location) {
          // Follow redirect
          const next = new URL(res.headers.location, url).toString();
          res.resume();
          fetchUrl(next).then(resolve, reject);
          return;
        }
        if (res.statusCode !== 200) {
          res.resume();
          reject(new Error(`HTTP ${res.statusCode} for ${url}`));
          return;
        }
        const chunks = [];
        res.on('data', (c) => chunks.push(c));
        res.on('end', () => resolve(Buffer.concat(chunks).toString('utf8')));
        res.on('error', reject);
      },
    );
    req.on('error', reject);
    req.setTimeout(30000, () => {
      req.destroy(new Error(`Timeout fetching ${url}`));
    });
  });
}

async function getPage(urlPath) {
  const cachePath = cacheKeyFor(urlPath);
  try {
    const cached = await fs.readFile(cachePath, 'utf8');
    return { html: cached, fromCache: true };
  } catch {
    /* fallthrough: fetch */
  }
  const url = BASE_URL + urlPath;
  const html = await fetchUrl(url);
  await fs.writeFile(cachePath, html, 'utf8');
  return { html, fromCache: false };
}

// --- HTML helpers (regex-based; the catalogue's HTML is small + uniform) ---

function decodeEntities(s) {
  if (!s) return s;
  return s
    .replace(/&nbsp;/g, ' ')
    .replace(/&amp;/g, '&')
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&quot;/g, '"')
    .replace(/&#(\d+);/g, (_, n) => String.fromCharCode(Number(n)))
    .replace(/&ndash;/g, '–')
    .replace(/&mdash;/g, '—');
}

function stripTags(s) {
  return decodeEntities(s.replace(/<[^>]+>/g, '')).replace(/\s+/g, ' ').trim();
}

// Extract all <li> entries from the alphabetical index page.
// Each <li> has structure:
//   <li><a href="<slug>/"><displayHtml>[&nbsp;[OTF or TTF available|only]]<br>
//   <img src="<slug>/<slug>-2.png" ...></a></li>
function parseAlphaIndex(html) {
  const entries = [];
  // Match <li> blocks containing an <a href="slug/">
  const liRegex = /<li>\s*<a\s+href="([a-z0-9_-]+)\/">([\s\S]*?)<\/a>\s*<\/li>/gi;
  let m;
  while ((m = liRegex.exec(html)) !== null) {
    const slug = m[1];
    const inner = m[2];

    // Pull img src for preview
    let previewImageUrl = null;
    const imgMatch = inner.match(/<img[^>]+src="([^"]+)"/i);
    if (imgMatch) {
      previewImageUrl = new URL(imgMatch[1], BASE_URL).toString();
    }

    // Strip <img>, <br> tags and remaining markup to get the display name + flags
    let textPortion = inner.replace(/<img[^>]*>/gi, '').replace(/<br\s*\/?>/gi, ' ');
    textPortion = stripTags(textPortion);

    // Detect flags
    let hasOtf = false;
    let otfOnly = false;
    if (/\[OTF or TTF only\]/i.test(textPortion)) {
      hasOtf = true;
      otfOnly = true;
    } else if (/\[OTF or TTF available\]/i.test(textPortion)) {
      hasOtf = true;
    }

    // Strip flag annotations from the display name
    let displayName = textPortion
      .replace(/\[OTF or TTF only\]/gi, '')
      .replace(/\[OTF or TTF available\]/gi, '')
      .replace(/\s+/g, ' ')
      .trim();

    entries.push({ slug, displayName, previewImageUrl, hasOtf, otfOnly });
  }
  return entries;
}

// Extract slugs listed on a category page (sub-page links of the form href="slug/")
function parseCategorySlugs(html) {
  const slugs = new Set();
  // We only want <li><a href="slug/"> style links, not nav links.
  // Nav links use href="seriffonts.html" etc.; font links end with "/".
  const liRegex = /<li>[\s\S]*?<a\s+href="([a-z0-9_-]+)\/"/gi;
  let m;
  while ((m = liRegex.exec(html)) !== null) {
    slugs.add(m[1]);
  }
  return slugs;
}

// Find the Usage <pre> block in a font detail page.
// Structure: <h3>Usage</h3>\n<pre> ... </pre>
function extractUsageBlock(html) {
  const re = /<h3>\s*Usage\s*<\/h3>\s*<pre>([\s\S]*?)<\/pre>/i;
  const m = html.match(re);
  if (!m) return null;
  return decodeEntities(m[1]).trim();
}

// Strip LaTeX comments (anything from % to EOL, unless escaped \%).
function stripLatexComments(line) {
  // Remove trailing %... comment, but keep escaped \%
  let out = '';
  for (let i = 0; i < line.length; i++) {
    const c = line[i];
    if (c === '%' && (i === 0 || line[i - 1] !== '\\')) break;
    out += c;
  }
  return out.trimEnd();
}

// Parse usage <pre> body into structured pdflatexUsage + fontspecName.
function parseUsage(usageText, displayName, hasOtf) {
  const cleanedLines = usageText
    .split(/\r?\n/)
    .map((l) => stripLatexComments(l))
    .map((l) => l.trim())
    .filter((l) => l.length > 0);

  const packages = [];
  let fontencOption = null;
  const renewCommands = [];
  let setMainFont = null;
  let usesFontspec = false;

  for (const line of cleanedLines) {
    // \usepackage[OPTS]{NAME}  or  \usepackage{NAME}
    const usepackageRe = /\\usepackage\s*(?:\[([^\]]*)\])?\s*\{([^}]+)\}/g;
    let upm;
    while ((upm = usepackageRe.exec(line)) !== null) {
      const opts = upm[1] != null ? upm[1].trim() : null;
      const name = upm[2].trim();
      if (name === 'fontenc') {
        fontencOption = opts;
        continue;
      }
      if (name === 'fontspec') {
        usesFontspec = true;
        continue;
      }
      packages.push({ name, options: opts && opts.length > 0 ? opts : null });
    }

    // \setmainfont{NAME}  (also \setmainfont[OPTS]{NAME})
    const setMainRe = /\\setmainfont\s*(?:\[[^\]]*\])?\s*\{([^}]+)\}/;
    const smm = line.match(setMainRe);
    if (smm) setMainFont = smm[1].trim();

    // \renewcommand*\familydefault{NAME}  (also without star, with \familydefault wrapped)
    // Match: \renewcommand , optional *, then \familydefault, then {value}
    // Strip a leading backslash from the value (LaTeX writes {\sfdefault}; we store "sfdefault").
    const renewRe = /\\renewcommand\*?\s*\\familydefault\s*\{([^}]+)\}/;
    const rcm = line.match(renewRe);
    if (rcm) {
      let val = rcm[1].trim();
      if (val.startsWith('\\')) val = val.slice(1);
      renewCommands.push(val);
    }
  }

  let pdflatexUsage = null;
  if (packages.length > 0 || fontencOption !== null) {
    pdflatexUsage = {
      fontencOption,
      packages,
      renewCommands,
    };
  }

  let fontspecName = null;
  if (setMainFont) {
    fontspecName = setMainFont;
  } else if (usesFontspec) {
    // fontspec used but no explicit \setmainfont → fall back to display name
    fontspecName = displayName;
  } else if (hasOtf) {
    // No fontspec block in the catalogue, but the font is OTF-capable
    fontspecName = displayName;
  }

  return { pdflatexUsage, fontspecName };
}

// --- Main ---

async function main() {
  await ensureDirs();
  console.log('[info] Output:', OUTPUT_PATH);
  console.log('[info] Cache: ', CACHE_DIR);

  // 1. Fetch alphabetical index → master font list
  console.log('[info] Fetching alphabetical index...');
  const alphaRes = await getPage(ALPHA_PAGE);
  if (!alphaRes.fromCache) await sleep(SLEEP_MS);
  const alphaEntries = parseAlphaIndex(alphaRes.html);
  console.log(`[info] Found ${alphaEntries.length} entries in alpha index`);

  // 2. Fetch each category page, collect slug→categories[]
  const slugCategories = new Map();
  for (const [cat, page] of Object.entries(CATEGORY_PAGES)) {
    console.log(`[info] Fetching category: ${cat} (${page})`);
    const res = await getPage(page);
    if (!res.fromCache) await sleep(SLEEP_MS);
    const slugs = parseCategorySlugs(res.html);
    console.log(`[info]   ${slugs.size} fonts in ${cat}`);
    for (const s of slugs) {
      if (!slugCategories.has(s)) slugCategories.set(s, []);
      slugCategories.get(s).push(cat);
    }
  }

  // 3. Fetch math page → set hasMath flag
  console.log('[info] Fetching math fonts list...');
  const mathRes = await getPage(MATH_PAGE);
  if (!mathRes.fromCache) await sleep(SLEEP_MS);
  const mathSlugs = parseCategorySlugs(mathRes.html);
  console.log(`[info]   ${mathSlugs.size} fonts with math support`);

  // 4. Fetch each detail page, parse Usage
  const out = [];
  let i = 0;
  for (const entry of alphaEntries) {
    i++;
    const { slug, displayName, previewImageUrl, hasOtf, otfOnly } = entry;
    const detailUrl = `${BASE_URL}${slug}/`;
    let html;
    try {
      const res = await getPage(`${slug}/`);
      if (!res.fromCache) await sleep(SLEEP_MS);
      html = res.html;
    } catch (err) {
      warn(`[${slug}] fetch failed: ${err.message}`);
      continue;
    }

    const usage = extractUsageBlock(html);
    if (!usage) {
      warn(`[${slug}] no <h3>Usage</h3><pre> block found`);
      continue;
    }
    if (usage.length === 0) {
      warn(`[${slug}] empty Usage <pre>`);
      continue;
    }

    let parsed;
    try {
      parsed = parseUsage(usage, displayName, hasOtf);
    } catch (err) {
      warn(`[${slug}] usage parse failed: ${err.message}`);
      continue;
    }

    if (!parsed.pdflatexUsage && !parsed.fontspecName) {
      warn(`[${slug}] usage block produced no pdflatex packages and no fontspec name; raw=${JSON.stringify(usage)}`);
      continue;
    }

    // Resolve display name fallback from <title> if the alpha entry had none
    let finalDisplayName = displayName;
    if (!finalDisplayName) {
      const titleMatch = html.match(/<title>([^<]+)<\/title>/i);
      if (titleMatch) {
        finalDisplayName = stripTags(titleMatch[1])
          .replace(/^The LaTeX Font Catalogue\s*[–—-]\s*/i, '')
          .trim();
      }
      if (!finalDisplayName) {
        warn(`[${slug}] could not resolve display name`);
        continue;
      }
    }

    const categories = slugCategories.get(slug) || [];
    const hasMath = mathSlugs.has(slug);

    out.push({
      slug,
      displayName: finalDisplayName,
      categories,
      hasMath,
      hasOtf,
      otfOnly,
      pdflatexUsage: parsed.pdflatexUsage,
      fontspecName: parsed.fontspecName,
      previewImageUrl,
      detailUrl,
    });

    if (i % 50 === 0) {
      console.log(`[info] Progress: ${i}/${alphaEntries.length} processed (${out.length} kept, ${warnings.length} warnings)`);
    }
  }

  // 5. Write output + warnings
  await fs.writeFile(OUTPUT_PATH, JSON.stringify(out, null, 2) + '\n', 'utf8');
  if (warnings.length > 0) {
    await fs.writeFile(WARNINGS_PATH, warnings.join('\n') + '\n', 'utf8');
  } else {
    // Clear stale warnings file if any
    try { await fs.unlink(WARNINGS_PATH); } catch { /* noop */ }
  }

  console.log('');
  console.log(`[done] Wrote ${out.length} entries to ${OUTPUT_PATH}`);
  console.log(`[done] Warnings: ${warnings.length} (${WARNINGS_PATH})`);
}

main().catch((err) => {
  console.error('[fatal]', err);
  process.exit(1);
});
