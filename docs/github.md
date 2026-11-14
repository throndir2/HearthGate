# Public GitHub project showcase

**Status:** Implemented
**Last Updated:** 2026-11-14
**Audience:** Operators, contributors, and agents
**Last verified:** 2026-11-14 against 95892be
**Code:** `src/HearthGate.Server/GitHub/`, `src/HearthGate.Server/Endpoints/PublicEndpoints.cs`, `src/HearthGate.Server/Config/HearthGateConfig.cs`, `web/src/pages/Projects.tsx`

## Project selection and filters

`GitHubShowcaseService` owns the cached public project list.
Without `github.owner`, it returns an empty list and the site response hides Projects navigation.
The server requests the owner's user repository endpoint with `per_page=100`, `type=owner`, and `sort=updated`.
It reads one page only; there is no pagination.
The browser never receives the GitHub token.

Private repositories are excluded.
`include` restricts names when nonempty. `exclude` always removes matching names.
Forks and archived repositories are hidden unless their respective flags are enabled.
`hostedSites` removes names with separately hosted sites.
`hideReposWithHomepage` optionally removes repositories with a homepage value; its default is false.
Names in include, exclude, and hosted-site matching are case-insensitive.
Results sort by pushed time, or updated time when pushed time is absent.

## Cache refresh and failure behavior

The cache lifetime is at least 5 minutes; `cacheMinutes` defaults to 60.
A semaphore prevents concurrent refreshes.
The first request waits for an empty cache to load. Later callers can read the cache during another refresh.
The server sends an ETag on later requests. HTTP 304 refreshes the cache time.
Fetch failures are logged. Existing cached data stays available, and another fetch becomes due after 5 minutes.
An empty failed cache returns an empty project list.

## Display data

`/api/projects` returns name, description, language, stars, forks, update time, topics, and repository URL.
The Home page shows up to six projects. Projects has its own page.
Changing the configured owner does not explicitly clear the existing cache or ETag.
See [configuration](configuration.md) before changing the owner of an active deployment.
