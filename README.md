# RE Engine MCP Server

An [MCP (Model Context Protocol)](https://modelcontextprotocol.io) server that gives AI agents live, programmatic access to any running RE Engine game -- Resident Evil, Monster Hunter, Devil May Cry, Dragon's Dogma, and more.

Connect Claude (or any MCP client) to a running game and it can inspect every object in memory, read and write fields, call methods, navigate the full type database, search singletons, and chain multi-step queries across the object graph. It turns a running game into a fully introspectable, queryable system that an AI can reason about and manipulate in real time.

## What can it do?

With the game running, an AI agent can:

- **Explore the type system.** Search 100,000+ types by name, inspect fields, methods, inheritance -- the entire type database is queryable.
- **Navigate live objects.** Start from any singleton, walk reference fields, expand arrays, filter by method return values, and collect data across object graphs -- all in a single chained query.
- **Read and write game state.** Get/set player health, position, inventory. Read equipment stats, quest progress, weather, lobby members. Toggle material visibility on meshes.
- **Call any method.** Invoke instance or static methods with typed arguments (int, float, bool, string, enum, struct). Great for triggering game behaviors or reading computed properties.
- **Batch operations.** Combine multiple reads/writes into a single request. No round-trip overhead.
- **Localize text.** Resolve message GUIDs to localized strings (item names, quest descriptions, UI text).
- **Monitor plugin state.** Check compile status, read logs, wait for hot-reload cycles to complete -- all via a named pipe that works even when the HTTP API is down.
- **Build mods live.** Write a C# plugin, save it, and the MCP server reports compile errors and logs in real time. The agent iterates on code without ever leaving the conversation.

## Architecture

Three components, each independently useful:

```
  Claude / AI Agent
        |
        | MCP protocol (stdio)
        v
  +-----------------+
  |   MCP Server    |  mcp-server/     .NET console app
  |  (stdio bridge) |  Translates MCP tool calls -> HTTP
  +-----------------+
        |
        | HTTP (localhost:8899)
        v
  +-----------------+
  |  Game Plugin    |  plugin/          C# REFramework.NET plugin
  |  (HTTP server)  |  Runs inside the game process
  +-----------------+
        |
        | REFramework.NET API
        v
  +-----------------+
  |   RE Engine     |  The actual game
  |   Game Process  |
  +-----------------+
```

**`reframework/plugins/source/TestWebAPI.cs`** -- A C# plugin loaded by [REFramework](https://github.com/praydog/REFramework) inside the game. It starts an HTTP server on `localhost:8899`, exposes `/api/*` endpoints that call into the game engine via REFramework.NET's managed API, and serves the web dashboard from the `WebAPI/` folder.

**`reframework/plugins/source/WebAPI/`** -- A static web frontend with a live object explorer and player dashboard. Open `http://localhost:8899` in a browser while the game is running.

**`mcp-server/`** -- A standalone .NET 10 console app that speaks MCP over stdio. It translates MCP tool calls into HTTP requests to the game plugin. Also has a named-pipe channel for out-of-band operations (compile status, logs, errors) that work even before the HTTP plugin loads.

## Supported games

Any RE Engine title that REFramework supports:

- Monster Hunter Wilds
- Monster Hunter Rise
- Resident Evil 2/3/4/7/8/9
- Devil May Cry 5
- Dragon's Dogma 2
- Street Fighter 6
- And more

The plugin has per-game convenience endpoints (player info, equipment, weather, etc.) gated behind preprocessor symbols (`#if MHWILDS`, `#if RE9`). The core explorer endpoints -- type search, object inspection, field read/write, method invocation -- work on any RE Engine game without modification.

## 50+ MCP tools

| Category | Tools | What they do |
|----------|-------|-------------|
| **Game state** | `get_player`, `get_equipment`, `get_map`, `get_weather`, `get_lobby`, `get_camera` | Read player stats, gear, location, weather, multiplayer lobby |
| **Type database** | `search_types`, `get_type`, `get_tdb` | Search and inspect the 100k+ type database |
| **Object explorer** | `get_singleton`, `get_singletons`, `inspect_object`, `summary`, `read_field`, `call_method`, `get_array` | Navigate, inspect, and read any live object in memory |
| **Mutation** | `invoke_method`, `write_field`, `set_health`, `set_position` | Write fields, call methods, modify game state |
| **Traversal** | `chain`, `batch` | Multi-step object graph navigation and batched operations |
| **Materials** | `get_materials`, `set_material_visibility` | Inspect and toggle mesh material visibility |
| **Localization** | `localize_guid` | Resolve message GUIDs to localized text |
| **Dev tools** | `compile_status`, `wait_compile`, `get_errors`, `clear_errors`, `get_log`, `clear_log` | Monitor hot-reload, read compile errors, tail logs |
| **System** | `get_game_info`, `get_plugins`, `help` | Game paths, loaded plugins, agent navigation guide |

## Setup

### Prerequisites

- [REFramework nightly build](https://github.com/praydog/REFramework-nightly/releases) installed for your game. **You must use a nightly build** -- stable releases do not include .NET support.
- `csharp-api.zip` from the [same nightly releases page](https://github.com/praydog/REFramework-nightly/releases). Extract it into your game's `reframework/` folder to get the .NET runtime and reference assemblies the plugin needs.
- [.NET 10 runtime](https://dotnet.microsoft.com/download/dotnet/10.0) -- required by the C# API and the MCP server. Install the SDK if you want to build the MCP server from source, or just the runtime if using a pre-built release.

### 1. Install the game plugin

Copy the `reframework/` folder into your game directory. It mirrors the exact folder structure REFramework expects:

```
<game>/reframework/plugins/source/TestWebAPI.cs
<game>/reframework/plugins/source/WebAPI/
```

REFramework will hot-compile the plugin on next game launch. You should see `[WebAPI] Listening on http://localhost:8899/` in the REFramework log.

### 2. Install the MCP server

**For Claude Code:**

```cmd
cd mcp-server
install.cmd
```

This registers the MCP server with Claude Code via `claude mcp add`. Restart Claude Code to pick it up.

**Manual registration (any MCP client):**

```json
{
  "mcpServers": {
    "reframework": {
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/mcp-server"],
      "env": {
        "REFRAMEWORK_API_URL": "http://localhost:8899"
      }
    }
  }
}
```

### 3. Launch the game

Start the game with REFramework loaded. The plugin compiles automatically and starts the HTTP server. The MCP server connects to it on first tool call.

## Usage examples

Once connected, you can ask Claude things like:

- *"What's my current health and position?"*
- *"Find the type that manages weather and show me its fields."*
- *"Set my health to max."*

But the real power is in open-ended requests. You don't need to know the game's internals -- the agent will figure it out:

- *"Figure out how the save system works and write a mod that expands it to 90 save slots."*
- *"Find out what controls item drop rates and make a plugin that doubles them."*
- *"Reverse-engineer how the damage formula works, then write a C# plugin that adds a damage log overlay."*
- *"I want to change the weather to always be sunny. Find the right singleton, figure out the fields, and make it happen."*

The agent has the full type database, every singleton, every field and method in the running game. It can search for types by name, inspect live objects, trace references, write plugins, watch them hot-compile, and iterate -- all in one conversation. You describe what you want; it figures out the internals.

> **Tip:** Pair with [IDA Pro MCP](https://github.com/mrexodia/ida-pro-mcp) for even deeper reverse engineering. If the IDA database has RE Engine method addresses mapped to names (REFramework ships a Python script for this), the agent can read decompiled function bodies alongside live object inspection. Not required, but powerful when you need to understand what a function actually does rather than just what it's called.

The agent guide (`help` tool) provides detailed navigation paths, chain examples, and tips -- call it first in any new session.

## Web dashboard

Open `http://localhost:8899` in a browser while the game is running for:

- **Player dashboard** -- Live stats, equipment, position, map info
- **Object explorer** -- Browse singletons, navigate object graphs, inspect fields and methods interactively

## Configuration

| Environment variable | Default | Description |
|---------------------|---------|-------------|
| `REFRAMEWORK_API_URL` | `http://localhost:8899` | URL of the in-game HTTP API |

The plugin listens on port 8899 by default. Change `s_port` in `TestWebAPI.cs` to use a different port, and set `REFRAMEWORK_API_URL` accordingly.

## How it works

The game plugin uses REFramework.NET to access the RE Engine's managed object system. Every game object, singleton, type, field, and method is reachable through the type database (TDB). The plugin exposes this as a REST API:

1. **Type queries** go straight to the TDB -- no live instance needed
2. **Object inspection** reads field values from live objects in the game's managed heap
3. **Method invocation** calls game methods through REFramework's managed interop layer
4. **Chain queries** walk the object graph server-side, expanding fields, calling methods, filtering, and collecting results in a single request

The MCP server is a thin translation layer. Each MCP tool maps to one HTTP endpoint. The named pipe channel provides a secondary communication path for compile/log operations that must work before the HTTP plugin is loaded.

## License

MIT
