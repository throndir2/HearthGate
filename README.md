# HearthGate

**Status:** Implemented
**Last Updated:** 2026-11-14
**Audience:** Self-hosted operators and contributors
**Last verified:** 2026-11-14 against acd55ed
**Code:** `src/HearthGate.Server/Program.cs`, `src/HearthGate.Agent/Program.cs`, `web/src/main.tsx`, `config/`, `deploy/`, `Dockerfile`, `Dockerfile.agent`, `.github/workflows/release.yml`

HearthGate is a self-hosted home page for services, status, and public GitHub projects.
Guests see public cards and limited status data. Approved users get the permissions assigned to their groups.
An optional host agent reads metrics and controls configured game servers.
Optional OpenID Connect (OIDC) sign-in links a provider identity to a local account.

Use the [knowledge base map](docs/README.md) to find current behavior and its source code.
Use the [KB contract](docs/knowledge-base.md) to search and maintain these pages.

## Components

| Component | Current implementation |
|---|---|
| Server | ASP.NET Core on .NET 10; Entity Framework Core and SQLite |
| Browser UI | React 19, TypeScript, Vite, Tailwind CSS, and Motion |
| Host agent | .NET 10 HTTP API; Linux container or Windows process |
| Packaging | Server image, agent image, and tagged-release Windows agent ZIP |

## Configuration and deployment

Start with [server configuration](config/hearthgate.example.yaml), [agent configuration](config/agent.example.yaml), and the [Compose example](deploy/docker-compose.example.yml).
Edit these examples for your deployment. They are not private deployment files.
Some existing examples contain deployment-specific values; replace those values before use.
Keep secrets in environment variables. YAML fields ending in `Env` name those variables.
Game command passwords can also come from an existing game settings file.

The server container listens on port 8080. Put a reverse proxy with HTTPS in front of it.
The agent listens on port 7177 by default. Keep the agent on a private network.
The Compose example uses a Docker socket proxy for container reads.
Docker game control needs write access through that proxy; ordinary host monitoring does not.

The [Windows installer](deploy/windows/Install-HearthGateAgent.ps1) runs the agent as the signed-in user from a logon task.
It keeps an existing `agent.yaml` unless `-ReplaceConfig` is set.
It can change the firewall, which requires administrator rights.

## Development

Use the .NET 10 SDK and a Node.js version accepted by the installed Vite package.
Build the UI from `web` with `npm run build`. Vite writes the result to `src/HearthGate.Server/wwwroot`.
Run the server on port 5080 for the Vite development proxy.
Set `HG_INSECURE_COOKIES=true` only for local HTTP development.

`HG_CONFIG` selects the server YAML file. Without it, the server tries `/data/hearthgate.yaml`, then the bundled example.
`HG_DATA` selects the data folder. Without it, the server uses `.hgdata` under its content root.
On first start, the server creates the database and the first admin.
It writes a relative setup link to `first-admin-link.txt` unless a valid `HG_ADMIN_PASSWORD` was supplied.
Open the link through the site, set the password, then enable two-step sign-in in Profile.
Admin tools require two-step sign-in when `auth.requireAdminTotp` is true.

## Releases

The existing [release workflow](.github/workflows/release.yml) accepts manual dispatch and `v*` tag pushes only.
It publishes server and agent images. Tag runs also publish the Windows ZIP and a GitHub release.
It contains no test or validation jobs. Ordinary branch pushes and pull requests do not start it.

## License

See [LICENSE](LICENSE).
