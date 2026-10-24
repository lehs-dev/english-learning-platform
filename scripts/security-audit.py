#!/usr/bin/env python3
"""Audit tracked/unignored files and every reachable Git blob without echoing secrets.

This is a deterministic hygiene gate for known credential forms, not a proof that
all secrets have been found. Review its report before publishing evidence.
"""
import argparse
import csv
import hashlib
import re
import subprocess
import sys
from pathlib import Path


CREDENTIAL = re.compile(r'''(?ix)(?<![\w])
    ((?:\w+[_:])?(?:password|pwd|secret|signingsecret|clientsecret|api[_-]?key|access[_-]?token))
    \s*["']?\s*[:=](?!\?)\s*["']?([^\s;"'<>},)]+)''')
TOKEN = re.compile(r"(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,}|AKIA[A-Z0-9]{16}|sk-(?:proj-)?[A-Za-z0-9_-]{32,})")
PRIVATE_KEY = re.compile(r"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----")
HTML_ENTITY = re.compile(r"&(?:amp|lt|gt|quot|apos|#\d+|#x[0-9a-fA-F]+);")
# Public sample values already present in repository history. These are never
# production defaults: application startup requires externally supplied secrets.
PLACEHOLDERS = {"Change_this_password_123!", "YourStrong@Passw0rd", "YourStrong@Passw0rd!", "Demo@Password123!", "[REDACTED]", "***", "..."}

# Public synthetic fixture from the Admin PR's reachable history. New tests use
# TestOnly_ values. Restrict the legacy exception to this exact file and value;
# the same value in application/configuration sources must still fail the gate.
LEGACY_TEST_FIXTURES = {
    "tests/EnglishLearningPlatform.IntegrationTests/AdminManagementTests.cs": {"Teacher!123Abc"},
}


def git(*args):
    return subprocess.check_output(["git", *args])


def harmless(value, path):
    if value in LEGACY_TEST_FIXTURES.get(path, set()):
        return True
    if value in PLACEHOLDERS or value.startswith(("${", "$", "<", "YOUR_", "your_", "REPLACE_", "replace_")):
        return True
    # References to configuration/test helpers do not contain a credential.
    if re.fullmatch(r"(?:[A-Za-z_]\w*\.)+[A-Za-z_]\w*", value):
        return True
    # Razor implicit expressions (@Model.DemoPassword) reference a server-side
    # property; the rendered value is supplied by configuration, never a literal.
    if re.fullmatch(r"@(?:[A-Za-z_]\w*\.)+[A-Za-z_]\w*", value):
        return True
    if value in {"configuration[", "builder.Configuration[", "options[", "string.Empty", "null", "true", "false", "new"}:
        return True
    if value.startswith("[System.Net.NetworkCredential]::new("):
        return True
    # Only allow explicitly synthetic test fixtures in test sources.
    if path.startswith("tests/") and (value.lower().startswith(("testonly_", "test-", "integration-", "test_", "wrong_", "demo_")) or len(set(value)) == 1
                                     or value in {"Secret", "Password"} or value.startswith("Convert.ToHexString(RandomNumberGenerator.GetBytes(")):
        return True
    return False


def decode_html_entities(content):
    # Decode only HTML syntax before scanning HTML files. This makes encoded
    # credentials detectable while keeping prompt text and placeholders intact.
    entities = {"amp": "&", "lt": "<", "gt": ">", "quot": '"', "apos": "'"}

    def decode(match):
        entity = match.group()[1:-1]
        if entity in entities:
            return entities[entity]
        try:
            number = int(entity[2:], 16) if entity.startswith("#x") else int(entity[1:])
            return chr(number) if 0 <= number <= 0x10ffff else match.group()
        except ValueError:
            return match.group()

    return HTML_ENTITY.sub(decode, content)


def inspect(data, path, source, findings, reviewed):
    if b"\0" in data:
        return
    content = data.decode("utf-8", errors="replace")
    if Path(path).suffix.lower() in {".html", ".htm"}:
        content = decode_html_entities(content)
    for number, line in enumerate(content.splitlines(), 1):
        for match in CREDENTIAL.finditer(line):
            value = match.group(2)
            if harmless(value, path):
                reviewed.add((path, match.group(1).lower(), hashlib.sha256(value.encode()).hexdigest()[:12]))
            else:
                findings.add((source, path, number, match.group(1).lower()))
        if TOKEN.search(line):
            findings.add((source, path, number, "provider_token"))
        if PRIVATE_KEY.search(line):
            findings.add((source, path, number, "private_key"))


