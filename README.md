# Northwind Support Desk

A support ticket management system: raise tickets, search and filter them, assign them to agents,
and move them through their lifecycle.

**.NET 10 Web API** + **React 19 / TypeScript** + **SQL Server**, in one monorepo.

---

## The assignment

You are joining a team that already has this app working. Complete the tasks **in order**.
Full requirements are in **[`ASSIGNMENT_CANDIDATE.md`](ASSIGNMENT_CANDIDATE.md)**.

| # | Task | Done looks like | Effort |
| --- | --- | --- | --- |
| 1 | **Fix a defect, add server-side filtering** | Root cause fixed with a regression test; every list filter applied in the database with a correct count | ~2.5 h |
| 2 | **Ticket escalation & assignment** | Automatic triage on create, escalation with history, SLA status, UI for all of it | ~7 h |
| 3 | **Login and authentication** | Agents sign in; API rejects unauthenticated calls; login UI | ~4 h |
| 4 | **Dockerise the API** *(optional)* | `docker compose up` gives a migrated, seeded API with no baked-in secret | ~1.5 h |

### Guidance

- **Time: 2 days.** Honestly marking a task "not finished" beats rushing all of them.
- **Fork** this repo to your own GitHub account and work there. **Do not push to the original.**
- **AI tools are allowed.** You must understand and be able to defend every line you submit.
- **Commit as you go** and keep the real history. Commit the Task 1 defect fix and the filtering
  separately from each other and from the feature work. A single squashed commit is not accepted.
- **Where the spec is ambiguous**, make a decision, write it down in the README, and move on.
- **Out of scope:** registration, password reset, roles, OAuth/SSO, refresh tokens, MFA,
  notifications, CI, caching, visual redesign. Mention them under *Known limitations* instead.

### Submission

1. Update this **README** with what you built, your design decisions (chosen, rejected and why),
   the defect's root cause, dev login credentials, how to run and test it, AI usage, and known
   limitations. A reviewer must be able to run it without asking you anything.
2. Add an **APPROACH.md** explaining your approach and thought process for each task.
3. Commit both, then send us the **link to your fork**, the **dev login credentials**, and a
   **rough note of time spent** on what. (If the fork is private, add the reviewers as collaborators.)

---

## Quick start

**Prerequisites:** .NET SDK 10 (`global.json`), Node.js 22 (`.nvmrc`; 20.19+ works), Docker.

```bash
cp .env.example .env          # optional, every value has a working default
npm install                   # installs the web app
npm run db:up                 # SQL Server 2022 in Docker on localhost:1433
npm run api                   # terminal 1: migrates, seeds, then serves the API
npm run web                   # terminal 2: React dev server
```

| What | Where |
| --- | --- |
| Web app | http://localhost:5173 |
| API | https://localhost:7043 (also http://localhost:5043) |
| Swagger | https://localhost:7043/swagger |

In Development the API applies migrations and seeds demo data on startup. **Never commit real
credentials.**

**Visual Studio 2026:** run `npm run db:up`, open `SupportDesk.sln`, set
**SupportDesk.Presentation** as the startup project, and press F5 on the **https** profile.

## Tests

```bash
npm test                      # dotnet test, then the web tests (Vitest)
```

Both pass on a clean checkout and neither needs a database.

## Commands

| Command | What it does |
| --- | --- |
| `npm run db:up` / `db:down` / `db:reset` | Start / stop / wipe SQL Server |
| `npm run api` / `npm run web` | Run the API / the React app |
| `npm run build` | Build both |
| `npm run lint` / `typecheck` / `format` | Web lint and types / `dotnet format` |
| `npm run ef -- migrations add <Name>` | Add an EF Core migration |

## API at a glance

| Endpoint | Description |
| --- | --- |
| `GET /api/tickets` | Paged list with `search`, `status`, `priority`, `categoryId`, `customerId`, `assignedAgentId`, `unassignedOnly`, `slaStatus`, `sortBy`, `sortDirection`. |
| `GET /api/tickets/{id}` | One ticket |
| `POST /api/tickets` | Create a ticket |
| `POST /api/tickets/{id}/escalate` · `GET /api/tickets/{id}/escalations` | Escalate and inspect immutable escalation history |
| `PATCH /api/tickets/{id}/status` · `/assignment` | Change status / assignee |
| `GET /api/customers` · `/api/agents` · `/api/categories` | Reference data |
| `POST /api/auth/login` · `GET /api/auth/me` | Sign in and get the authenticated agent |

