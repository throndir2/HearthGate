# Knowledge base contract

**Status:** Implemented
**Last Updated:** 2026-11-14
**Audience:** Contributors and agents
**Last verified:** 2026-11-14 against f3c01d5
**Code:** `.github/mcp.json`, `scripts/kb/`, `AGENTS.md`, `.github/copilot-instructions.md`, `docs/repo-kb-eval.json`

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
Use UTC calendar dates for verification and update fields.
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

## Local check

Run `python scripts/kb/check_kb.py` from the repository root.
Before merge, run `python scripts/kb/check_kb.py --diff origin/main` after fetching the target branch.
The check uses the merge base and includes tracked working-tree changes.

Missing `Code` paths and missing documentation index entries are errors.
Changed source without a file or parent-folder `Code` reference is a warning.
A deleted or renamed file still named by a `Code` line is also a warning and can cause a missing-path error.
Warnings do not change the exit code. Fix every error before merge.
The check ignores fenced examples, so the sample header above is not treated as real evidence.

Generated folder names are `.git`, `node_modules`, `bin`, `obj`, `wwwroot`, `dist`, `build`, `coverage`, `__pycache__`, `.venv`, and `generated`.
Keep authored KB pages outside these folders.
The check does not prove behavior claims, verify the SHA, or replace the code audit.
There are no checked-in local hooks to update. Do not install a new hook for this task.
Run `python scripts/kb/test_check_kb.py` for the focused checker fixtures.

## Search evaluation

The [evaluation input](repo-kb-eval.json) contains 30 real repository questions.
The CLI format is an array of objects with `q` and `expect`, where `expect` lists acceptable documentation paths.
Run `npx -y github:throndir2/repo-kb eval docs/repo-kb-eval.json --json` from the repository root.
Use `index` first to complete local embeddings before the measurement.
Hit@1 means the expected page ranked first. Hit@3 and hit@5 mean it appeared within those result limits.
The evaluation checks page retrieval, not answer correctness or token savings.

Measured locally on 2026-11-14 with hybrid retrieval and all 82 chunks embedded:

| Metric | Result |
|---|---|
| Questions | 30 |
| hit@1 | 100% (30/30) |
| hit@3 | 100% (30/30) |
| hit@5 | 100% (30/30) |
| Mean reciprocal rank | 1.000 |
| Median search time | 8 ms |
| Maximum search time | 360 ms |

The measured corpus contained 14 Markdown files.
This is a repository question set, not an independent benchmark.
No page needed retrieval tuning for this set.
Repeat the evaluation when a feature, page, or expected path changes.

## Optional wiki mirror

The GitHub repository reports that its wiki feature is enabled.
On 2026-11-14, the wiki Git endpoint returned "Repository not found".
No pushable wiki repository was available, so no mirror script or wiki publication was added.
The checked-in documentation remains the source of truth. repo-kb does not need a wiki.
