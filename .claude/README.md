# `.claude/` — Claude Code project configuration

These files are read by [Claude Code](https://docs.claude.com/en/docs/claude-code/overview) when it runs in this repository. Commit them so every contributor gets the same tooling.

## Files

- **`settings.json`** — project-level settings (permissions, hooks).
- **`check-coauthor.py`** — `PreToolUse/Bash` hook that blocks `git commit` invocations missing the trailer `Co-Authored-By: Claude <noreply@anthropic.com>`. Ensures every Claude-authored commit is attributed.

## Requirements

- **Python 3** must be on `PATH` as `python3`.
  - Linux / macOS: pre-installed on virtually every distro.
  - Windows: install from [python.org](https://www.python.org/downloads/) (the official installer creates a `python3.exe` shim) or `winget install Python.Python.3`. If only `python` is available, edit `settings.json` and change `python3` to `python`.

The script uses only the standard library (`json`, `re`, `sys`) — no `pip install` step.
