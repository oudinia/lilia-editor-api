# Lilia API — E2E Tests

End-to-end tests that run against a live API instance (local or remote).

## Quick Start

### Against local API (dev mode, no auth validation)

```bash
# Start the API first
dotnet run --project src/Lilia.Api

# Run E2E tests (default: http://localhost:5001, DevJwt auth)
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
| `DevJwt` | Local dev, staging without auth | Generates self-signed JWTs with test user claims |
| `Kinde` | Remote/production-like | Uses Kinde M2M client credentials to get real tokens |

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
