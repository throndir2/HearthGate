# HearthGate knowledge base

**Status:** Partial
**Last Updated:** 2026-11-14
**Audience:** Contributors and agents
**Last verified:** 2026-11-14 against 6a23bc5
**Code:** `src/HearthGate.Server/`, `src/HearthGate.Agent/`, `src/HearthGate.Contracts/`, `web/`, `config/`, `deploy/`, `Dockerfile`, `Dockerfile.agent`

Use this map before you read source files. The pages describe the current code, not an intended design.

## Pages

- [Knowledge base contract](knowledge-base.md): How do agents search, verify, and maintain the documentation?

## Code map

| Question | Main source area |
|---|---|
| How does the server start and route requests? | `src/HearthGate.Server/Program.cs`, `src/HearthGate.Server/Endpoints/` |
| How do accounts, permissions, and database records work? | `src/HearthGate.Server/Auth/`, `src/HearthGate.Server/Data/` |
| How do service checks and host probes work? | `src/HearthGate.Server/Status/`, `src/HearthGate.Server/Agents/` |
| How does the GitHub showcase work? | `src/HearthGate.Server/GitHub/` |
| How does the server load YAML configuration? | `src/HearthGate.Server/Config/`, `config/` |
| How does the agent read metrics and control games? | `src/HearthGate.Agent/`, `src/HearthGate.Contracts/` |
| How do game commands and profiles work? | `src/HearthGate.Agent/GameCommandCatalog.cs`, `src/HearthGate.Agent/GameCommandRunner.cs`, `src/HearthGate.Agent/GameProfiles/` |
| How does the browser display the site? | `web/src/` |
| How are containers and the Windows agent packaged? | `Dockerfile`, `Dockerfile.agent`, `deploy/` |

## Planned

The documentation audit will add verified pages for these areas and replace stale claims in the root README.
