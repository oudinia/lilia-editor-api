#!/usr/bin/env python3
"""
Build the self-contained KaTeX bundle the HTML export inlines.

An exported .html is a file someone downloads. It has to typeset its own
equations from disk, on a plane, in five years — so the maths renderer, its
stylesheet and its fonts all travel inside it. A CDN <script> would have been
one line and would stop working the moment the reader is offline.

Only woff2 fonts are embedded. Every browser released this decade reads woff2,
and the woff/ttf fallbacks in KaTeX's own CSS point at files that will not
exist beside a downloaded page — a browser that tries them takes a 404 for
every glyph.

    ./ops/build-katex-asset.py          # regenerate after a KaTeX upgrade

Reads from lilia-web-editor/node_modules/katex, writes
src/Lilia.Engines/Assets/katex-inline.html. Do not edit that file by hand.
"""
import base64
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
DIST = os.path.join(HERE, "..", "..", "lilia-web-editor", "node_modules", "katex", "dist")
OUT = os.path.join(HERE, "..", "src", "Lilia.Engines", "Assets", "katex-inline.html")

if not os.path.isdir(DIST):
    sys.exit(f"KaTeX not found at {DIST} — run npm install in lilia-web-editor first.")

css = open(os.path.join(DIST, "katex.min.css"), encoding="utf-8").read()
inlined = 0


def inline_font(match):
    global inlined
    block = match.group(0)
    woff2 = re.search(r"url\(fonts/([^)]+?\.woff2)\)", block)
    if not woff2:
        return ""  # no woff2 variant — drop the face rather than ship a broken src
    path = os.path.join(DIST, "fonts", woff2.group(1))
    data = base64.b64encode(open(path, "rb").read()).decode()
    inlined += 1
    # The src declaration is the last in the block, so it ends at "}" not ";".
    return re.sub(
        r"src:[^}]+",
        f'src:url(data:font/woff2;base64,{data}) format("woff2")',
        block,
        count=1,
    )


css = re.sub(r"@font-face\{[^}]*\}", inline_font, css)
if inlined == 0:
    sys.exit("No fonts were inlined — KaTeX's CSS layout has changed; fix the pattern.")

katex_js = open(os.path.join(DIST, "katex.min.js"), encoding="utf-8").read()
auto_js = open(os.path.join(DIST, "contrib", "auto-render.min.js"), encoding="utf-8").read()
version = json.load(open(os.path.join(DIST, "..", "package.json")))["version"]

# $$…$$ is listed before $…$ so a display equation is not read as two inline
# ones. Code and pre are excluded: a paper about shell scripting must not have
# its dollar signs typeset as algebra.
init = r"""
(function () {
  function render() {
    if (!window.renderMathInElement) return;
    window.renderMathInElement(document.body, {
      delimiters: [
        { left: "$$", right: "$$", display: true },
        { left: "\\[", right: "\\]", display: true },
        { left: "$", right: "$", display: false },
        { left: "\\(", right: "\\)", display: false }
      ],
      ignoredTags: ["script", "noscript", "style", "textarea", "pre", "code", "option"],
      throwOnError: false
    });
  }
  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", render);
  } else {
    render();
  }
})();
"""

os.makedirs(os.path.dirname(OUT), exist_ok=True)
with open(OUT, "w", encoding="utf-8") as f:
    f.write(f"<!-- KaTeX {version}, inlined by ops/build-katex-asset.py. Do not edit. -->\n")
    f.write(f"<style>{css}</style>\n")
    f.write(f"<script>{katex_js}</script>\n")
    f.write(f"<script>{auto_js}</script>\n")
    f.write(f"<script>{init}</script>\n")

print(f"  KaTeX {version} → {os.path.relpath(OUT, HERE)}")
print(f"  {inlined} font faces inlined, {os.path.getsize(OUT) // 1024} KB total")
