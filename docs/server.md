# Server and API map

**Status:** Implemented
**Last Updated:** 2026-11-14
**Audience:** Contributors and agents
**Last verified:** 2026-11-14 against 95892be
**Code:** `src/HearthGate.Server/Program.cs`, `src/HearthGate.Server/Endpoints/PublicEndpoints.cs`, `src/HearthGate.Server/Endpoints/AccountEndpoints.cs`, `src/HearthGate.Server/Endpoints/AdminEndpoints.cs`, `src/HearthGate.Server/Endpoints/OperationsEndpoints.cs`, `src/HearthGate.Server/Endpoints/OidcEndpoints.cs`, `src/HearthGate.Server/Data/`, `src/HearthGate.Server/Auth/AuditService.cs`, `src/HearthGate.Contracts/`

## Startup and request routing

`Program.cs` registers configuration, database services, authentication, HTTP clients, and background services.
`Bootstrap.Run` creates the database, missing groups, and the first admin before request handling starts.
The middleware applies forwarded headers, response headers, static files, authentication, user context, rate limits, and antiforgery checks.
State-changing `/api` requests require antiforgery validation. GET, HEAD, and OPTIONS are excluded.

The server serves the browser build from `wwwroot` and user icons from the data folder's `icons` directory.
`/healthz` returns `ok`. Unknown `/api` paths return 404. Other paths fall back to `index.html`.
API responses use `Cache-Control: no-store`. Built assets use a one-year immutable cache; other static files use `no-cache`.

## Endpoint owners

| Owner | API area and question |
|---|---|
| `PublicEndpoints` | `/api/site`, `/api/session`, `/api/services`, `/api/projects`, `/api/pow`: What can the browser read? |
| `AccountEndpoints` | `/api/auth`, `/api/account-requests`, `/api/me`: How do users sign in and manage their profiles? |
| `AdminEndpoints` | `/api/admin`: How does an active admin manage requests, users, groups, audit records, and configuration views? |
| `OperationsEndpoints` | `/api/games`, `/api/hosts`: Which game actions and host data can this user access? |
| `OidcEndpoints` | `/api/auth/oidc`: How does optional provider sign-in and account linking work? |

See [accounts](accounts.md), [status](status.md), [GitHub](github.md), and [games](games.md) for the behavior of these endpoints.

## Database and first admin

`AppDb` owns users, groups, user-group membership, account requests, setup tokens, external logins, audit events, and status samples.
SQLite uses `hearthgate.db` in `HG_DATA`, or `.hgdata` under the server content root when `HG_DATA` is absent.
Data-protection keys persist in the `keys` subfolder.
`EnsureCreatedAsync` creates the schema. The repository has no database migration scripts.
Date-time values use `DateTimeOffsetToBinaryConverter` for SQLite comparisons and ordering.

`Bootstrap` adds configured groups only when their names are absent. It does not update existing groups from YAML.
With no group seeds, it adds Members, Trusted, and Game hosts.
It creates the first admin only when there are no users.
`HG_ADMIN_USER` defaults to `admin`. A valid `HG_ADMIN_PASSWORD` sets the first password.
Otherwise, `first-admin-link.txt` contains a relative one-use setup link that expires after 72 hours.

## Admin operations and audit

The admin endpoint filter requires a signed-in active admin.
When `auth.requireAdminTotp` is true, an admin without two-step sign-in gets 403.
Approval creates a user, adds Members if that group exists, and returns a password setup path.
The server does not email the path. The admin must give it to the user.

Admins can update user roles, disable users, set group membership, make reset links, revoke sessions, and reset another user's TOTP.
An admin cannot disable or remove their own admin access through the user update endpoint.
Group permissions use the catalog plus per-game control and admin grants.
The audit endpoint returns 100 records at a time and accepts `before` for older records.
`AuditService` records the action, actor, target, detail, client IP, and result.

## Shared formats

`src/HearthGate.Contracts/AgentContracts.cs` defines the server-agent wire records.
It includes host snapshots, container statistics, game status, action results, command metadata, and command parameters.
Command templates, channel details, and passwords remain on the agent.
Browser response types are declared separately in `web/src/api.ts`; see the [UI page](web.md).
