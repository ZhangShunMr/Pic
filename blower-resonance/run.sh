#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
exec dotnet run --project src/Blower.App/Blower.App.csproj --urls http://127.0.0.1:5178
