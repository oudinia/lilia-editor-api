# Teams slice

First vertical slice of the Lilia API. Owns everything Teams: REST surface,
codename generation, default-team minting on user registration, and (later)
membership / sharing flows.

## Layout

```
Features/Teams/
├── Controllers/        REST entry — TeamsController routes /api/teams/*
├── Services/           Slice-internal services (codename generator, etc.)
├── Dtos/               Request/response shapes specific to this slice
├── Handlers/           Wolverine subscribers (UserCreatedEvent → mint default team)
└── README.md           ← you are here
```

`Lilia.Core.Entities.Team` and `Lilia.Core.Entities.TeamMember` stay in the
shared Core project — entities are persistence concerns owned by EF, not
the slice. The slice owns *behavior* on top of those entities.

## Boundaries

* **Inbound:** HTTP via `TeamsController`; events via `Handlers/`.
* **Outbound:** direct DI for queries (`IUserService`, `IEmailService`,
  `LiliaDbContext`); Wolverine for cross-slice fan-out (none yet, but
  e.g. a future `TeamCreatedEvent` would live in
  `Lilia.Api/Events/Teams/`).
* Other slices may **not** reach into `Features/Teams/Services/*` —
  consume the controller endpoints or subscribe to a published event.

## Why a slice?

Pilot for the modular-monolith move (decision 2026-05-15). Goal: own a
feature end-to-end in one folder so v2-of-Teams can ship by editing a
single subtree. See `lilia-docs/decisions/2026-05-15-vertical-slices.md`
once written.
