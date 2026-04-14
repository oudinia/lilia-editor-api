# Lilia Test Coverage

> Last audited: 2026-04-14 (updated same day with new converter + export tests)

---

## Summary

| Category | Count | Notes |
|---|---|---|
| Backend test methods (xUnit) | 1 303 | Unit + integration + E2E |
| Frontend test cases (Vitest) | 256 | Exporter/parser packages |
| Frontend E2E (Playwright) | 1 | Minimal — home page only |
| Manual health-check tools | 2 | DocxTestGenerator, DocxExportTester |
| CI workflows | 4 | 2 per repo |
| Services with unit tests | ~11 / 50+ | 22 % |
| Controllers with integration tests | 11 / 34 | 32 % |

---

## 1. Backend — Unit & Service Tests (617 methods)

### LaTeX Pipeline (222 tests)

| File | Tests | What it covers |
|---|---|---|
| `LatexBlockCompilationTests.cs` | 52 | Block → LaTeX string conversion for every block type |
| `LatexImportCoverageTests.cs` | 52 | LaTeX import scenarios (equations, theorems, tables) |
| `LatexCoverageTests.cs` | 39 | LaTeX rendering edge cases |
| `LatexIntegrationTests.cs` | 44 | Full pdflatex compilation via Docker |
| `LatexTypstComparisonTests.cs` | 16 | LaTeX vs Typst output parity |
| `RenderServiceTests.cs` | 41 | Preview / PDF rendering |
| `TypstMathEmitterTests.cs` | 23 | Typst math emission |

### Import Services (110 tests)

| File | Tests | What it covers |
|---|---|---|
| `PdfImportServiceTests.cs` | 40 | PDF → blocks (Mathpix/Mineru) |
| `MathpixPdfImportServiceTests.cs` | 24 | Mathpix API integration |
| `AiImportServiceTests.cs` | 24 | AI-assisted import |
| `MathParserTests.cs` | 22 | Math expression parsing |
| `MathpixClientTests.cs` | 11 | Mathpix HTTP client |
| `MineruClientTests.cs` | 9 | Mineru client |

### Export & Preview (72 tests)

| File | Tests | What it covers |
|---|---|---|
| `EpubServiceTests.cs` | 44 | EPUB export — format conversion |
| `DocumentExportServiceTests.cs` | 14 | Export orchestration (LaTeX, DOCX, PDF) |
| `PreviewCacheServiceTests.cs` | 14 | Preview caching logic |

### Content Processing (112 tests)

| File | Tests | What it covers |
|---|---|---|
| `BlockEnrichmentTests.cs` | 41 | Block validation and enrichment |
| `AccessibilityTests.cs` | 29 | Accessibility tree generation |
| `InlineFormattingTests.cs` | 27 | Markdown-style inline formatting |
| `HtmlTableParserTests.cs` | 10 | HTML table → block parsing |
| `HeadingLevelCorrectionTests.cs` | 5 | Heading normalization |

### DOCX Pipeline (285 tests — added 2026-04-14)

| File | Tests | What it covers |
|---|---|---|
| `LatexToOmmlConverterTests.cs` | 130 | LaTeX → OMML: fractions, radicals, sub/sup, nary ops, delimiters, accents, bars, groupChr, limUpp/limLow, boxed, matrices, aligned, text runs, mathbb, Greek, symbols, \not, tokenizer |
| `OmmlToLatexConverterTests.cs` | 95 | OMML → LaTeX: all element types + round-trip identity tests |
| `DocxExportServiceTests.cs` | 60 | DocxExportService: all block types, equation paths (OMML/PNG/fallback), mocked converters, real converter integration |

### AI (33 tests)

| File | Tests | What it covers |
|---|---|---|
| `AiAssistantServiceTests.cs` | 33 | AI assistant response logic |

---

## 2. Backend — Controller Integration Tests (163 methods)

Run against a Testcontainers PostgreSQL instance. Full HTTP round-trips.

