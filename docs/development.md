# Local development and packaging

**Status:** Implemented
**Last Updated:** 2026-11-14
**Audience:** Operators, contributors, and agents
**Last verified:** 2026-11-14 against 95892be
**Code:** `HearthGate.slnx`, `src/HearthGate.Server/HearthGate.Server.csproj`, `src/HearthGate.Agent/HearthGate.Agent.csproj`, `src/HearthGate.Contracts/HearthGate.Contracts.csproj`, `src/HearthGate.Server/Properties/`, `src/HearthGate.Agent/Properties/`, `src/HearthGate.Server/appsettings.json`, `src/HearthGate.Agent/appsettings.json`, `web/package.json`, `web/package-lock.json`, `web/tsconfig.json`, `web/tsconfig.app.json`, `web/tsconfig.node.json`, `web/eslint.config.js`, `web/vite.config.ts`, `deploy/`, `Dockerfile`, `Dockerfile.agent`, `.dockerignore`, `.gitignore`, `.github/workflows/release.yml`, `scripts/kb/`

## Local build commands and tools

The solution contains Server, Agent, and Contracts projects targeting .NET 10.
The UI uses npm, TypeScript, and Vite. It has no repository test runner.
Use a Node.js version accepted by the installed Vite package; repo-kb itself needs Node.js 20 or later.

Run `npm run build` from `web` to compile TypeScript and write the browser build to server `wwwroot`.
Use `npm ci` only when dependencies need restoration. `npm run lint` runs ESLint.
Run `dotnet build HearthGate.slnx` from the repository root for a local solution build.
There is no development setup script and no checked-in local Git hook.
Documentation-only changes need a diff inspection, not a product build.
Run the [local KB check](knowledge-base.md#local-check) before merge.
Never dispatch hosted validation for this work.

## Development server and agent

Run the server project with `dotnet run --project src/HearthGate.Server --urls http://127.0.0.1:5080` from the repository root.
Set `HG_INSECURE_COOKIES=true` in that process environment for local HTTP.
Set `HG_CONFIG` and `HG_DATA` when you need isolated local configuration and data.
The first-admin setup flow is described in [server startup](server.md).
`npm run dev` from `web` starts Vite and proxies API requests to server port 5080.

Run the agent with `dotnet run --project src/HearthGate.Agent`.
Set a token of at least 24 characters in the variable named by `tokenEnv`.
Set `HG_AGENT_CONFIG` to the agent YAML path when it is not next to the binary.
Agent configuration and profile changes need a restart.
The database uses `EnsureCreated`; it does not apply schema migrations.
For schema work, use a disposable development database. Never delete a production database as a development step.

## Container images and Compose

`Dockerfile` builds the UI with Node 22, publishes the server with the .NET 10 SDK, and runs on the ASP.NET runtime image.
`Dockerfile.agent` publishes the agent and runs it on the ASP.NET runtime image.
Both use the non-root `app` user.
The server image sets `/data` for data and `/data/hearthgate.yaml` for configuration.
The agent image selects `/config/agent.yaml`.
Read-only root filesystems are Compose settings, not Dockerfile enforcement.

Compose includes the server, agent, and Docker socket proxy.
The server publishes container port 8080 through a configurable host binding and port.
The agent is not published through Compose. The proxy network is internal.
The server data mount is writable. The agent configuration mount is read-only.
Give the container user access to those files. A missing agent config uses defaults; an unreadable existing file fails startup.
The examples need deployment-specific edits; see [configuration](configuration.md).

## Windows installer options

`Install-HearthGateAgent.ps1` accepts required `-SourceDir`.
Options are `-Token`, `-AllowFrom`, `-InstallDir`, `-Port`, and `-ReplaceConfig`.
The default install folder is `C:\HearthGate\Agent`; the default firewall port is 7177.
The first install needs `-Token` unless the user variable `HG_AGENT_TOKEN` already exists.
The token must have at least 24 characters and is stored as a user environment variable, not a file.

The installer stops the existing task and matching installed agent process before copying files.
It preserves an existing `agent.yaml` unless `-ReplaceConfig` is set.
It registers the `HearthGate Agent` scheduled task at logon, as the signed-in user with limited rights.
It starts the task after registration.
The agent supports Windows service hosting, but this installer does not install a Windows service.
The win-x64 publish sets `WinExe`, so the task has no console window.

Firewall changes need administrator rights and can trigger a UAC prompt.
`-AllowFrom` restricts the firewall source; omitting it permits any source.
`-Port` changes the firewall rule only, not the YAML listen URL.
The agent's own `allowedClients` is a separate check.

## Release workflow

The existing release workflow runs only on manual dispatch or a `v*` tag push.
It publishes both container images with `latest` and a version tag.
Manual runs use a `dev-<sha>` image tag; they do not create the Windows ZIP or GitHub release.
Tag runs publish a self-contained win-x64 agent, include the example YAML and installer, and create a GitHub release.
No test or validation job exists.
Do not change or dispatch this workflow for KB work.
