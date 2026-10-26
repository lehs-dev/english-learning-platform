#!/usr/bin/env python3
"""Guard: every integration test class must appear in exactly one CI shard.

The integration suite is split across parallel jobs in .github/workflows/ci.yml
through --filter "FullyQualifiedName~<ClassName>" lists. A class that is added
to the test project but never added to a shard would silently stop running in
CI, so this script compares the classes declared in the test sources with the
classes referenced by the workflow filters and fails on any mismatch.

Run from the repository root:  python3 scripts/ci/check-test-shards.py
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TEST_DIR = ROOT / "tests" / "EnglishLearningPlatform.IntegrationTests"
WORKFLOW = ROOT / ".github" / "workflows" / "ci.yml"

# Top-level test classes only: nested helper classes are indented and usually
# declared `private`, so anchoring the declaration at the start of a line with
# an optional `public` keeps them out. Partial classes spread over several
# files collapse into one set entry.
CLASS_DECL = re.compile(
    r"^(?:public\s+)?(?:sealed\s+|abstract\s+|static\s+|partial\s+)*class\s+(\w+Tests)\b",
    re.MULTILINE,
)
TEST_ATTRIBUTE = re.compile(r"\[(?:Fact|Theory)\b")
FILTER_REF = re.compile(r"FullyQualifiedName~(\w+)")


def source_classes() -> set[str]:
    classes: set[str] = set()
    for path in sorted(TEST_DIR.glob("*.cs")):
        text = path.read_text(encoding="utf-8-sig")
        if not TEST_ATTRIBUTE.search(text):
            continue
        classes.update(CLASS_DECL.findall(text))
    return classes


def shard_classes() -> list[str]:
    return FILTER_REF.findall(WORKFLOW.read_text(encoding="utf-8"))


def main() -> int:
    expected = source_classes()
    assigned = shard_classes()
    assigned_set = set(assigned)

    duplicates = sorted({name for name in assigned if assigned.count(name) > 1})
    missing = sorted(expected - assigned_set)
    unknown = sorted(assigned_set - expected)

    for name in duplicates:
        print(f"ERROR: {name} appears in more than one shard filter.")
    for name in missing:
        print(f"ERROR: {name} is not assigned to any CI shard; add it to a filter in .github/workflows/ci.yml.")
    for name in unknown:
        print(f"ERROR: {name} is referenced by a shard filter but no such test class exists.")

    if duplicates or missing or unknown:
        return 1
    print(f"Shard coverage OK: {len(expected)} integration test classes, each in exactly one shard.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
