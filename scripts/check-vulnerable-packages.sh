#!/usr/bin/env bash
#
# Scan all restored projects for known-vulnerable NuGet packages (direct and
# transitive). `dotnet list package --vulnerable` reports vulnerabilities but
# still exits 0, so we parse its output and fail explicitly when any are found.
#
# Assumes `dotnet restore` has already run.
set -euo pipefail

echo "Scanning for vulnerable NuGet packages (direct + transitive)..."

if ! output="$(dotnet list package --vulnerable --include-transitive 2>&1)"; then
  echo "$output"
  echo "::error::'dotnet list package --vulnerable' failed to run; failing closed."
  exit 1
fi
echo "$output"

if echo "$output" | grep -qE 'has the following vulnerable packages'; then
  echo "::error::Vulnerable NuGet packages detected. See the report above."
  exit 1
fi

# Fail closed: a scan that actually evaluated the sources prints a per-project
# verdict line. If we see neither the vulnerable marker (handled above) nor any
# clean marker, the scan did not really check anything (offline, source auth
# failure, or changed SDK output) and must NOT report a false all-clear.
if ! echo "$output" | grep -qE 'no vulnerable packages given the current sources'; then
  echo "::error::Vulnerability scan produced no recognisable per-project results; failing closed."
  exit 1
fi

echo "No vulnerable NuGet packages found."
