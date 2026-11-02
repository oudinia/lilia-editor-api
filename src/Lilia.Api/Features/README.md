# Features: the API's slices

The API is moving from layers (`Controllers/`, `Services/`) to feature slices, one slice per change
(2 Nov 2026; the plan and its order: `lilia-docs/reports/2026-11-02-api-vertical-slices.md`).

| Slice | State |
|---|---|
| `Library` | templates, snippets, formulas, tables, insertions: moved (step 2) |
| `References` | bibliography, references, citation styles: moved (step 2) |
| `Teams` | the first slice; its service moved in (step 2) |
| `Platform` | changelog, diagnostics, dev tools, e2e, logs, feedback, sync telemetry, coverage: moved (step 2) |
| `Onboarding`, and the `Handlers/` of `Exports`, `Imports`, `Sharing`, `Telemetry`, `Versioning` | earlier; Accounts, Sharing, Connector come next (step 3) |

**Inside a slice:** `Controllers/`, `Services/`, and, for what other code may use, `Contracts/` (interfaces and
the records they take) or `Dtos/`. The namespace is the folder.

**Between slices:**
1. Code outside a slice uses it through its interfaces, `Contracts/` or `Dtos/`, never its services or
   controllers. `Program.cs` (the composition root) is the one exception: it registers them.
   `tests/Lilia.Api.Tests/Shared/SliceBoundaryTests.cs` fails on any other use, and runs on every API change.
2. What three or more slices need stays shared (plan limits, email, audit).
3. `Lilia.Engines` is the core: slices call it, it never calls them.

Tests sit in the same slice under `tests/Lilia.Api.Tests/Features/<Slice>/`.
