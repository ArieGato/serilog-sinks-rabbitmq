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
except (json.JSONDecodeError, ValueError):
    # Unreadable payload — fail-open rather than blocking every commit.
    sys.exit(0)

cmd = payload.get("tool_input", {}).get("command", "")

# Only inspect `git commit` invocations (not `git commit-tree` etc.).
if not re.match(r"^git\s+commit(\s|$)", cmd):
    sys.exit(0)

if "Co-Authored-By: Claude" in cmd:
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
