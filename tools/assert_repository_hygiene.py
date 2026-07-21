#!/usr/bin/env python3
"""Reject tracked local automation artifacts that may contain sensitive data."""

from __future__ import annotations

import fnmatch
import subprocess
import sys


PROHIBITED_PREFIXES = (
    ".codex/",
    ".playwright-mcp/",
)
PROHIBITED_ROOT_PATTERNS = (
    "audit-*.png",
)


def find_prohibited_paths(paths: list[str]) -> list[str]:
    findings: list[str] = []
    for raw_path in paths:
        path = raw_path.replace("\\", "/")
        if path.startswith(PROHIBITED_PREFIXES) or (
            "/" not in path
            and any(fnmatch.fnmatchcase(path, pattern) for pattern in PROHIBITED_ROOT_PATTERNS)
        ):
            findings.append(path)
    return sorted(set(findings))


def read_tracked_paths() -> list[str]:
    result = subprocess.run(
        ["git", "ls-files", "-z"],
        check=True,
        stdout=subprocess.PIPE,
    )
    return [
        path.decode("utf-8", errors="surrogateescape")
        for path in result.stdout.split(b"\0")
        if path
    ]


def main() -> int:
    try:
        tracked_paths = read_tracked_paths()
    except (OSError, subprocess.CalledProcessError) as error:
        print(f"repository hygiene check failed to read Git index: {error}", file=sys.stderr)
        return 2

    findings = find_prohibited_paths(tracked_paths)
    if findings:
        print("repository hygiene check failed: prohibited tracked artifacts:", file=sys.stderr)
        for path in findings:
            print(f"- {path}", file=sys.stderr)
        return 1

    print("repository hygiene check passed: no prohibited artifacts are tracked")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
