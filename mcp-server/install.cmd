@echo off
REM Registers the REFramework MCP server with Claude Code (uses 'claude' CLI).
REM Run from anywhere: install.cmd

claude mcp add -t stdio -s user reframework -e REFRAMEWORK_API_URL=http://localhost:8899 -- dotnet run --project "%~dp0."

echo Done. Restart your MCP client to pick it up.
