#!/usr/bin/env python3
"""Fail CI when a `dotnet list package --vulnerable --format json` report contains findings."""

from __future__ import annotations

import json
import pathlib
import sys
from typing import Any


def find_vulnerabilities(value: Any, package: str | None = None) -> list[str]:
    findings: list[str] = []
    if isinstance(value, dict):
        current_package = str(value.get("id") or value.get("name") or package or "unknown-package")
        vulnerabilities = value.get("vulnerabilities")
        if isinstance(vulnerabilities, list):
            for vulnerability in vulnerabilities:
                if isinstance(vulnerability, dict):
                    severity = vulnerability.get("severity", "unknown")
                    advisory = vulnerability.get("advisoryurl") or vulnerability.get("advisoryUrl") or "no-advisory-url"
                    findings.append(f"{current_package}: severity={severity}, advisory={advisory}")
                else:
                    findings.append(f"{current_package}: {vulnerability}")

        for key, nested in value.items():
            if key != "vulnerabilities":
                findings.extend(find_vulnerabilities(nested, current_package))
    elif isinstance(value, list):
        for nested in value:
            findings.extend(find_vulnerabilities(nested, package))
    return findings


def main() -> int:
    if len(sys.argv) != 2:
        print("usage: assert_nuget_audit.py <report.json>", file=sys.stderr)
        return 2

    report_path = pathlib.Path(sys.argv[1])
    try:
        payload = report_path.read_bytes()
        encoding = "utf-16" if payload.startswith((b"\xff\xfe", b"\xfe\xff")) else "utf-8-sig"
        report = json.loads(payload.decode(encoding))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as error:
        print(f"invalid NuGet audit report: {error}", file=sys.stderr)
        return 2

    if not isinstance(report, dict) or not isinstance(report.get("projects"), list):
        print("invalid NuGet audit report: missing projects array", file=sys.stderr)
        return 2

    findings = find_vulnerabilities(report)
    if findings:
        print("NuGet vulnerability audit failed:", file=sys.stderr)
        for finding in findings:
            print(f"- {finding}", file=sys.stderr)
        return 1

    print("NuGet vulnerability audit passed: no vulnerable packages reported")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
