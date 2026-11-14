# Service status, host probes, and agent caches

**Status:** Implemented
**Last Updated:** 2026-11-14
**Audience:** Operators, contributors, and agents
**Last verified:** 2026-11-14 against 95892be
**Code:** `src/HearthGate.Server/Status/`, `src/HearthGate.Server/Agents/`, `src/HearthGate.Server/Endpoints/PublicEndpoints.cs`, `src/HearthGate.Server/Endpoints/OperationsEndpoints.cs`, `src/HearthGate.Server/Config/HearthGateConfig.cs`

## Service checks and status transitions

`StatusMonitor` runs HTTP or TCP checks from configured targets in a background loop.
Public service requests read `StatusStore`; they cannot supply probe targets or start checks.
Without a `check` object, a service with a URL gets an HTTP check.
`check.type: none` disables monitoring. `check.target` overrides the service URL.

The loop wakes every 5 seconds. Each service interval is at least 15 seconds; the default is 60.
Timeouts are clamped to 1 through 60 seconds; the default is 8.
HTTP status 200 through 399 is accepted unless `expectedStatus` supplies an explicit list.
An unexpected HTTP status or response slower than `slowMs` is degraded.
Network failures become down after `failuresBeforeDown` consecutive failures; the default is 2.
Earlier failures are degraded. A later successful check resets the failure count.
States are `up`, `degraded`, `down`, and `unknown`.

## History, uptime, and retention

`StatusStore` keeps at most 288 samples per service. The details API returns the last 60.
Startup reads database samples from the last day, then seeds the bounded memory history.
`uptime24h` is the percentage of known retained samples that are not down.
Degraded samples count as available; unknown samples are excluded.
This is sample-based uptime, not a guarantee of complete 24-hour coverage.

The monitor persists samples to SQLite.
Hourly pruning removes status samples older than 7 days and audit events older than 180 days.
Guests get status and last-check time. Only `status.details` adds error text, latency, changes, uptime, and history.
Overall status prefers down, then degraded, then up; an empty or entirely unknown set is unknown.

## Host network reachability

`HostProbeService` checks hosts that have `address`.
It runs ping and distinct configured TCP ports, then waits 30 seconds.
Ping has a 2-second timeout. Each TCP connection has a 3-second timeout.
A successful ping or any open port makes a host reachable.
Without a usable check, reachability can be unknown rather than false.
`HostProbeStore` holds the last result and the time when reachability changed.
Only the hosts API exposes it, and that API requires `hosts.view`.

## Agent polling and stale snapshots

`AgentClient` builds requests from `hosts[].agentUrl` and the token named by `tokenEnv`.
`AgentPoller` reads `/v1/host`; it also reads `/v1/games` when the host has configured games.
After a round, it waits up to 20 seconds. A game lifecycle action can wake it early.
`AgentCache` owns host and game snapshots and last-failure times.
Game cache keys are `hostId/agentKey`.

Game snapshots older than 2 minutes have unknown state and no player or version details.
Host snapshots older than 2 minutes make `online` false, but the response still includes the last snapshot.
The hosts API also returns configured network addresses, link bases, and extra links.
Link values beginning with `:` or `/` are combined with `linkAddress` when it is set.
See [host metrics](agent.md) and [game actions](games.md) for agent-side behavior.
