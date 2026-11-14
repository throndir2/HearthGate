# Accounts, permissions, and security

**Status:** Implemented
**Last Updated:** 2026-11-14
**Audience:** Operators, contributors, and agents
**Last verified:** 2026-11-14 against 95892be
**Code:** `src/HearthGate.Server/Auth/AccountService.cs`, `src/HearthGate.Server/Auth/UserContext.cs`, `src/HearthGate.Server/Endpoints/AccountEndpoints.cs`, `src/HearthGate.Server/Endpoints/OidcEndpoints.cs`, `src/HearthGate.Server/Endpoints/AdminEndpoints.cs`, `src/HearthGate.Server/Security/`, `src/HearthGate.Server/Program.cs`, `web/src/pow.worker.ts`, `web/src/pages/RequestAccess.tsx`

## Local sign-in and password setup

`AccountService` owns password validation, hashing, lockout, setup tokens, and sign-in cookies.
Usernames have 3 to 32 ASCII letters, digits, dots, underscores, or hyphens.
Passwords have 10 to 256 characters. The Identity password hasher uses 600,000 iterations.
Five failed sign-in attempts lock the account for 15 minutes.
Unknown users and wrong passwords receive the same public failure response.

Password setup tokens contain 32 random bytes. The database stores their SHA-256 hashes.
Tokens expire after 72 hours and work once. Creating a new token invalidates older unused tokens for that user.
Profile password changes rotate the security stamp and issue a new cookie to the current session.

## Two-step sign-in and session revocation

TOTP means time-based one-time password. Users configure it in Profile with a locally generated QR code.
Verification accepts the current, previous, or next step, but rejects a step already used by the account.
Turning TOTP on or off rotates the security stamp. Admin TOTP reset and session revocation also rotate it.
Disabling a user rotates the stamp.

`UserContextMiddleware` checks the user, disabled flag, and stamp.
It caches the derived user context for 15 seconds, so revocation and permission changes are not immediate.
Sign-out clears the current cookie. There is no separate self-service "sign out everywhere" endpoint.
An admin can revoke a user's sessions through `/api/admin/users/{id}/revoke-sessions`.

## Permission and visibility rules

`UserContext.Has` and `UserContext.CanSee` are the server-side sources of permission decisions.
An active admin has all permissions. Other users get the grants of their groups.
Visibility is `public`, `users` for any signed-in user, or `admins` for active admins.
`services.private` exists in the catalog, but service visibility uses sign-in and admin status, not that grant.

| Permission | Current use |
|---|---|
| `status.details` | Service latency, errors, uptime, and history |
| `games.view` | Game players, version, and configured connection information |
| `hosts.view` | Host addresses, probes, metrics, and containers |
| `games.control:<id>` or `games.control:*` | Game start, stop, restart, and control-level commands |
| `games.admin:<id>` or `games.admin:*` | Admin-level commands; also grants control for the same game |

Game and service APIs filter visibility on the server. UI controls do not replace API checks.
Guest service data includes identifiers, public display fields, a status, and `lastChecked`; detailed data is null.
Guest game data includes a coarse state and update time.
If `connectInfoPublic` is true, the configured game connection text is also public.

## Optional OIDC sign-in and linking

OpenID Connect (OIDC) supplies external sign-in; it does not replace the local permission system.
The server registers it at startup when enabled with a nonempty authority.
It uses the authorization code flow, PKCE, and `/signin-oidc` as the provider callback.
Profile linking begins with an antiforgery-protected form POST.
The completion endpoint stores the `sub` claim's issuer and value as the provider identity.
The database makes the issuer-subject pair unique. Email is never an account merge key.

`autoProvision: none` requires a linked local account.
`pending` can create an account request when account requests are enabled.
`approved` creates a local account and adds Members when present.
`groupMap` adds or removes mapped local groups at provider sign-in; unmapped groups are not managed by it.
Unlinking requires a remaining password or another external login.
There is no password import or cross-application account migration feature.

## Cookies, headers, and request limits

Production authentication cookies use the `__Host-` prefix, Secure, and HttpOnly.
The main cookie uses SameSite Lax. The antiforgery cookie uses SameSite Strict.
`HG_INSECURE_COOKIES=true` removes the prefix and permits local HTTP; do not use it for public hosting.
Trusted proxies and OIDC registration are startup settings; see [configuration](configuration.md).

The global fixed-window rate limit is 300 requests per client IP per minute.
The auth policy is 10 per minute. Account requests allow 3 per 15 minutes.
Proof-of-work challenges allow 30 per 10 minutes. Game actions allow 20 per user per minute.
The server sets content, framing, referrer, and permissions headers. HTTPS responses include HSTS.
The content policy permits local scripts and fonts, plus data images for QR codes.

## Account request proof of work

The account form has a honeypot and a local worker that solves a SHA-256 challenge.
`ProofOfWorkService` signs each challenge, sets a 10-minute expiry, and clamps difficulty to 8 through 24 bits.
It rejects used challenges through an in-memory cache. The signing key changes when the server restarts.
The form accepts at most 200 pending requests. Duplicate usernames receive the same success response as new requests.
No third-party CAPTCHA or email delivery service is implemented.
