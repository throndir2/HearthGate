# HearthGate agent bootstrap

**Status:** Implemented
**Last Updated:** 2026-11-14
**Audience:** Coding agents and contributors
**Last verified:** 2026-11-14 against acd55ed
**Code:** `.github/mcp.json`, `scripts/kb/`

Start with [docs/README.md](docs/README.md), the question-to-code map.
Follow the full [repository rules](.github/copilot-instructions.md) and the [KB contract](docs/knowledge-base.md).

## Knowledge Base First (Required)

Always call `kb_search` on the local repo-kb MCP server when you must find, search, or move through this repository.
Open cited sections with `kb_read_section`. Use their `**Code:**` lines to locate source, then read only the required code.
Do not explore with broad grep, glob, directory listings, or file reads.
Use them only for an exact name supplied by the KB.
If the KB has no answer or a wrong answer, inspect the code and fix the doc in the same pull request.

## Keep the KB true (Required)

A pull request that adds, renames, moves, or deletes code, config, scripts, commands, or behavior updates the KB.
Update the page, `**Code:**`, `**Last verified:**`, and the folder index in that pull request.
A new feature gets a page or section. Delete a removed feature or mark it Superseded.
If no doc must change, the pull request body says "KB: no change needed" and gives the reason.
Before merge, run `python scripts/kb/check_kb.py --diff origin/main` and fix every error.

## Repository boundaries and delivery

Keep deployment values in YAML and secrets out of the repository.
Optional integrations must remain optional. Preserve the minimal guest API and server-side permission checks.
Keep state ownership explicit. Make the smallest focused change and a quick local check.
Use Simplified Technical English for user-facing text.

Finish implementation work through a task branch, a pull request, and remote `main`, unless the user requests a hold.
Fetch and integrate `origin/main` in the task worktree. Never change the shared main checkout or force-push.
For documentation-only work, inspect the diff and run the KB check. Product changes also need a quick local build and behavior check.
Resolve conflicts and actionable feedback before merge. Explicit local-only, draft, review-only, and hold requests override automatic delivery.

Inspect all workflow triggers before pushes and PR operations. Ordinary pushes and PRs must not start Actions.
Use local engineering only, never Copilot Cloud or hosted review.
Only actual minimal releases may use the existing manual or `v*` tag release workflow; never use Actions for validation.
A merge does not authorize deployment, proxy changes, spending, disclosure, or permission changes.