| File | Tests | Endpoints covered |
|---|---|---|
| `ImportReviewControllerTests.cs` | 38 | Import session CRUD, review workflow, finalize |
| `DocumentsControllerTests.cs` | 22 | Document CRUD, permissions, sharing |
| `LaTeXValidationControllerTests.cs` | 20 | `POST /latex/validate` |
| `LaTeXScenarioTests.cs` | 17 | Complex LaTeX round-trip scenarios |
| `BlocksControllerTests.cs` | 17 | Block CRUD, reorder, convert |
| `BlockTypesControllerTests.cs` | 10 | Block type definitions |
| `ExportControllerTests.cs` | 9 | `GET /export/latex`, `/export/docx`, `/export/pdf` |
| `BibliographyControllerTests.cs` | 7 | Bibliography CRUD, DOI/ISBN lookup |
| `SchemaDriftTests.cs` | 7 | Detects EF model / DB schema drift |
| `TemplatesControllerTests.cs` | 6 | Template CRUD |
| `LabelsControllerTests.cs` | 6 | Label management |
| `PreferencesControllerTests.cs` | 4 | User preferences |
| `PdfImportIntegrationTests.cs` | 3 | PDF import workflow |

---

## 3. Backend — E2E Tests (238 methods)

Run against the live API (daily CI + manual trigger via `workflow_dispatch`).

### Core Workflows (18 tests)
- `AuthoringWorkflowTests.cs` (6) — create → edit → export full flow
- `ImportReviewWorkflowTests.cs` (4) — DOCX upload → review → finalize
- `TemplateWorkflowTests.cs` (3)
- `StudioWorkflowTests.cs` (5)

### Feature Coverage (46 tests)
- `DocumentsE2ETests.cs` (7), `BlocksE2ETests.cs` (4), `BibliographyE2ETests.cs` (6)
- `FormulasE2ETests.cs` (4), `ExportE2ETests.cs` (3), `LaTeXValidationE2ETests.cs` (6)
- `LaTeXRenderTests.cs` (5 combinatorial), `BlockTypeExportTests.cs` (3 combinatorial)

### Edge Cases & Adversarial (43 tests)
- `ExportAdversarialTests.cs` (7) — malicious LaTeX, XSS, Unicode extremes
- `ExportEdgeCaseTests.cs` (7), `BlockEdgeCaseTests.cs` (10)
- `DocumentEdgeCaseTests.cs` (11), `BoundaryTests.cs` (13)
- `CrossEntityTests.cs` (12), `MalformedInputTests.cs` (10)
- `InjectionTests.cs` (7), `ConcurrencyEdgeCaseTests.cs` (5)
- `BatchOperationTests.cs` (5), `SharingEdgeCaseTests.cs` (5), `DoubleOperationTests.cs` (8)

### Auth, Search, Collab (37 tests)
- `AuthorizationE2ETests.cs` (6), `AuthEndpointTests.cs` (2)
- `SearchFilterE2ETests.cs` (7), `CommentsE2ETests.cs` (5)
- `VersionsE2ETests.cs` (3), `DraftBlocksE2ETests.cs` (5)
- `PreviewE2ETests.cs` (4), `SnippetsE2ETests.cs` (4)
- `LabelsE2ETests.cs` (3), `TemplatesE2ETests.cs` (4)
- `TrashE2ETests.cs` (3), `PreferencesE2ETests.cs` (2), `HealthTests.cs` (3)

---

## 4. Frontend — Package Tests (256 cases, Vitest)

Located in `lilia-cloud/packages/lilia/`.

| File | Tests | What it covers |
|---|---|---|
| `latex.test.ts` | 55 | LaTeX exporter — all block types, edge cases |
| `typst.test.ts` | 50 | Typst exporter |
| `html.test.ts` | 50 | HTML exporter |
| `markdown.test.ts` | 41 | Markdown exporter |
| `lml-parser.test.ts` | 40 | LML document format parser |
| `epub.test.ts` | 25 | EPUB importer |

---

## 5. Manual Health-Check Tools

### DocxTestGenerator (`tools/DocxTestGenerator/`)

Builds a comprehensive DOCX from scratch, imports it via the API with `SkipReview=true`, fetches the resulting blocks, and exports to LaTeX.

**ValidationReport checks (11 checks, all must pass):**

| Block type | Minimum | Description |
|---|---|---|
| `heading` | 6 | H1–H6 headings |
| `paragraph` | 5 | Body paragraphs |
| `equation` | 5 | Display equations — Gaussian, Euler-Poisson, quadratic, matrix, sum |
| `code` | 3 | Python / Go / SQL code blocks |
| `table` | 2 | Data tables |
| `list` | 2 | Bullet + numbered lists |
| `theorem` | 4 | Theorem / Lemma / Definition / Proof environments |
| `blockquote` | 1 | Indented blockquote |
| `bibliography` | 1 | References section |
| `abstract` | 1 | Abstract block |
| `figure` | 1 | Embedded image |

