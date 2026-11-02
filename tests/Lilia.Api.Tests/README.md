# Lilia.Api.Tests: where a test goes

Tests sit by what they test, not by kind (2 Nov 2026, step 1 of the slices:
`lilia-docs/reports/2026-11-02-api-vertical-slices.md`). The namespace is the folder.

| Folder | What it tests |
|---|---|
| `Features/Accounts` | sign-in, two-step, the Stytch webhook, profile, preferences, onboarding, account export and deletion |
| `Features/Documents` | documents, blocks, versions, labels, recent, studio, capabilities, the document hub |
| `Features/Sharing` | collaborators, invites, public links, sharing emails |
| `Features/Teams` | teams and team documents |
| `Features/Compilation` | LaTeX render and validation, preview, compile cache and slots, Typst compile, TikZ figures, Unicode, fonts |
| `Features/Export` | PDF, LaTeX, Word (`Docx/`), EPUB, HTML, Markdown, Typst export (`Typst/`), publishers and submissions (`Publishers/`) |
| `Features/Import` | LaTeX, Word, PDF, Overleaf and AI import, the import review, jobs |
| `Features/AskLilia` | the AI endpoints, routing, the knowledge base |
| `Features/References` | bibliography, references, citations |
| `Features/Library` | templates, starters, snippets, tables, insertions |
| `Features/Connector` | the MCP server and connector transfers |
| `Features/Platform` | diagnostics, coverage pages, feedback, telemetry, dev-only routes |
| `Engines/` | `Lilia.Engines` and the maths in `Lilia.Core`: the LaTeX emitter (`Latex/`), themes, maths parsing, the TeX sandbox |
| `Shared/` | what every slice uses: plan limits, email, the database, zip safety, the LaTeX catalogue |
| `Integration/Infrastructure`, `Fixtures/` | the test plumbing and the corpora: not tests |

A database test is still marked by its base class (`IntegrationTestBase`, category `Integration`), wherever
it sits. `lilia test Features.Sharing` runs one slice's tests.
