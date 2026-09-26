# Lilia API — E2E Tests

End-to-end tests that run against a live API instance (local or remote).

## Quick Start

### Against local API (dev mode, no auth validation)

```bash
# Start the API first — against a local database, never Neon
Database__Target=local dotnet run --project src/Lilia.Api --launch-profile http

# Run E2E tests (default: http://localhost:5001, DevHeader auth)
dotnet test tests/Lilia.Api.E2E
```

### Against remote API (Kinde auth)

```bash
# Set the target URL and auth mode
export E2E__ApiBaseUrl="https://editor.liliaeditor.com"
export E2E__AuthMode="Kinde"
export E2E__Kinde__ClientId="your-m2m-client-id"
export E2E__Kinde__ClientSecret="your-m2m-client-secret"
export E2E__Kinde__Audience="https://editor.liliaeditor.com"

dotnet test tests/Lilia.Api.E2E
```

## Auth Modes

| Mode | Use Case | How It Works |
|------|----------|--------------|
| `DevHeader` (default) | Local Development API (`Auth:Authority` unset) | No token: sends `X-Development-User-Id: <test user id>`. The API's DevelopmentAuthMiddleware authenticates the request as that user and UserSyncMiddleware creates the user row, so each test user (`e2e_owner_001` …) is its own account, isolated from the built-in dev user. Cannot make anonymous requests (a header-less request *is* the built-in dev user), so it refuses them. |
| `DevJwt` | Stale — a local dev API answers 401 | Generates self-signed JWTs with test user claims |
| `Kinde` | Remote/production-like | Uses Kinde M2M client credentials to get real tokens |
| `StaticToken` | Read-heavy runs with a browser token | Sends `E2E__StaticToken` as the bearer for every user |

## LaTeX feature coverage

`Tests/LatexCoverage/LatexFeatureCoverageTests` imports every feature of
`Fixtures/latex-features.json` (shared with the web UI suite) through the
product's .tex import, and checks five layers per feature: `api.import`,
`api.export`, `api.compile` (local `pdflatex`), `api.roundtrip` and `api.typst`
(the server-side Typst path). Known failures live in
`Fixtures/latex-known-gaps.json` and are strict both ways: a listed layer that
starts passing fails the test until it is removed from the list.

```bash
export PATH=$HOME/texlive/current/bin/x86_64-linux:$PATH   # pdflatex; absent → compile layer is "skip"
LATEX_COVERAGE_OUT=/tmp/api-results.jsonl \
E2E__ApiBaseUrl=http://localhost:5019 \
  dotnet test tests/Lilia.Api.E2E --filter "FullyQualifiedName~LatexFeatureCoverageTests"
```

`LATEX_COVERAGE_OUT` gets one JSON line per feature × layer:
`{"suite":"api","feature":"…","layer":"api.import","status":"pass|fail|known-gap|skip","detail":"…"}`.

## Test Users

Configured in `appsettings.e2e.json` under `TestUsers`:

- **Owner** — Creates and owns documents
- **Collaborator** — Has write access to shared docs
- **Viewer** — Read-only access
- **Anonymous** — No authentication

## CI Setup

Set these GitHub Actions secrets:
- `E2E_API_BASE_URL` — Target API URL
- `E2E_KINDE_CLIENT_ID` — Kinde M2M app client ID
- `E2E_KINDE_CLIENT_SECRET` — Kinde M2M app client secret
- `E2E_KINDE_AUDIENCE` — Kinde API audience

### Creating the Kinde M2M Application

1. Go to Kinde → Settings → Applications → Add Application
2. Choose "Machine to Machine" type
3. Grant it access to the Lilia API
4. Copy the Client ID and Secret to GitHub secrets