def self_test():
    cases = [
        ('Password', 'live_credential', 'src/config.json', True),
        ('SigningSecret', 'live_secret_value', 'src/config.json', True),
        ('MSSQL_SA_PASSWORD', 'live_credential', '.env.example', True),
        ('Payments__SigningSecret', 'live_credential', '.env.example', True),
        ('Password', '${MSSQL_SA_PASSWORD}', '.env.example', False),
        ('Password', 'Change_this_password_123!', 'src/config.json', False),
        ('Password', 'TestOnly_123!', 'tests/Fixture.cs', False),
        ('Password', 'TestOnly_123!', 'src/config.json', True),
        ('Password', 'Teacher!123Abc', 'tests/EnglishLearningPlatform.IntegrationTests/AdminManagementTests.cs', False),
        ('Password', 'Teacher!123Abc', 'tests/OtherTests.cs', True),
        ('Password', 'Teacher!123Abc', 'src/config.json', True),
        ('Password', '@Model.DemoPassword', 'src/View.cshtml', False),
        ('Password', '@live_credential', 'src/View.cshtml', True),
        ('Password', 'OtherCredential123!', 'tests/EnglishLearningPlatform.IntegrationTests/AdminManagementTests.cs', True),
        ('SigningSecret', 'Secret', 'tests/Fixture.cs', False),
        ('key', '-' * 5 + 'BEGIN PRIVATE KEY' + '-' * 5, 'key.pem', True),
        ('token', 'ghp_' + 'a' * 36, 'config.txt', True),
    ]
    for key, value, path, expected in cases:
        findings, reviewed = set(), set()
        data = f'{key}="{value}"'
        inspect(data.encode(), path, 'self-test', findings, reviewed)
        assert bool(findings) == expected, f'Unexpected classification for case in {path}'
    key = "password"
    html_cases = [
        (f'read -r -s -p &apos;SQL backup {key}: &apos; SQLCMDPASSWORD', False),
        (f'{key}=&quot;live_credential&quot;', True),
        (f'{key}=&lt;local-secret&gt;', False),
    ]
    for data, expected in html_cases:
        findings, reviewed = set(), set()
        inspect(data.encode(), 'docs/exports/operations.html', 'self-test', findings, reviewed)
        assert bool(findings) == expected, 'Unexpected HTML entity classification'
    print(f'Security scanner self-test passed: {len(cases) + len(html_cases)} cases.')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, help="Write a metadata-only CSV report and adjacent summary")
    parser.add_argument("--skip-history", action="store_true", help="Only scan the working tree")
    parser.add_argument("--self-test", action="store_true", help="Verify detection, redaction and scoped fixture allowlists")
    args = parser.parse_args()
    if args.self_test:
        self_test()
        return 0
    repo = Path(git("rev-parse", "--show-toplevel").decode().strip())
    findings, reviewed = set(), set()
    worktree_files = 0
    names = git("ls-files", "--cached", "--others", "--exclude-standard", "-z").decode().split("\0")
    for name in sorted(set(names) - {""}):
        path = repo / name
        if path.is_file() and not path.is_symlink():
            inspect(path.read_bytes(), name, "worktree", findings, reviewed)
            worktree_files += 1
    commits, blobs = 0, 0
    if not args.skip_history:
        commits = int(git("rev-list", "HEAD", "--count"))
        objects = []
        for line in git("rev-list", "--objects", "HEAD").decode().splitlines():
            pieces = line.split(" ", 1)
            if len(pieces) == 2:
                objects.append((pieces[0], pieces[1]))
        # One batch process avoids starting a subprocess for every historical blob.
        types = subprocess.check_output(["git", "cat-file", "--batch-check"], input=("\n".join(o[0] for o in objects) + "\n").encode()).decode().splitlines()
        selected = [(oid, path) for (oid, path), kind in zip(objects, types) if " blob " in kind]
        payload = subprocess.check_output(["git", "cat-file", "--batch"], input=("\n".join(o[0] for o in selected) + "\n").encode())
        offset = 0
        for oid, path in selected:
            header_end = payload.index(b"\n", offset)
            size = int(payload[offset:header_end].split()[2])
            start = header_end + 1
            inspect(payload[start:start + size], path, "blob:" + oid[:12], findings, reviewed)
            offset = start + size + 1
            blobs += 1
    summary = (f"candidate_sha={git('rev-parse', 'HEAD').decode().strip()}\n"
               f"reachable_commits={commits}\nhistorical_blobs={blobs}\nworktree_files={worktree_files}\n"
               f"reviewed_placeholder_or_reference_count={len(reviewed)}\nfindings={len(findings)}\n"
               "limitations=Pattern-based scan of locally reachable history and tracked/unignored files; "
               "cannot inspect unreachable or remote-only objects, ignored local secrets or external logs.\n")
    rows = [(source, path, line, kind, "[REDACTED]") for source, path, line, kind in sorted(findings)]
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        with args.output.open("w", newline="") as output:
            writer = csv.writer(output)
            writer.writerow(["source", "path", "line", "kind", "value"])
            writer.writerows(rows)
        args.output.with_suffix(".txt").write_text(summary)
    print(summary, end="")
    writer = csv.writer(sys.stdout)
    writer.writerow(["source", "path", "line", "kind", "value"])
    writer.writerows(rows)
    return 1 if findings else 0


if __name__ == "__main__":
    raise SystemExit(main())
