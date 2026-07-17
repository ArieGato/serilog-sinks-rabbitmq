#!/usr/bin/env bash
#
# Scan all restored projects for known-vulnerable NuGet packages (direct and
# transitive). `dotnet list package --vulnerable` reports vulnerabilities but
# still exits 0, so we parse its output and fail explicitly when any are found.
#
# Assumes `dotnet restore` has already run.
set -euo pipefail

echo "Scanning for vulnerable NuGet packages (direct + transitive)..."

output="$(dotnet list package --vulnerable --include-transitive 2>&1)"
echo "$output"

if echo "$output" | grep -qE 'has the following vulnerable packages'; then
  echo "::error::Vulnerable NuGet packages detected. See the report above."
  exit 1
fi

echo "No vulnerable NuGet packages found."