Also prints: block type distribution, unknown types (importer gaps), first 120 lines of exported `main.tex`.

**Run:**
```bash
dotnet run --project tools/DocxTestGenerator -- http://localhost:5001 <user-id> /tmp
```

---

### DocxExportTester (`tools/DocxExportTester/`)

Creates a Lilia document via API with all block types, exports to DOCX, validates the DOCX XML, and optionally re-imports (round-trip).

**DocxValidator checks (7 checks, all must pass):**

| Check | Threshold | Description |
|---|---|---|
| OMML equations | ≥ 7 | Native Word math — must NOT be plain-text fallbacks |
| Plain-text fallbacks | = 0 | `$latex$` or `[latex]` in text = OMML conversion failure |
| Headings | ≥ 5 | Word Heading styles present |
| Tables | ≥ 1 | `<w:tbl>` elements present |
| Code blocks | present | Consolas-font runs detected |
| Abstract | present | Style or text contains "abstract" |
| File size | > 3 KB | Sanity check — empty/corrupt DOCX detection |

Also reports: equation render path breakdown (OMML / PNG fallback / plain text), round-trip block count comparison.

**Run:**
```bash
dotnet run --project tools/DocxExportTester -- http://localhost:5001 <user-id> /tmp
# Options: --no-roundtrip  --keep
```

---

## 6. CI / CD

### lilia-editor-api

| Workflow | Trigger | What runs |
|---|---|---|
| `ef-migrations-check.yml` | PR (when entities or migrations change) | Blocks merge if EF model has unmigrated changes |
| `e2e-tests.yml` | Daily 06:00 UTC + manual `workflow_dispatch` | Full 238 E2E test suite against production |

### lilia-cloud

| Workflow | Trigger | What runs |
|---|---|---|
| `validate-prs.yml` | Every PR | Biome lint + Playwright E2E (`pnpm --filter web e2e:ci`) |
| `pages.yml` | Push to main | Publishes documentation |

---

## 7. Coverage Gaps

### Critical — zero tests, high risk

| Area | What's missing |
|---|---|
| `DocxImportService.cs` | Unit tests for DOCX → block conversion of every element type |
| ~~`DocxExportService.cs`~~ | ✅ **Done** — 60 unit tests added 2026-04-14 |
| ~~`LatexToOmmlConverter.cs`~~ | ✅ **Done** — 130 unit tests added 2026-04-14 |
| ~~`OmmlToLatexConverter.cs`~~ | ✅ **Done** — 95 unit tests + round-trip tests added 2026-04-14 |
| `LaTeXExportService.cs` | 42 KB service — no unit tests |
| `ImportReviewService.cs` | 48 KB service — no unit tests (only integration-level E2E) |

### High — missing coverage for active features

| Area | What's missing |
|---|---|
| `BibliographyService.cs` | DOI/ISBN lookup logic, citation formatting, BibTeX serialisation |
| `BlockService.cs` | Reorder, convert, batch operations |
| `DocumentService.cs` | Permission checks, soft-delete, version management |
| `ConvertController` | Block type conversion endpoint |
| `JobsController` | Import job lifecycle, status polling |
| `FormulasController` | Formula CRUD, category filtering |
| Frontend Playwright | Only 1 test (home redirect); no editor, formula, export flows |

### Medium — PDF on pause

PDF import tests (`PdfImportServiceTests`, `MathpixPdfImportServiceTests`) are well covered but the live pipeline is paused pending Mathpix setup. No action needed until that's resolved.

### Low — good enough for now

- 17 utility/admin controllers (LoremController, LogController, etc.) — low risk
- `SnippetService`, `LabelService`, `PreferencesService` — covered by E2E
- TypeScript exporters — 256 Vitest tests provide strong coverage

---

## 8. Recommended Next Tests (priority order)

1. ~~**`LatexToOmmlConverter` unit tests**~~ ✅ **Done** (130 tests, 2026-04-14)

2. ~~**`OmmlToLatexConverter` round-trip tests**~~ ✅ **Done** (95 tests + round-trip, 2026-04-14)

3. ~~**`DocxExportService` unit tests**~~ ✅ **Done** (60 tests, 2026-04-14)

4. **`DocxImportService` integration tests** — use DocxBuilder to generate fixture DOCX files in-memory, run through import pipeline, assert block types and content. Reuse Testcontainers setup already in the test project.

5. **Frontend Playwright** — add tests for: equation block edit/render, DOCX export download, formula quick-add save flow.
