# Game lifecycle, commands, and profiles

**Status:** Implemented
**Last Updated:** 2026-11-14
**Audience:** Operators, contributors, and agents
**Last verified:** 2026-11-14 against 95892be
**Code:** `src/HearthGate.Agent/GameController.cs`, `src/HearthGate.Agent/ControlScriptRunner.cs`, `src/HearthGate.Agent/GameCommandCatalog.cs`, `src/HearthGate.Agent/GameCommandRunner.cs`, `src/HearthGate.Agent/Rcon.cs`, `src/HearthGate.Agent/GameProfiles/`, `src/HearthGate.Agent/AgentConfig.cs`, `src/HearthGate.Server/Endpoints/OperationsEndpoints.cs`, `src/HearthGate.Contracts/AgentContracts.cs`

## Server game views and lifecycle actions

Server `gameServers[].host` selects a host. `agentKey` selects the game's `key` in that agent's YAML.
Visibility filters all game views and actions.
`games.view` supplies players, version, and connection text.
`connectInfoPublic` explicitly permits guest connection text.
Control and admin grants are described in [accounts](accounts.md).

Start, stop, and restart POST requests return HTTP 202 after permission checks.
The server runs the actual agent action in a background task and logs its later result.
The initial success means accepted, not that the game reached its final state.
Polling wakes early after actions. The UI confirms stop and restart before sending them.
Commands wait for the agent result and return `ok`, `message`, and `output`.

## Agent game kinds and locking

`GameController` owns per-game action locks and transition states.
Concurrent lifecycle actions for the same game are rejected.
Kinds are `script`, `process`, `windowsService`, and `docker`.
Process games start `exe` with optional `args` and `workingDir`.
Status uses `processNames`, or the executable name when no list is supplied.
Windows service games use the configured service name. Docker games use the configured container.

Process stop tries the main window, waits ten seconds, then kills remaining matched processes.
Configured stop steps or Palworld graceful shutdown run before that fallback.
Script games delegate their lifecycle to a Windows PowerShell control script.
The implementation invokes Windows PowerShell; it is not a portable Linux script executor.

## Control script arguments and status contract

`ControlScriptRunner` calls status as `<script> -Action Status -AsJson`, without `-Server`.
The script must report all servers. The agent selects `scriptServer` from its JSON result.
The result can be an array or one object with `Server`, `Running`, and `SupervisorRunning`.
Running maps to running; supervisor-only maps to starting; neither maps to stopped.
Status results are cached for five seconds per script.

Actions use `<script> -Server <scriptServer> -Action Start|Stop|Restart -AsJson`.
Script channel commands use `<script> -Server <scriptServer> -Action Command -Command <text>`.
Arguments are separate process arguments, not a shell command string.
The action timeout is clamped to 30 through 600 seconds from `stopTimeoutSeconds`.
Status has a 60-second timeout.

## Built-in profiles and overrides

`GameCommandCatalog` builds once at startup.
It reads embedded YAML profiles and optional `*.yaml` files from `profilesDir`.
The default custom folder is `game-profiles` next to `agent.yaml`.
Custom profiles with the same ID replace built-in profiles.
Built-in IDs are `minecraft`, `palworld`, `factorio`, `source`, and `ark-ascended`.

Set `profile` on a game. Game `channel` fields override profile fields one by one.
Game commands with the same ID replace profile commands. `disabled: true` removes a command.
`hidden: true` keeps a command out of the public command catalog but permits stop steps to use it.
Invalid command levels become admin-level.
For an HTTP profile with `palworldRest` and no explicit channel, the catalog uses the REST URL and password settings.

## Channels and parameter validation

Channels are `rcon`, `http`, and `script`.
RCON uses a Source RCON connection per command. HTTP uses a configured base URL and optional basic or bearer authentication.
Passwords come from `passwordEnv`, or an existing `passwordIni` file and `passwordIniKey`.
The default INI key is `AdminPassword`.
RCON and script commands render `template`. HTTP commands use `method`, `path`, and optional JSON `body`.

Parameters use `text`, `int`, or `choice`.
`GameCommandRunner.Check` trims inputs, applies defaults, checks required values, and rejects control characters.
Text defaults to 200 characters and can have a full-match regular expression.
Integers use `min` and `max`. Choices must exactly match an option.
Non-HTTP text rejects semicolons. HTTP paths escape substitutions; JSON bodies escape string content.
Output removes Minecraft color codes and limits text to 4,000 characters before a truncation marker.
Commands that require running reject stopped or unknown games. Commands also reject an active lifecycle transition.

## Stop steps and Palworld REST

`stopSteps` run in order: send `command` with `params`, then `wait`, then optionally `waitForStop`.
Wait values are limited to 600 seconds per step.
Failed command steps log a warning and do not stop the sequence.
A null game `stopSteps` uses the profile steps; an empty list disables profile steps.
For non-script games, failure to stop through steps leads to the kind-specific stop fallback.

`palworldRest` supplies player names, count, and version when the game is running.
Without stop steps, non-script games try REST save and shutdown before the normal stop fallback.
It uses the process environment password or reads `AdminPassword` from `settingsIni`.
These paths do not import passwords into repository configuration.
