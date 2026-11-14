# Knowledge base contract

**Status:** Partial
**Last Updated:** 2026-11-14
**Audience:** Contributors and agents
**Last verified:** 2026-11-14 against 6a23bc5
**Code:** `.github/mcp.json`

The knowledge base (KB) is the repository's Markdown documentation. It is the source of truth for current behavior.
The source code supplies the evidence used to verify each page.

## Search before source reads

Use the local `repo-kb` MCP server. Call `kb_search` with a question, then open its cited sections with `kb_read_section`.
Use the page's `Code` line to select the source files that you need.
If the KB has no answer, inspect the relevant code and update the page in the same pull request.

The repository MCP definition uses `npx` and the public `throndir2/repo-kb` package. It needs Node.js 20 or later and git.
There is no repository setup script. There is no custom include list: the default paths include `docs`, the root README, and agent instructions.

The CLI provides `index`, `status`, `search`, `read`, and `eval`. Run these commands from the repository root.
For example, use `npx -y github:throndir2/repo-kb index` to refresh the local index.
The index refreshes during searches. Index data stays outside the repository.
Use an explicit workspace path as the MCP `repo` argument when another worktree of the same repository is active.

## Page headers and evidence

Each Markdown page starts with an H1 title. Put these fields directly below it:

```markdown
# Page title

**Status:** Implemented
**Last Updated:** YYYY-MM-DD
**Audience:** Operators, contributors, or agents
**Last verified:** YYYY-MM-DD against <short sha>
**Code:** `src/Feature/File.cs`, `config/example.yaml`
```

Use `Implemented`, `Partial`, `Planned`, or `Superseded` for status.
The verified SHA identifies the source revision inspected, not the future commit that contains the page.
Refresh the verification line when you check or change a claim.
A page about built behavior must name its main source files or folders in `Code`.
Use backticks, repository-relative paths, and forward slashes in these Markdown references.
Runtime filesystem commands use the target operating system's path format.

Describe what the code does today. Put unbuilt design only in a section titled `Planned`.
Do not present configured examples as universal product defaults.
Remove stale or duplicate pages, or mark them `Superseded` and link to the replacement.

## Folder indexes

Each documentation folder has a `README.md` index.
List each page and each direct subfolder once, with a link and the question it answers.
Link a subfolder to its README. The README does not need to list itself.
List evaluation data and scripts when they are stored in the documentation folder.
The [top-level map](README.md) connects questions to the main source areas.

## Generated pages

Generated pages must name their generator and its command.
Change the generator or its input, then generate the page again. Never edit generated pages by hand.
This repository currently has no generated KB pages.

## Planned

Add a local path and index check, required agent bootstrap rules, and a repository search evaluation.
Measure hit@1 and hit@5 after the audited pages are indexed. No search-quality result is available yet.
