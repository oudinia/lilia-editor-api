# API smoke testing — local against production DB

**Purpose.** Boot the API locally pointed at the production Postgres so you can hit endpoints with curl and see real data. Catches bugs that integration tests miss because they don't see prod's data shape.

**Risk model.** Read-only against prod is safe. Writes are NOT safe — they corrupt production data. The smoke checklist below is read-only by construction. Don't `POST` against this setup unless you've audited the endpoint.

## Prerequisites

- `.dotnet/` toolchain at `~/.dotnet` (the project's pinned version).
- `scripts/backup-db.env` with prod DB credentials (already in repo).
- A fresh Kinde JWT for endpoints behind `[Authorize]` — open https://editor.liliaeditor.com in a browser, DevTools → Network → click any API call → copy `Authorization: Bearer <token>` from the request headers. Tokens expire after ~1h.
- Port `5001` may already be in use by another local dev instance. Use `5002` for prod-pointed runs.

## Boot the API

From the repo root:

```bash
source scripts/backup-db.env
export ConnectionStrings__LiliaCore="Host=$DB_HOST;Port=$DB_PORT;Database=$DB_NAME;Username=$DB_USER;Password=$DB_PASSWORD;SSL Mode=Require;Trust Server Certificate=true"
export Auth__Authority="https://liliaeditor.kinde.com"     # disables DevAuthMiddleware so it doesn't try to upsert a fake user
export ASPNETCORE_URLS="http://localhost:5002"
export ASPNETCORE_ENVIRONMENT="Development"
/home/oussama/.dotnet/dotnet run --project src/Lilia.Api --no-launch-profile --urls http://localhost:5002
```

Wait for `Now listening on: http://localhost:5002`.

**Critical:** the `Auth__Authority` export above is what disables `DevelopmentAuthMiddleware`. Without it, every request triggers a fake-user upsert that hits a unique-constraint violation in prod. If you forget this and see 500s with `users_email_key`, that's why.

## Smoke checklist

### Anonymous endpoints

```bash
# Health
curl -s http://localhost:5002/health | jq

# Block types catalog (anonymous reads)
curl -s http://localhost:5002/api/blocktypes | jq '.[] | .type'
```

Expected: `health` returns `{ "status": "healthy", "database": "connected", ... }`. Block types returns the canonical list.

### Authenticated endpoints — read only

```bash
TOKEN="<paste your fresh Kinde JWT here>"

# List the user's documents — finds a docId for further probes.
curl -s -H "Authorization: Bearer $TOKEN" \
  "http://localhost:5002/api/documents?limit=3" | jq '.items[].id, .items[].title'

# Per-document insertion catalog (kernel + installed-package tokens)
DOC_ID="<paste a docId from the previous call>"
curl -s -H "Authorization: Bearer $TOKEN" \
  "http://localhost:5002/api/lilia/insertions?docId=$DOC_ID" | jq 'length, .[0:3]'

# Insertion telemetry stats — top tokens
curl -s -H "Authorization: Bearer $TOKEN" \
  "http://localhost:5002/api/lilia/insertions/stats/top?windowDays=30&limit=5" | jq

# Insertion telemetry stats — source mix (panel/palette/slash/package-modal)
curl -s -H "Authorization: Bearer $TOKEN" \
  "http://localhost:5002/api/lilia/insertions/stats/sources?windowDays=30" | jq
```

Expected: all return 200 with JSON arrays / objects. If any return 500, check the API console output for the stack — that's a real prod bug.

### Endpoints that MUST NOT be smoke-tested against prod

- `POST /api/lilia/insertions/events` — would inject fake telemetry events into the prod table.
- `POST /api/webhooks/kinde` — would dispatch a real welcome email if you craft a `user.created` payload.
- Any `POST` / `PUT` / `DELETE` / `PATCH` endpoint, full stop. Use Testcontainers integration tests for these.

## When you find a 500

1. **Check the API console** for the exception. `EF Core LINQ translation` errors are common and indicate prod-shape bugs that don't reproduce against an empty Testcontainers DB.
2. **Open or reuse a Jira ticket** — usually a one-line LINQ rewrite to push the projection client-side.
3. **Add a regression test** in `tests/Lilia.Api.Tests/Integration/Controllers/` that proves the fix.
4. **Ship the fix in the PR alongside the test.**

Example bug found 2026-05-06 by this exact procedure: `InsertionEventsController.GetTopTokens` and `GetSourceMix` had `g.Select(x => x.UserId).Distinct().Count()` inside a `GroupBy.Select`, which Npgsql can't translate. Fixed by projecting (token, userId) tuples then grouping in memory. See LILIA-125 for the full story.

## Stopping

Ctrl+C in the API terminal, or `pkill -f "Lilia.Api.*5002"`.

## Troubleshooting

- **`duplicate key value violates unique constraint "users_email_key"`** — `Auth__Authority` not exported. DevAuthMiddleware is trying to upsert a dev user. Re-export and restart.
- **`Address already in use`** — another local API instance is on the same port. Pick a different port via `ASPNETCORE_URLS=http://localhost:5003`.
- **`could not be translated. Either rewrite the query in a form that can be translated, or switch to client evaluation`** — EF Core LINQ translation failure. Real bug; add to Jira and fix.
- **All endpoints return 401** — Kinde token expired (1h TTL). Grab a fresh one from the browser.
