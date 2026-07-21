#!/usr/bin/env python3
"""Tests for the repository tracked-artifact denylist."""

from __future__ import annotations

import unittest

from assert_repository_hygiene import find_prohibited_paths


class RepositoryHygieneTests(unittest.TestCase):
    def test_rejects_sensitive_local_artifacts(self) -> None:
        paths = [
            ".codex/config.toml",
            ".playwright-mcp/page.yml",
            "audit-01-login.png",
            "src/Program.cs",
        ]

        self.assertEqual(
            find_prohibited_paths(paths),
            [
                ".codex/config.toml",
                ".playwright-mcp/page.yml",
                "audit-01-login.png",
            ],
        )

    def test_allows_intentional_project_artifacts(self) -> None:
        paths = [
            ".playwright-cli/config.json",
            "docs/audit-01-login.png",
            "screenshots/dashboard.png",
            "tools/assert_repository_hygiene.py",
        ]

        self.assertEqual(find_prohibited_paths(paths), [])

    def test_normalizes_windows_separators(self) -> None:
        self.assertEqual(
            find_prohibited_paths([r".codex\config.toml"]),
            [".codex/config.toml"],
        )


if __name__ == "__main__":
    unittest.main()
