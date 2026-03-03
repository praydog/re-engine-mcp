@echo off
REM Installs the REFramework MCP server into Claude Code (global/user scope).
REM Run from anywhere: install.cmd

claude mcp add -t stdio -s user reframework -e REFRAMEWORK_API_URL=http://localhost:8899 -- dotnet run --project "%~dp0"

echo Done. Restart Claude Code to pick it up.
