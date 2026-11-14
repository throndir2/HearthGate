# HearthGate knowledge base

**Status:** Implemented
**Last Updated:** 2026-11-14
**Audience:** Contributors and agents
**Last verified:** 2026-11-14 against f3c01d5
**Code:** `src/HearthGate.Server/Program.cs`, `src/HearthGate.Agent/Program.cs`, `web/src/main.tsx`

Use this map before you read source files. The pages describe the current code, not an intended design.

## Pages

- [Knowledge base contract](knowledge-base.md): How do agents search, verify, and maintain the documentation?
- [Server and API](server.md): Where do requests, database records, and admin operations live?
- [Accounts and security](accounts.md): How do sign-in, permissions, OIDC, and request protection work?
- [Status and host probes](status.md): How do background checks, history, and agent caches work?
- [GitHub showcase](github.md): Which repositories appear, and how does the cache refresh?
- [Configuration](configuration.md): Which YAML keys and environment variables control the product?
- [Host agent](agent.md): How does the agent read metrics and protect its API?
- [Game control and commands](games.md): How do lifecycle actions, profiles, channels, and stop steps work?
- [Browser UI](web.md): Where do routes, session state, polling, and UI controls live?
- [Development and packaging](development.md): How do local builds, container packaging, and the Windows installer work?
- [Search evaluation questions](repo-kb-eval.json): Which real repository questions measure KB retrieval quality?

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

## Other documentation

The root [README](../README.md) is the product overview.
The [agent bootstrap](../AGENTS.md) requires KB-first source navigation and same-PR documentation updates.
The checked-in [.github/copilot-instructions.md](../.github/copilot-instructions.md) contains repository working rules.
Examples live in `config` and `deploy`; they are configuration inputs, not generated KB pages.
