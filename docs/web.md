# Browser UI routes and state

**Status:** Implemented
**Last Updated:** 2026-11-14
**Audience:** Contributors and agents
**Last verified:** 2026-11-14 against 95892be
**Code:** `web/src/`, `web/package.json`, `web/vite.config.ts`, `web/index.html`

## Routes and page owners

`main.tsx` creates the React root, router, session provider, and Motion reduced-motion setting.
`Layout` owns navigation, the mobile menu, theme choice, footer, and page transitions.
The theme defaults to dark and persists as `hg.theme` in local storage.
CSS also reduces animations when the user requests reduced motion.
Inter fonts are bundled through the package import.

| Route | Page and question |
|---|---|
| `/` | `Home`: Where are visible services, categories, game cards, and up to six projects? |
| `/status` | `StatusPage`: Which service status and permitted details can the user see? |
| `/projects` | `Projects`: Which public repositories appear in the showcase? |
| `/signin` | `SignIn`: How does the user sign in locally or through OIDC? |
| `/request-access` | `RequestAccess`: How does a guest request an account? |
| `/set-password` | `SetPassword`: How is a setup link used? |
| `/profile` | `Profile`: How does the user change passwords, TOTP, and linked accounts? |
| `/games` | `Games`: Where are lifecycle buttons and configured command forms? |
| `/hosts` | `Hosts`: Where are reachability, metrics, and container data displayed? |
| `/admin` | `Admin`: Where are account requests, users, groups, and audit controls? |

Profile, Games, Hosts, and Admin are lazy-loaded.
Unknown browser routes show a 404 page.
Navigation depends on site feature flags and user permissions.
The server enforces access even when someone opens a hidden route directly.

## Session state and API requests

`SessionProvider` owns site settings and current user state.
Startup reads `/api/site` and `/api/session`.
`refresh` reads the session again after sign-in, sign-out, or profile changes.
The API module owns the antiforgery token and adds `X-CSRF-TOKEN` to non-GET requests.
Requests use same-origin credentials. Non-success responses throw `ApiError`.
The Profile OIDC link uses a real form POST with the antiforgery field because the server redirects to the provider.

`session.can` supplies UI permission checks and mirrors wildcard game grants.
It is not an authorization boundary. See [accounts](accounts.md) for server-side rules.

## Polling and page-local state

`useServices` loads immediately and polls every 30 seconds by default.
`usePoll` loads immediately, supports explicit reload, and defaults to 15 seconds.
Interval polls run only while the document is visible.
Cleanup clears the interval and prevents old responses from changing unmounted page state.
Games polls every 10 seconds. Hosts polls every 15 seconds.
Home uses a 30-second game poll.
Projects loads once per enabled hook; it does not have interval polling.

Home owns service search and category selection.
Games owns pending confirmation, busy state, and command dialog selection.
`GameCommandDialog` makes forms from the command parameter metadata.
Hosts owns container filtering, problem-only filtering, and sorting by name, CPU, memory, or network and disk activity.
Host port links exist for running containers with published TCP ports and a configured link address.
Ports 443 and 8443 use HTTPS in those links.

## Browser build

`npm run build` runs TypeScript project compilation, then Vite.
Vite writes to `src/HearthGate.Server/wwwroot` and clears that output folder during the build.
The development proxy forwards `/api`, `/icons`, and `/signin-oidc` to server port 5080.
The browser types in `api.ts` are separate from the .NET wire records.
See [development](development.md) for tools and packaging.
