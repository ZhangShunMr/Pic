#!/usr/bin/env sh
set -eu
cd "$(dirname "$0")"
dotnet run --project src/TrimContact.App -c Release --no-launch-profile -- "$@"
