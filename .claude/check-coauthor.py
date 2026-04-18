#!/usr/bin/env python3
"""PreToolUse hook: blocks `git commit` invocations that omit the Claude
co-author trailer. Reads Claude Code's hook payload (JSON on stdin),
pulls out the Bash command, and exits non-zero with a clear message
when the trailer is missing.
"""
import json
import re
import sys

try:
    payload = json.load(sys.stdin)
except ValueError:
    # Unreadable payload — fail-open rather than blocking every commit.
    sys.exit(0)

cmd = payload.get("tool_input", {}).get("command", "")

# Only inspect `git commit` invocations (not `git commit-tree` etc.).
if not re.match(r"^git\s+commit(\s|$)", cmd):
    sys.exit(0)

# Build the haystack: the command string plus, if -F/--file points at a
# readable message file, that file's contents too. Keeps the hook working
# for `git commit -F /tmp/msg.txt`, not just inline -m messages.
haystack = cmd
file_match = re.search(r"(?:^|\s)(?:-F\s+|--file(?:=|\s+))(\S+)", cmd)
if file_match:
    path = file_match.group(1).strip("\"'")
    if path and path != "-":
        try:
            with open(path, "r", encoding="utf-8", errors="replace") as f:
                haystack += "\n" + f.read()
        except OSError:
            pass  # unreadable — fall back to cmd-only search

# Case-insensitive: git trailers are case-insensitive and GitHub's
# canonical form is the lowercase "Co-authored-by:".
if "co-authored-by: claude" in haystack.lower():
    sys.exit(0)

sys.stderr.write(
    "BLOCKED: this git commit is missing the Claude co-author trailer.\n"
    "\n"
    "Every commit in this repo must end with:\n"
    "    Co-Authored-By: Claude <noreply@anthropic.com>\n"
    "\n"
    "Re-run the commit with that line appended to the message.\n"
)
sys.exit(1)
