#!/bin/bash
# Installs the REFramework MCP server into Claude Code (global/user scope).
# Run from anywhere: bash install.sh

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

claude mcp add \
  -t stdio \
  -s user \
  reframework \
  -e REFRAMEWORK_API_URL=http://localhost:8899 \
  -- dotnet run --project "$SCRIPT_DIR"

echo "Done. Restart Claude Code to pick it up."
