#!/usr/bin/env bash
# Generate a corpus of .docx files from inline markdown templates by
# piping to pandoc. Each fixture targets one Word feature surface
# (lists, headings, tables, formatting, code, blockquotes, math, etc).
#
# Run: bash generate.sh
# Output: ../generated/<name>.docx

set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
OUT="$HERE/../generated"
mkdir -p "$OUT"

if ! command -v pandoc >/dev/null 2>&1; then
  echo "pandoc not found. Install via apt or https://pandoc.org" >&2
  exit 2
fi

emit() {
  local name=$1
  local md=$2
  echo "$md" | pandoc -f markdown -t docx -o "$OUT/$name.docx"
  echo "  generated $name.docx"
}

emit_file() {
  local name=$1
  local file=$2
  pandoc -f markdown -t docx -o "$OUT/$name.docx" < "$file"
  echo "  generated $name.docx (from file)"
}

echo "Generating DOCX corpus into $OUT"

# === Plain prose ===
emit "plain-prose" "This is a plain paragraph with no formatting.

Another paragraph follows. Sentences are short.

A third paragraph for body length."

emit "long-paragraph" "$(printf 'A long paragraph %.0s' {1..30})that wraps across many lines without any inline formatting markers."

# === Headings ===
emit "headings-h1-h6" "# H1 title

## H2 section

### H3 subsection

#### H4 subsubsection

##### H5 minor

###### H6 minimal

Body."

emit "heading-with-bold" "# Important: **Critical Update**

Body text after a heading that contains bold."

# === Lists ===
emit "bullet-list-flat" "Items:

- First
- Second
- Third
- Fourth"

emit "numbered-list-flat" "Steps:

1. First step
2. Second step
3. Third step"

emit "bullet-list-nested" "Tree:

- Root
  - Child A
  - Child B
    - Grandchild
  - Child C
- Sibling"

emit "numbered-nested" "Outline:

1. First
   1. First-sub
   2. Second-sub
2. Second
3. Third"

emit "mixed-bullet-numbered" "Mixed:

- Bullet
  1. Numbered under bullet
  2. Another
- Bullet two"

# === Inline formatting ===
emit "inline-bold-italic" "A paragraph with **bold**, *italic*, ***bold italic***, ~~strike~~, and \`inline code\` markers."

emit "links-and-images" "Visit [the example site](https://example.com) for more.

Email me at <foo@example.com>.

