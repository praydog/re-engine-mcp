#!/bin/bash
# Registers the REFramework MCP server with Claude Code (uses 'claude' CLI).
# Run from anywhere: bash install.sh

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

claude mcp add \
  -t stdio \
  -s user \
  reframework \
  -e REFRAMEWORK_API_URL=http://localhost:8899 \
  -- dotnet run --project "$SCRIPT_DIR"

echo "Done. Restart your MCP client to pick it up."
