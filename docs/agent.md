# Host agent API and metrics

**Status:** Implemented
**Last Updated:** 2026-11-14
**Audience:** Operators, contributors, and agents
**Last verified:** 2026-11-14 against 95892be
**Code:** `src/HearthGate.Agent/Program.cs`, `src/HearthGate.Agent/AgentConfig.cs`, `src/HearthGate.Agent/HostReader.cs`, `src/HearthGate.Agent/ContainerStatsReader.cs`, `src/HearthGate.Agent/DockerHttp.cs`, `src/HearthGate.Contracts/AgentContracts.cs`

## Startup and API protection

The agent loads configuration once and requires a token with at least 24 characters.
It reads the variable named by `tokenEnv`; on Windows it also tries the user environment.
Missing or short tokens stop startup with an error.
Every request passes the optional IP or CIDR allow-list, then a constant-time bearer-token comparison.
Unknown or forbidden clients receive 403. Wrong tokens receive 401 after a 200-millisecond delay.
The default listen address binds all interfaces. Private-network exposure is an operator responsibility, not an enforced binding.

| Endpoint | Owner and result |
|---|---|
| `GET /v1/host` | `HostReader.Read`: host and container snapshot |
| `GET /v1/games` | `GameController.All`: configured game status and command metadata |
| `POST /v1/games/{key}/{action}` | `GameController.Act`: allowed start, stop, or restart |
| `POST /v1/games/{key}/commands/{id}` | Catalog lookup and command execution; hidden commands are not public |

Action and command failures use HTTP 409. Unknown actions, games, and commands use 404.
See [game control](games.md) for the command contract and lifecycle.

## Host CPU, memory, disks, and uptime

`HostReader` samples CPU over 500 milliseconds.
Linux reads `/proc/stat` and `/proc/meminfo`; Windows uses native system calls.
Unsupported or unavailable CPU data can be null.
Memory uses host data when available, with a garbage-collector memory fallback.
Uptime uses `Environment.TickCount64`.

An explicit `disks` list selects matching mounted drive names.
With an empty list, Windows reports fixed drives and Linux reports the root fixed drive only.
To report another disk from a container, mount it read-only and include the mount path.
The [Compose example](../deploy/docker-compose.example.yml) shows that setup.

## Docker transports and container inventory

`DockerHttp` accepts HTTP endpoints, Windows named pipes, and Unix sockets.
Use a read-only socket proxy for the Linux deployment shown in Compose.
The transport itself does not enforce read-only access.
Docker start and stop need write access; ordinary inventory and statistics use reads.

`HostReader` lists all containers, including stopped ones.
It reports names, images, state, status, health, published ports, Compose project labels, and available statistics.
Only published ports appear. Duplicate IPv4 and IPv6 entries are combined.
A failed inventory request produces `dockerError: "Docker is not reachable."`.
Individual statistics failures can omit statistics without failing the inventory.

## Container resource statistics

`ContainerStatsReader` owns previous samples per running container.
It reads one-shot stats with at most eight concurrent requests and an eight-second per-request timeout.
The first sample supplies memory; CPU and rates need a later sample.
CPU is the fraction of the whole host, not a per-core percentage.
Memory excludes inactive page cache when the Docker API supplies it.
Network and disk rates are bytes per second between samples.
Samples for containers no longer in the running list are removed.
The server polls the agent; see [agent caches](status.md).
