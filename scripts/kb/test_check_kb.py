"""Focused fixtures for KB paths, indexes, fenced examples, and Git renames."""

import subprocess
import tempfile
from pathlib import Path
import unittest

from check_kb import check, covered


class KnowledgeBaseChecks(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.write("docs/README.md", "# Map\n\n[Feature](feature.md)\n")
        self.write("docs/feature.md", "# Feature\n\n**Code:** `src/feature.cs`\n")
        self.write("src/feature.cs", "// fixture\n")

    def write(self, name, text):
        path = self.root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding="utf-8")

    def git(self, *args):
        subprocess.run(["git", "-C", str(self.root), *args], check=True,
                       capture_output=True)

    def test_fenced_examples_and_generated_folders_are_ignored(self):
        self.write("docs/feature.md", "# Feature\n\n**Code:** `src/feature.cs`\n"
                   "```markdown\n**Code:** `missing/example.cs`\n```\n")
        self.write("docs/generated/stale.md", "**Code:** `missing.cs`\n")
        self.assertEqual(check(self.root), ([], []))

    def test_missing_code_and_folder_index_entries_fail(self):
        self.write("docs/feature.md", "**Code:** `src/missing.cs`\n")
        self.write("docs/extra.md", "# Extra\n")
        self.write("docs/nested/README.md", "# Nested\n")
        errors, _ = check(self.root)
        self.assertTrue(any("does not exist" in e for e in errors))
        self.assertTrue(any("page not listed: extra.md" in e for e in errors))
        self.assertTrue(any("subfolder not listed: nested/" in e for e in errors))

    def test_coverage_matches_parent_not_similar_prefix(self):
        self.assertTrue(covered("src/feature/file.cs", {"src/feature"}))
        self.assertFalse(covered("src/feature-extra/file.cs", {"src/feature"}))

    def test_diff_warns_for_renamed_reference_and_uncovered_source(self):
        self.git("init", "-q")
        self.git("config", "user.name", "KB fixture")
        self.git("config", "user.email", "fixture@example.invalid")
        self.git("add", ".")
        self.git("commit", "-qm", "base")
        self.git("branch", "baseline")
        self.git("mv", "src/feature.cs", "src/renamed.cs")
        self.write("src/new.cs", "// new fixture\n")
        self.git("add", ".")
        self.git("commit", "-qm", "change")
        errors, warnings = check(self.root, "baseline")
        self.assertTrue(any("src/feature.cs" in e for e in errors))
        self.assertTrue(any("deleted or renamed" in w for w in warnings))
        self.assertTrue(any("src/new.cs" in w for w in warnings))
        self.assertTrue(any("src/renamed.cs" in w for w in warnings))


if __name__ == "__main__":
    unittest.main()
