# YAML configuration and environment variables

**Status:** Implemented
**Last Updated:** 2026-11-14
**Audience:** Operators, contributors, and agents
**Last verified:** 2026-11-14 against 95892be
**Code:** `src/HearthGate.Server/Config/`, `src/HearthGate.Server/Program.cs`, `src/HearthGate.Agent/AgentConfig.cs`, `config/`, `deploy/docker-compose.example.yml`

## Server configuration loading and reload

`ConfigProvider` loads `HG_CONFIG`, or `/data/hearthgate.yaml`.
When that path is absent, it logs a warning and loads the bundled `hearthgate.example.yaml`.
YAML properties use camelCase. Unknown properties are ignored.
Validation rejects duplicate service, host, or game IDs.
Services need IDs and names, HTTP or HTTPS public URLs, known visibility values, and known check types.
Each game server must refer to a configured host.
This is not a complete schema validator.

The provider watches its selected file and debounces changes for 500 milliseconds.
A valid reload replaces `Current`. A failed reload logs an error and keeps the previous configuration.
If startup selected the bundled example, the watcher stays on that file.
OIDC registration and trusted proxies need a process restart.
Cookie options are registered at startup; new sign-in expiry also reads the current `sessionHours`.
Group seeds add missing groups at startup; they do not replace database group permissions.
GitHub filters read current configuration, but owner changes do not clear the existing cache.

## Server YAML key map

| Section | Main keys and behavior |
|---|---|
| `site` | `title`, `tagline`, `accentColor`, `logo`, `footerLinks`; `publicUrl` exists in the model but has no current consumer |
| `categories` | `id`, `name`, `icon`; the UI uses them to filter services |
| `services` | `id`, `name`, `description`, `icon`, `category`, `url`, `visibility`, `check` |
| `services[].check` | `type`, `target`, `intervalSeconds`, `timeoutSeconds`, `slowMs`, `failuresBeforeDown`, `expectedStatus`, `insecureTls` |
| `hosts` | `id`, `name`, `agentUrl`, `tokenEnv`, `address`, `ports`, `ping`, `linkAddress`, `linkScheme`, `links` |
| `gameServers` | `id`, `name`, `host`, `agentKey`, `game`, `icon`, `connectInfo`, `connectInfoPublic`, `visibility` |
| `github` | `owner`, `tokenEnv`, `cacheMinutes`, `includeForks`, `includeArchived`, `hideReposWithHomepage`, `include`, `exclude`, `hostedSites` |
| `auth` | `localLogin`, `accountRequests`, `requireAdminTotp`, `sessionHours`, `groups`, optional `oidc` |
| `auth.oidc` | `enabled`, `displayName`, `authority`, `clientId`, `clientSecretEnv`, `autoProvision`, `groupsClaim`, `groupMap` |
| `security` | `trustedProxies`, `proofOfWorkBits` |

See [status](status.md) for check thresholds, [accounts](accounts.md) for permissions and OIDC, and [GitHub](github.md) for filters.

## Environment variables and secrets

| Variable | Current behavior |
|---|---|
| `HG_CONFIG` | Server YAML path; falls back to `/data/hearthgate.yaml`, then the bundled example |
| `HG_DATA` | Server database, keys, and icons folder; default is `.hgdata` under the server content root |
| `HG_ADMIN_USER` | First admin name; default `admin` |
| `HG_ADMIN_PASSWORD` | Optional valid password for first-admin creation |
| `HG_INSECURE_COOKIES` | `true` allows local HTTP cookies and HTTP OIDC metadata |
| `HG_AGENT_CONFIG` | Agent YAML path; default is `agent.yaml` next to the binary |
| `HG_AGENT_TOKEN` | Default agent token variable; `tokenEnv` can name another variable |
| Configured `*Env` names | Agent tokens, GitHub token, OIDC secret, or game channel password |

The server's `ConfigProvider.Secret` reads process environment variables.
The agent token and command-channel passwords can also read user environment variables on Windows.
The older `palworldRest` status path reads the process environment or `settingsIni`.
Never put passwords or tokens in repository YAML.
The server container sets `HG_DATA=/data`; that is a container setting, not the bare server default.

## Agent YAML and lifecycle

`AgentConfig.Load` reads YAML once at startup. Missing files use an empty default configuration.
Unreadable or invalid existing files do not use that missing-file fallback.
Agent YAML and profile changes require a restart.
Top-level keys are `listen`, `tokenEnv`, `allowedClients`, `docker`, `disks`, `games`, and `profilesDir`.
The default listen URL is `http://0.0.0.0:7177`.
An empty client allow-list permits any client that has the bearer token.
See [agent metrics](agent.md) and [game configuration](games.md).

## Example scope and known mismatch

The files in `config` and `deploy` are examples, not actual deployment configuration.
Some values are currently deployment-specific rather than generic.
That mismatch is tracked in [issue #11](https://github.com/throndir2/HearthGate/issues/11).
The KB audit did not change product code or configuration.