![diagram](https://example.com/diagram.png)"

# === Tables ===
emit "table-simple" "| A | B | C |
|---|---|---|
| 1 | 2 | 3 |
| 4 | 5 | 6 |
| 7 | 8 | 9 |"

emit "table-with-alignment" "| Left | Centre | Right |
|:-----|:------:|------:|
| a | b | c |
| 1 | 2 | 3 |"

emit "table-with-formatting" "| Col 1 | Col 2 |
|---|---|
| **bold** | *italic* |
| \`code\` | normal |
| [link](https://x.com) | text |"

# === Blockquotes ===
emit "blockquote-simple" "Some intro:

> This is a quoted paragraph.
>
> It spans multiple lines.

Back to body."

emit "blockquote-nested" "Quote nesting:

> Level one
>
> > Level two
> >
> > > Level three
>
> Back to one."

# === Code ===
emit "code-block-fenced" "Example code:

\`\`\`python
def hello(name):
    return f'Hello, {name}!'
\`\`\`

After the block."

emit "code-block-no-lang" "Code:

\`\`\`
$ git status
$ git commit -m 'fix'
\`\`\`"

emit "inline-code-paragraph" "Use \`git status\` to check, then \`git add .\` and \`git commit\` to record."

# === Math ===
emit "inline-math" "Pi is approximately \$\\pi \\approx 3.14\$ and Euler's number is \$e \\approx 2.72\$."

emit "display-math" "The famous identity:

\$\$
e^{i\\pi} + 1 = 0
\$\$

connects five fundamental constants."

# === Mixed-content document types ===
emit "memo-style" "# MEMO

**To:** All Staff

**From:** Director

**Date:** April 25, 2026

**Re:** Office hours update

Effective immediately, office hours are 9-5 Monday through Friday."

emit "report-with-sections" "# Quarterly Report

## Summary

This quarter showed strong growth across all segments.

## Financial Highlights

- Revenue up 15% YoY
- Margins improved 3 percentage points
- Free cash flow of \$25M

## Outlook

Expect continued growth into Q4.

## Risks

| Risk | Impact | Likelihood |
|---|---|---|
| Supply chain | High | Medium |
| Currency | Medium | Low |
| Regulatory | Low | Low |"

emit "meeting-notes" "# Engineering Sync — April 22

**Attendees:** Alice, Bob, Carol, Dave

## Topics

1. Sprint planning
2. Deployment pipeline
3. Open items

## Decisions

- Move to weekly deploys
- Adopt new code review tool

## Action items

- [ ] Alice: ship auth migration
- [ ] Bob: draft billing spec
- [ ] Carol: schedule retro"

emit "blog-post" "# Why We Migrated to PostgreSQL

If you're here, you probably saw our outage last month. This post covers the migration plan.

## Background

We outgrew MongoDB at around the 1M-user mark.

## What we tried first

We tried sharding. It worked for a while, then operational complexity caught up.

## The migration

Six engineers, three months, parallel writes during cutover.

## Results

- 40% latency reduction
- 50% storage reduction
- Simpler operational model"

emit "thesis-chapter" "# Chapter 3: Convergence

## 3.1 Definitions

A sequence \$(a_n)\$ of real numbers converges to \$L\$ if for every \$\\varepsilon > 0\$ there exists \$N\$ such that \$|a_n - L| < \\varepsilon\$ when \$n \\geq N\$.

## 3.2 Examples

The sequence \$a_n = 1/n\$ converges to 0.

## 3.3 Theorems

**Theorem 1.** Every convergent sequence is bounded.

*Proof.* Let \$(a_n) \\to L\$. Then for \$\\varepsilon = 1\$ there is \$N\$ such that \$|a_n - L| < 1\$ for \$n \\geq N\$, so \$|a_n| < |L| + 1\$ for those \$n\$. The first \$N-1\$ terms are bounded individually."

emit "recipe" "# Tomato Pasta

**Serves:** 4 — **Time:** 30 min

## Ingredients

- 400g spaghetti
- 2 tbsp olive oil
- 3 cloves garlic
- 1 can (400g) tomatoes
- Salt and pepper
- Fresh basil

## Method

1. Boil salted water
2. Cook pasta
3. Heat oil, add garlic
4. Add crushed tomatoes
5. Simmer 10 min
6. Toss with pasta
7. Garnish with basil"

emit "letter" "**Jane Doe** — *42 Elm St, Boston, MA*

April 25, 2026

Dear Hiring Manager,

I am writing to apply for the Software Engineer role advertised on your careers page.

With over eight years of experience building distributed systems, I would contribute immediately to your team.

Sincerely,
Jane Doe"

emit "exam-questions" "# Midterm Exam

## Question 1 (10 points)

What is the time complexity of binary search on a sorted array of size \$n\$?

a) \$O(n)\$
b) \$O(n^2)\$
c) \$O(\\log n)\$
d) \$O(1)\$

## Question 2 (15 points)

Prove that \$1 + 2 + \\cdots + n = n(n+1)/2\$.

## Question 3 (25 points)

Implement quicksort. Analyse worst-case and average-case runtime."

emit "syllabus" "# CS 343 Syllabus

**Instructor:** Prof. Park

**TA:** Sam Wu

**Office hours:** Tuesday 2-4

## Topics

- Probabilistic method
- Approximation algorithms
- Online learning
- Streaming

## Grading

- Problem sets: 40%
- Midterm: 25%
- Final project: 25%
- Participation: 10%"

emit "manual-cli" "# CLI Manual

## Installation

\`\`\`bash
brew install cloudcli
\`\`\`

## Authentication

Run \`cloud login\`.

## Commands

### deploy

Deploy a service:

\`\`\`bash
cloud deploy --service api --env production
\`\`\`

Flags:
- \`--service NAME\` — service to deploy
- \`--env ENV\` — target environment
- \`--wait\` — block until healthy

### status

Check status: \`cloud status --service api\`."

emit "press-release" "**FOR IMMEDIATE RELEASE**

# Example Corp Announces Q2 Results

San Francisco, April 25, 2026 — Example Corp today reported revenue of \$1.2B for Q2 2026, up 15% YoY.

\"We're pleased with the strong performance,\" said CEO Jane Smith. \"Our investments in AI are paying off.\"

## Financial Highlights

- Revenue: \$1.2B (+15% YoY)
- Operating income: \$300M
- Free cash flow: \$250M

## Outlook

The company expects continued growth into Q3."

emit "case-study" "# Customer Case Study: Global Retail Co

## Background

Global Retail Co operates 500+ stores across North America.

## Challenge

Inventory turnover was lagging industry average by 30%.

## Solution

Implemented our AI-powered forecasting system.

## Results

- Inventory turnover improved 25%
- Stockouts reduced 40%
- Operating margin up 200bps

## Next steps

Expand to international stores."

# === Edge cases ===
emit "very-short" "Hi."

emit "empty-paragraphs" "Paragraph one.



Paragraph two after blank.



Paragraph three."

emit "punctuation-heavy" "Punctuation test: comma, semicolon; colon: period. Question? Exclamation! Em-dash—usage. En-dash – usage. Ellipsis…

Quotes: \"double\" and 'single'. Smart quotes: \"double\" and 'single'."

emit "unicode-mix" "Languages and symbols:

- Café (French)
- Naïve (with diaeresis)
- Straße (German with ß)
- Москва (Russian)
- 日本語 (Japanese)
- مرحبا (Arabic)
- 🚀 emoji
- € £ ¥ currency
- ½ ¼ ¾ fractions
- ± × ÷ math"

emit "dense-formatting" "A paragraph with **bold word**, *italic word*, ~~strike word~~, \`code word\`, [link word](https://x.com), and **bold *and italic* nested** all in one line."

# === Tables (heavy) ===
emit "table-large" "| Region | Q1 | Q2 | Q3 | Q4 | Total |
|---|---|---|---|---|---|
| North | 100 | 120 | 110 | 130 | 460 |
| South | 90 | 95 | 100 | 105 | 390 |
| East | 200 | 210 | 220 | 230 | 860 |
| West | 150 | 160 | 170 | 180 | 660 |
| Central | 75 | 80 | 85 | 90 | 330 |"

emit "table-with-header-row" "| Name | Role | Joined |
|---|---|---|
| Alice | Engineer | 2020 |
| Bob | Designer | 2021 |
| Carol | Manager | 2019 |"

# === Wide doc with mixed content ===
emit "mixed-everything" "# Mixed-content document

This doc exercises **bold**, *italic*, \`code\`, and [links](https://x.com) in one paragraph.

## A list

- Bullet one with \`inline code\`
- Bullet two with **bold**
- Bullet three with *italic*

## A numbered list

1. First step
2. Second step with \$x = 1\$
3. Third step

## A table

| Key | Value |
|---|---|
| alpha | 1 |
| beta | 2 |

## A blockquote

> This is the conclusion.
>
> It has multiple paragraphs."

# === Real-world templates ===
emit "research-abstract" "# Efficient Attention via Low-Rank Factorisation

## Abstract

We propose a low-rank approximation to the attention matrix that reduces complexity from \$O(n^2)\$ to \$O(nr)\$ where \$r \\ll n\$, with minimal loss on downstream tasks.

## 1. Introduction

Transformer attention scales quadratically in sequence length, making long-context inference expensive.

## 2. Method

Our approach trains a 1B draft model using distillation. At inference, the draft generates \$k\$ candidate tokens.

## 3. Results

We see 2.3x speedup with token-level accuracy unchanged."

emit "interview-transcript" "# Interview: April 22, 2026

**Interviewer:** Tell me about your background.

**Candidate:** I have eight years of experience in distributed systems, mostly in fintech.

**Interviewer:** What's a project you're proud of?

**Candidate:** I led a migration from MongoDB to PostgreSQL that reduced p99 latency by 40%.

**Interviewer:** What was the biggest challenge?

**Candidate:** Coordinating the cutover across 12 services without downtime."

emit "faq" "# Frequently Asked Questions

## Q: How do I install?

A: Use \`brew install cloudcli\` on macOS.

## Q: Where are credentials stored?

A: In \`~/.cloudcli/credentials\` after running \`cloud login\`.

## Q: How do I report a bug?

A: Open an issue at https://github.com/example/cloudcli.

## Q: Is there a free tier?

A: Yes, the free tier includes 100 requests per month."

emit "changelog" "# Changelog

## v3.1.0 — 2026-04-22

### Added
- Streaming support for the deploy command
- New \`--dry-run\` flag

### Changed
- Default timeout increased from 30s to 60s
- Updated help text for clarity

### Fixed
- Fixed crash when config file was empty
- Fixed retry logic for transient errors

## v3.0.0 — 2026-03-15

### Breaking
- Renamed \`--app\` flag to \`--service\`
- Removed deprecated \`legacy-deploy\` command"

emit "issue-template" "# Bug Report: Login fails with SSO

## Summary

Login via SSO redirects in a loop after the IdP returns successfully.

## Steps to reproduce

1. Click \"Sign in with SSO\"
2. Complete IdP authentication
3. Get redirected to /callback
4. Loop back to /login

## Expected

Land on /dashboard.

## Environment

- Browser: Chrome 124
- OS: macOS 14.5
- Tenant: example-corp"

emit "code-review-comments" "# Code Review: PR #4521

Overall the change is solid. A few comments:

## src/auth.ts

Line 42: consider extracting the validation into its own function.

Line 58: the regex \`^[a-z]+\$\` doesn't match uppercase. Intentional?

## src/api.ts

Line 12: missing error handling on the network call.

Line 34: this is a great refactor — much cleaner.

## tests/auth.test.ts

Add a test for the empty input case."

emit "user-story" "# User Story: Bulk export

**As a** data analyst

**I want to** export all my saved queries to CSV

**So that** I can share them with my team

## Acceptance criteria

- [ ] Export button on the queries page
- [ ] Generates CSV with one row per query
- [ ] Includes columns: name, sql, created, last_run
- [ ] Downloads in under 5 seconds for up to 1000 queries"

emit "release-notes" "# Release Notes — April 2026

## What's new

This month brought significant improvements to the deployment pipeline and a new dashboard for tracking metrics.

## Highlights

- **New dashboard** with custom metrics support
- **Deployment pipeline** now 3x faster
- **API improvements** with bulk endpoints

## Bug fixes

We resolved 23 bug reports this month, including the long-standing issue with timezone handling in scheduled jobs."

emit "tutorial-step-by-step" "# Tutorial: Deploy your first app

## Prerequisites

- Account on cloud.example.com
- CLI installed
- Git repository

## Step 1: Initialise

\`\`\`bash
cloud init
\`\`\`

This creates a \`cloud.yaml\` in your repo.

## Step 2: Configure

Edit \`cloud.yaml\` to set the service name and runtime:

\`\`\`yaml
name: my-app
runtime: python3.11
\`\`\`

## Step 3: Deploy

\`\`\`bash
cloud deploy
\`\`\`

The deployment will run in your default environment.

## Step 4: Verify

Check the dashboard to confirm your app is running."

emit "checklist" "# Pre-deploy checklist

## Code

- [ ] All tests pass
- [ ] Code review complete
- [ ] No TODO comments
- [ ] Documentation updated

## Infrastructure

- [ ] Database migration applied
- [ ] Environment variables set
- [ ] Secrets rotated if needed
- [ ] Monitoring alerts configured

## Team

- [ ] Team notified
- [ ] On-call aware
- [ ] Rollback plan documented"

# === Curriculum: more lists in different shapes ===
emit "list-with-paragraphs" "Items with multi-paragraph entries:

- First item.

  This is a continuation paragraph for the first item.

- Second item.

  And its continuation.

- Third item.

  Final continuation."

emit "definition-list" "Term and definition format:

API
:   Application Programming Interface

REST
:   Representational State Transfer

HTTP
:   HyperText Transfer Protocol"

emit "footnotes" "This text has a footnote[^1] and another[^2].

[^1]: First footnote content.
[^2]: Second footnote with **bold**."

emit "task-list" "Tasks:

- [x] Done item
- [x] Another done
- [ ] Pending
- [ ] Also pending
- [x] Done"

emit "horizontal-rules" "Section one.

---

Section two after a horizontal rule.

***

Section three after another rule."

emit "html-passthrough" "Some <span style='color: red'>red text</span> via raw HTML.

A <kbd>Ctrl</kbd>+<kbd>C</kbd> shortcut.

<sup>superscript</sup> and <sub>subscript</sub>."

# Summary
total=$(ls "$OUT"/*.docx 2>/dev/null | wc -l)
echo ""
echo "Total .docx fixtures generated: $total"
