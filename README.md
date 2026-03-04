# RE Engine MCP Server

An [MCP (Model Context Protocol)](https://modelcontextprotocol.io) server that gives AI agents live, programmatic access to any running RE Engine game -- Resident Evil, Monster Hunter, Devil May Cry, Dragon's Dogma, and more.

Connect any MCP-compatible agent or IDE to a running game and it can inspect every object in memory, read and write fields, call methods, navigate the full type database, search singletons, and chain multi-step queries across the object graph. It turns a running game into a fully introspectable, queryable system that an agent can reason about and manipulate in real time.

But inspection is just the starting point. The agent can write C# plugins, deploy them into the running game via hot-reload, read back compile errors and logs, fix issues, and iterate -- all autonomously within a single conversation. It's a complete development loop: discover how something works by probing live objects, write code that changes it, test that the change took effect, and repeat until done.

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
  AI Agent / MCP Client
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

**Quick install (Claude Code):**

```cmd
cd mcp-server
install.cmd
```

This registers the server via `claude mcp add`. Other MCP clients use the manual registration below.

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

Once connected, you can ask your agent things like:

- *"What's my current health and position?"*
- *"Find the type that manages weather and show me its fields."*
- *"Set my health to max."*

But the real power is in open-ended requests. You don't need to know the game's internals -- the agent will figure it out:

- *"Figure out how the save system works and write a mod that expands it to 90 save slots."*
- *"Find out what controls item drop rates and make a plugin that doubles them."*
- *"Reverse-engineer how the damage formula works, then write a C# plugin that adds a damage log overlay."*
- *"I want to change the weather to always be sunny. Find the right singleton, figure out the fields, and make it happen."*

### Real-world example: querying every large monster on the map

A user asked: *"What large monsters are currently loaded? Give me each one's species, HP, max HP, position, and distance from my character."*

The agent had zero prior context about the game's enemy system. It solved this autonomously in ~7 minutes across roughly 25 tool calls:

1. **Get the player's position** via `get_player` -- needed later for distance calculation.
2. **Find the enemy manager** -- `get_singleton("app.EnemyManager")` to get the root of the enemy object graph.
3. **Navigate the enemy list** -- a multi-step `chain` query: `EnemyManager` -> `_EnemyList` (a `cManagedArray`) -> `get_Array()` -> expand the array -> filter by `get_CharacterValid=True` -> `get_Context()` -> `get_Em()` -> `Basic` -> filter by `get_IsBoss=True`. This yielded 5 boss-type enemies out of the full enemy pool.
4. **Resolve monster names** -- called `EnemyDef.EnemyName(ID)` for each boss to get a GUID, then batch-resolved those GUIDs via `localize_guid` to get localized names: Doshaguma, Lala Barina, Rathian, Rathalos, Uth Duna.
5. **Read HP** -- navigated each enemy's `Context` -> `Chara` -> `HealthManager` and called `get_Health()` / `get_MaxHealth()` to get current and max HP values.
6. **Read positions** -- called `get_Pos()` on each enemy's `ManageInfo` object to get world coordinates.
7. **Resolve species names** -- this was the non-obvious part. `EnemyDef.Species(ID)` returns a `SPECIES_Fixed` enum, but `EnemyDef.EmSpeciesName()` expects a non-Fixed `SPECIES` enum. These look identical (same member names like `SPECIES_006`) but have different underlying integer values -- the `_Fixed` variant starts at 0 for `INVARID`, while the non-Fixed variant starts at -1, shifting every subsequent value by 1. The agent discovered this by comparing `_Fixed` and non-Fixed backing fields on a data object (e.g., `IconDef.ENEMY_Fixed.E0002 = 3` vs `IconDef.ENEMY.E0002 = 2`), inferred the offset pattern, corrected the enum values, and got the right species names: Fanged Beast, Temnoceran, Flying Wyvern, Leviathan.
8. **Compute distances** -- Euclidean 3D distance from each monster's position to the player.

Final output, formatted as a table:

| Monster | Species | HP | Max HP | Position | Distance |
|---------|---------|---:|-------:|---------:|---------:|
| Doshaguma | Fanged Beast | 14,400 | 14,400 | (-45.5, 82.7, -462.4) | 512 |
| Lala Barina | Temnoceran | 10,483 | 10,483 | (144.5, 35.2, 329.6) | 348 |
| Rathian | Flying Wyvern | 11,772 | 11,794 | (110.9, 37.4, 160.4) | 207 |
| Rathalos | Flying Wyvern | 13,500 | 13,500 | (231.2, 147.6, 74.5) | 309 |
| Uth Duna | Leviathan | 20,196 | 20,196 | (56.1, 201.1, -542.5) | 607 |

The user's question was 15 words. They didn't name a single type, field, method, singleton, or enum. The agent discovered the entire object graph, navigated it, handled a non-obvious enum mapping bug, and delivered a formatted answer with computed distances -- all from a cold start in under 7 minutes.

### Autonomous development

The agent doesn't just read game state -- it can run a full development loop against the live game:

1. **Discover.** Search the type database for relevant singletons, inspect their fields and methods, probe live objects to understand data layouts and collection contents. The agent fans out parallel queries across multiple singletons to find populated collections of the right shape -- scanning `SaveServiceManager`, `ItemManager`, `CharacterManager` etc. to locate `Dictionary`, `HashSet`, `List`, and `Array` fields with real data to test against.
2. **Write.** Author a C# plugin (or modify an existing one) and save it to the game's `reframework/plugins/source/` directory.
3. **Build.** The plugin hot-compiles automatically. The agent reads compile status and errors through the MCP server -- no manual checking needed.
4. **Test.** Deploy test plugins that exercise specific behaviors, read the results from the game log, and verify correctness. If something fails, the agent reads the error, fixes the code, and recompiles -- all without human intervention.
5. **Iterate.** Repeat until the tests pass. The agent can locate candidate objects, write targeted tests, fix field names or type mismatches from error output, and converge on working code autonomously.

For example: given a C++ API with broken collection iteration, the agent probed live `Dictionary`, `HashSet`, `List`, and `Array` objects across multiple game singletons to characterize the failures, read the C++ source to identify the root cause, wrote a fix, built it, then needed test data. It queried the type database and inspected live singletons to find fields of each collection type that were actually populated -- a `Dictionary<String, GameSlotSaveHandler>` with 37 entries here, a `HashSet<ItemID>` with 113 entries there, a `Dictionary<SaveSlotAddress, SaveSlotInfo>` with struct keys for diversity. It assembled these into a 22-test regression suite covering both ManagedObject and proxy iteration paths, deployed it as a hot-compiled plugin, read back the log, fixed wrong field names from the error output, redeployed, and converged to 22 PASS, 0 FAIL. No human touched the keyboard between "fix the broken enumerator" and the final results.

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