Errors are `ProblemDetails`: `400` validation, `404` not found, `409` conflict or business rule.
All API controller endpoints require a bearer token except login. Swagger remains publicly accessible
in Development.

**Seed data:** 5 categories, 6 customers (3 Premium), 6 agents (one inactive, one at their limit)
and 40 tickets across every status, priority and SLA state.

## Troubleshooting

| Symptom | Fix |
| --- | --- |
| API cannot reach the database | `npm run db:up`, wait ~20 s, check `docker compose ps` |
| Port 1433 already in use | Change the port in `docker-compose.yml` and `.env` |
| Browser warns about the certificate | `dotnet dev-certs https --trust`, or use the web app, which proxies |
| Web app shows "Unable to load tickets" | The API is not running, or `VITE_API_PROXY_TARGET` points at the wrong port |
| SQL Server slow on Apple Silicon | Enable Rosetta in Docker Desktop → Settings → General |

## Task1 : DEFECT-117: Ticket Filter Requests

**Root cause:** The ticket-list React effect depended on search and pagination fields but omitted the other filter fields. As a result, changing the status or another omitted filter did not trigger a new API request.

**Fix:** Update the effect dependencies so filter changes trigger requests while preserving the debounce for search input.

### Task 1.2 — Server-side ticket filtering

Implemented server-side filtering for ticket searches.

- Added exact-match filters for status, priority, category, customer, and assigned agent.
- Added support for retrieving unassigned tickets.
- Added search by ticket title, reference, and customer name, with whitespace trimming and blank-search handling.
- Combined supplied filters using AND conditions.
- Applied filtering in the database before counting and pagination to ensure accurate results.
- Organized filters in a dedicated method to make future filters easier to add.

**Regression tests:** Added tests for exact-match priority filtering, combined filters, and customer-name search.

### Task 2 — Ticket escalation and assignment

- Ticket creation applies configured priority, SLA-window and agent-assignment rules and returns
  the triage reasons. Escalation recomputes the due date, re-evaluates ownership and records an
  immutable before/after history row.
- `TicketEscalations.TicketId` has an index because ticket history is queried by ticket ID; the
  foreign key cascades when its parent ticket is deleted.
- SLA status is derived from the ticket dates. The list filter applies the selected state before
  SQL counting and paging.
- The web app displays the triage outcome, reusable SLA status indicator, escalation controls
  and escalation history.

### Task 3 — Agent authentication

- Agent credentials are stored as a nullable `PasswordHash` column on the existing `Agents`
  table because each login identity belongs to exactly one agent; the credential migration is
  `AddAgentPasswordHash`.
- Passwords use ASP.NET Core Identity's salted `PasswordHasher<Agent>`. Development seeding
  provisions every agent (including existing seeded databases that predate the column) with
  the local-only password `LocalDev123!`. For example, sign in as
  `alex.turner@northwind-support.example` / `LocalDev123!`. Inactive agents cannot sign in.
- `POST /api/auth/login` returns a signed JWT, expiry and agent identity. Unknown email and wrong
  password share the same 401 response. `GET /api/auth/me` returns identity claims.
- JWT signing key is configured under `Jwt:SigningKey` (override with
  `Jwt__SigningKey`); `.env.example` contains only an explicitly local development value.
  Access tokens last 60 minutes by default. Supply a managed secret and rotate it outside
  development.
- Escalation requests accept only a reason; `escalatedBy` is populated from the authenticated
  agent's token claims, not from client input.
- The web client provides `/login`, protects application routes, restores the intended route
  after sign-in, displays the signed-in agent and clears the session on logout or any API 401.
- The browser keeps the access token, expiry and agent display data in `localStorage` under
  `northwind-support-session`, and sends the token as a Bearer credential on API requests.
  This survives reloads but is readable by JavaScript, so an XSS flaw could expose the token.
  Keep the app free of untrusted scripts and use HTTPS outside local development.

**Known limitations:** There are no refresh tokens or automatic token renewal, roles or
permissions, account lockout, or rate limiting. Agents sign in again after the 60-minute token
expires; any API 401 clears the local session and sends them to sign in again.
