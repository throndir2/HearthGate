"""Check KB source paths, folder indexes, and changed-source coverage locally."""

import argparse
import os
from pathlib import Path, PurePosixPath
import re
import subprocess
import sys
from urllib.parse import unquote


GENERATED = {".git", "node_modules", "bin", "obj", "wwwroot", "dist",
             "build", "coverage", "__pycache__", ".venv", "generated"}
LINK = re.compile(r"\[[^\]]*\]\(([^)]+)\)")
SOURCE_SUFFIXES = {".cs", ".csproj", ".slnx", ".ts", ".tsx", ".js", ".jsx",
                   ".css", ".html", ".json", ".yaml", ".yml", ".ps1", ".py", ".sh"}


def git(root, *args):
    return subprocess.check_output(
        ["git", "-C", str(root), *args], stderr=subprocess.PIPE
    ).decode("utf-8")


def prose(text):
    """Ignore fenced examples, including the contract's example Code line."""
    fence = None
    for line in text.splitlines():
        mark = re.match(r"^\s{0,3}(`{3,}|~{3,})", line)
        if mark:
            token = mark.group(1)
            if fence is None:
                fence = token
            elif token[0] == fence[0] and len(token) >= len(fence):
                fence = None
            continue
        if fence is None:
            yield line


def normalized(path):
    return str(PurePosixPath(path.replace("\\", "/"))).rstrip("/")


def skipped(path):
    return bool(set(PurePosixPath(normalized(path)).parts) & GENERATED)


def covered(path, code_paths):
    path = normalized(path)
    return any(path == p or path.startswith(p + "/") for p in code_paths)


def source(path):
    p = PurePosixPath(path)
    return not skipped(path) and (
        p.suffix.lower() in SOURCE_SUFFIXES or p.name in {"Dockerfile", "Dockerfile.agent"}
    )


def check(root, diff=None):
    errors, warnings = [], []
    pages = []
    folders = []
    for folder, dirs, files in os.walk(root / "docs"):
        dirs[:] = sorted(d for d in dirs if d not in GENERATED)
        folder = Path(folder)
        folders.append((folder, dirs.copy(), files))
        pages.extend(folder / f for f in sorted(files) if f.endswith(".md"))
    if not folders:
        errors.append("docs/README.md: the KB folder is missing")
    for name in ("README.md", "AGENTS.md", "CONTRIBUTING.md"):
        if (root / name).is_file():
            pages.append(root / name)
    if (root / ".github").is_dir():
        pages.extend(p for p in (root / ".github").rglob("*.md")
                     if not skipped(p.relative_to(root).as_posix()))

    code_paths = set()
    for page in pages:
        for line in prose(page.read_text(encoding="utf-8")):
            if not line.startswith("**Code:**"):
                continue
            names = re.findall(r"`([^`]+)`", line)
            if not names:
                errors.append(f"{page.relative_to(root)}: Code paths must use backticks")
            for name in names:
                path = normalized(name)
                parts = PurePosixPath(path).parts
                if not parts or path in {".", ""} or ".." in parts or ":" in path or path.startswith("/"):
                    errors.append(f"{page.relative_to(root)}: invalid Code path {name}")
                    continue
                if skipped(path):
                    continue
                code_paths.add(path)
                if not (root / path).exists():
                    errors.append(f"{page.relative_to(root)}: Code path does not exist: {path}")

    for folder, dirs, files in folders:
        index = folder / "README.md"
        if not index.is_file():
            errors.append(f"{folder.relative_to(root)}: missing README.md index")
            continue
        links = set()
        for line in prose(index.read_text(encoding="utf-8")):
            for target in LINK.findall(line):
                target = unquote(target.split("#", 1)[0])
                if not target or "://" in target:
                    continue
                links.add((folder / target).resolve())
        for name in sorted(f for f in files if f.endswith(".md") and f != "README.md"):
            if (folder / name).resolve() not in links:
                errors.append(f"{index.relative_to(root)}: page not listed: {name}")
        for name in dirs:
            if (folder / name / "README.md").resolve() not in links:
                errors.append(f"{index.relative_to(root)}: subfolder not listed: {name}/")

    if diff:
        base = git(root, "merge-base", diff, "HEAD").strip()
        fields = git(root, "diff", "--name-status", "-z", "--find-renames", base).split("\0")
        i = 0
        while i < len(fields) and fields[i]:
            status, path = fields[i:i + 2]
            i += 2
            old = path if status[0] in {"D", "R"} else None
            if status[0] in {"R", "C"}:
                path = fields[i]
                i += 1
            if old and old in code_paths:
                warnings.append(f"Code line names a deleted or renamed path: {old}")
            if status[0] != "D" and source(path) and not covered(path, code_paths):
                warnings.append(f"changed source has no Code coverage: {path}")
    return errors, warnings


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--diff", help="Compare source changes with this Git ref, such as origin/main.")
    args = parser.parse_args()
    try:
        root = Path(git(Path.cwd(), "rev-parse", "--show-toplevel").strip())
        errors, warnings = check(root, args.diff)
    except (OSError, UnicodeError, subprocess.CalledProcessError) as exc:
        detail = exc.stderr.decode("utf-8", errors="replace").strip() if isinstance(exc, subprocess.CalledProcessError) else str(exc)
        print(f"ERROR: KB check could not run: {detail}", file=sys.stderr)
        return 1
    for message in errors:
        print(f"ERROR: {message}")
    for message in warnings:
        print(f"WARNING: {message}")
    print(f"KB check: {len(errors)} error(s), {len(warnings)} warning(s).")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
