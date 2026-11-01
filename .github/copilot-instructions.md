# Copilot instructions for HearthGate

## File a GitHub issue for each bug that you see

- When you see a bug, file a GitHub issue for it in `throndir2/HearthGate` at once. Do this every time, also when the bug is not part of the current task.
- Before you file, search the open issues. If an issue for the same bug exists, add a comment with the new information. Do not file a duplicate.
- Give each issue:
  - A short title that names the symptom.
  - The steps to reproduce, the expected result, and the actual result.
  - The file paths and line numbers that are related.
  - The label `bug`.
- Do not fix a bug outside the current task only because you filed it. Fix it only when it is in scope or blocks the task.
- Tell the user the issue numbers that you filed or updated.

## File a GitHub issue for spaghetti code

- When you see spaghetti code, file a GitHub issue for it. Spaghetti code is code that is hard to follow or change. Examples:
  - Mutable state that more than one class owns or changes.
  - A whole session, game, or screen object passed as a service locator.
  - The same list of kinds kept in more than one place.
  - Very long methods or classes that mix unrelated features.
  - Hidden dispatch (reflection, magic strings) where a `switch` or a short table is sufficient.
  - Copied code that must be changed in more than one place.
- Use the same search, duplicate, and reporting rules as for bugs. Use the label `tech-debt`.
- In the issue, say what is tangled, why it is a problem, and a suggested extraction. Do not refactor outside the current task only because you filed the issue.
