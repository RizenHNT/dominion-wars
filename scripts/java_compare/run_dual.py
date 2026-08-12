#!/usr/bin/env python3
"""Run the Java and C# black-box engines with the same seed and scenarios.

Each command must accept ``--seed N --scenario PATH --json`` and print one
JSON snapshot to stdout.  The script deliberately does not interpret rules;
it compares the two returned documents and reports a structural diff.  Event
IDs are removed because the Java baseline does not expose the C# event counter.

Example (from the repository root)::

    python scripts/java_compare/run_dual.py \
      --java-cmd "java -cp build/classes com.dominionwars.test.TestMain" \
      --cs-cmd "dotnet run --project src/Engine" \
      --seed 42 --scenarios path1.json path2.json

The current development image has no ``python`` executable, so this file is
provided for the PL/QA machine and is not claimed as locally executed here.
"""

from __future__ import annotations

import argparse
import difflib
import json
import os
import shlex
import subprocess
import sys
from pathlib import Path
from typing import Any, Iterable


def parse_command(value: str) -> list[str]:
    """Parse a documented command string without invoking a shell."""
    parts = shlex.split(value, posix=True)
    if not parts:
        raise ValueError("command must not be empty")
    return parts


def run_engine(command: list[str], seed: int, scenario: Path) -> Any:
    argv = command + ["--seed", str(seed), "--scenario", str(scenario), "--json"]
    result = subprocess.run(
        argv,
        check=False,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
    )
    if result.returncode != 0:
        raise RuntimeError(
            f"command failed ({result.returncode}): {argv[0]}\n"
            f"stderr: {result.stderr[-4000:]}"
        )
    try:
        return json.loads(result.stdout)
    except json.JSONDecodeError as exc:
        raise RuntimeError(
            f"command did not print a JSON snapshot: {argv[0]}\n"
            f"stdout tail: {result.stdout[-4000:]}"
        ) from exc


def without_event_ids(value: Any) -> Any:
    if isinstance(value, dict):
        return {
            key: without_event_ids(item)
            for key, item in value.items()
            if key not in {"eventId", "parentEventId"}
        }
    if isinstance(value, list):
        return [without_event_ids(item) for item in value]
    return value


def canonical_lines(value: Any) -> list[str]:
    text = json.dumps(
        without_event_ids(value),
        ensure_ascii=False,
        indent=2,
        sort_keys=True,
    )
    return text.splitlines()


def unified_diff(java: Any, csharp: Any, scenario: Path) -> Iterable[str]:
    return difflib.unified_diff(
        canonical_lines(java),
        canonical_lines(csharp),
        fromfile=f"java:{scenario}",
        tofile=f"csharp:{scenario}",
        lineterm="",
    )


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--java-cmd", required=True)
    parser.add_argument("--cs-cmd", required=True)
    parser.add_argument("--seed", required=True, type=int)
    parser.add_argument("--scenarios", nargs="+", type=Path, required=True)
    return parser


def main() -> int:
    args = build_parser().parse_args()
    java_command = parse_command(args.java_cmd)
    csharp_command = parse_command(args.cs_cmd)
    failures = 0

    for scenario in args.scenarios:
        if not scenario.is_file():
            print(f"FAIL {scenario}: scenario file does not exist", file=sys.stderr)
            failures += 1
            continue

        try:
            java_snapshot = run_engine(java_command, args.seed, scenario)
            csharp_snapshot = run_engine(csharp_command, args.seed, scenario)
        except (OSError, RuntimeError, ValueError) as exc:
            print(f"FAIL {scenario}: {exc}", file=sys.stderr)
            failures += 1
            continue

        diff = list(unified_diff(java_snapshot, csharp_snapshot, scenario))
        if diff:
            failures += 1
            print(f"DIFF {scenario}")
            print("\n".join(diff))
        else:
            print(f"PASS {scenario}")

    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
