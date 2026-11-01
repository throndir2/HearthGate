# HearthGate agent instructions

HearthGate is a self-hosted home page: a public directory, status and GitHub showcase for guests, and accounts, host health and game-server control for approved users. Work at prototype speed: smallest change that works, a quick local check, then deliver.

## Repository layout

- `src/HearthGate.Server/` - ASP.NET Core (.NET 10) API, background status checks, auth, admin. Features live in `Endpoints/`, `Auth/`, `Status/`, `GitHub/`, `Agents/`, `Security/`, `Data/`.
- `src/HearthGate.Agent/` - host agent (Linux container or Windows service): host metrics, Docker via a read-only socket proxy, allow-listed game-server control.
- `src/HearthGate.Contracts/` - DTOs shared by the server and the agent.
- `web/` - React + TypeScript + Vite + Tailwind UI. `npm run build` writes to `src/HearthGate.Server/wwwroot` (git-ignored).
- `config/` - example `hearthgate.yaml` and `agent.yaml`. `deploy/` - compose example and the Windows agent installer.
- `Dockerfile`, `Dockerfile.agent`, `.github/workflows/release.yml` (manual or `v*` tag only).

## Build and check (local only)

```powershell
cd web; npm ci; npm run build; cd ..
dotnet build HearthGate.slnx
docker build -t hearthgate:local -f Dockerfile .    # when packaging changes
```

Run locally with `HG_INSECURE_COOKIES=true` (see README "Development").

## Rules

- Everything about a deployment (services, hosts, game servers, branding, GitHub owner) is configuration in YAML, never code. Secrets come only from environment variables named in the config (`*Env`). Never put secrets, passwords, tokens or private LAN configs in the repo.
- The guest surface must stay minimal: guest API responses never include internal hosts, IPs, ports, versions or error text. Every endpoint checks permissions on the server.
- Write user-facing text (UI, docs, summaries) in ASD-STE100 Simplified Technical English.

## Delivery (auto-merge)

- Every implementation task delivers to remote `main` by default, not only a local commit or a task branch.
- After a quick local check (build, plus a direct check of the changed behavior), commit to the task branch, open or update a PR, and merge it into `main` without asking. Resolve merge conflicts and actionable review feedback first.
- Fetch `origin/main` and integrate it in the task worktree. Never touch the shared main checkout. Never force-push or overwrite concurrent work.
- Before each push or PR operation, check every workflow trigger in `.github/workflows/`: nothing may start GitHub Actions. Only the manual or `v*` tag release workflow is allowed. Never add PR, push, or scheduled workflows, and never run tests or checks on Actions.
- A merge does not authorize a Nahida deployment or NPM changes. Do those only when the user asks.
- Explicit local-only, draft, review-only or hold requests override auto-merge.
