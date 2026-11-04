#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
if [ -f Cabin.App.dll ]; then
  dotnet Cabin.App.dll --port 8786 --data-directory "$PWD/Data"
else
  dotnet run --project src/Cabin.App/Cabin.App.csproj -- --port 8786 --data-directory "$PWD/Data"
fi
