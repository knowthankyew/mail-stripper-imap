#!/usr/bin/env bash
# Orchestrates automated demo generation for mail-stripper-imap
set -euo pipefail

DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" >/dev/null 2>&1 && pwd )"
REPO_ROOT="$(dirname "$DIR")"
cd "$REPO_ROOT"

echo "=========================================================="
echo "  mail-stripper-imap - Automated Demo Recorder"
echo "=========================================================="

if ! command -v node >/dev/null 2>&1; then
    echo "❌ Error: Node.js is required to record demo animations." >&2
    exit 1
fi

node scripts/record-demo.js

GIF_PATH="demo.gif"
MP4_PATH="demo.mp4"

if [ ! -f "$MP4_PATH" ]; then
    echo "❌ Error: Expected MP4 artifact missing: $MP4_PATH" >&2
    exit 1
fi

if [ ! -f "$GIF_PATH" ]; then
    echo "❌ Error: Expected GIF artifact missing: $GIF_PATH" >&2
    exit 1
fi

# Determine file size portably
if stat -f%z "$GIF_PATH" >/dev/null 2>&1; then
    GIF_SIZE=$(stat -f%z "$GIF_PATH")
else
    GIF_SIZE=$(stat -c%s "$GIF_PATH")
fi

MIN_SIZE=50000
MAX_SIZE=15728640

if [ "$GIF_SIZE" -lt "$MIN_SIZE" ] || [ "$GIF_SIZE" -gt "$MAX_SIZE" ]; then
    echo "❌ Error: Generated GIF size ($GIF_SIZE bytes) outside acceptable bounds ($MIN_SIZE - $MAX_SIZE bytes)" >&2
    exit 1
fi

echo "✅ Demo recording verified:"
ls -lh "$GIF_PATH" "$MP4_PATH"
