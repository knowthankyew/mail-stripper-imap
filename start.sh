#!/usr/bin/env bash
set -eo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

echo "=== Starting MailStripper IMAP Watcher Daemon ==="

echo "1. Running architectural claims audit..."
if command -v node >/dev/null 2>&1; then
  node scripts/audit-claims.mjs
fi

echo "2. Building solution and running tests..."
dotnet test mail-stripper-imap.sln

echo "3. Starting daemon..."
dotnet run --project MailStripper.Imap/MailStripper.Imap.csproj
