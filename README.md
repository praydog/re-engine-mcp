# RE Engine MCP Server

An [MCP (Model Context Protocol)](https://modelcontextprotocol.io) server that gives AI agents live, programmatic access to any running RE Engine game -- Resident Evil, Monster Hunter, Devil May Cry, Dragon's Dogma, and more.

- **Inspect everything.** 100,000+ types, every singleton, every field, every method -- searchable and navigable from a running game. Call any method, chain multi-step queries across the object graph, batch operations in a single request.
- **Read and write live state.** Player health, enemy AI, inventory, position, equipment -- the agent sees what the game sees, in real time.
- **Build mods in-conversation.** Write C# plugins, hot-reload them into the running game, read compile errors, fix, redeploy -- full development loop, no human in the middle.
- **Works across games.** Same tools work on RE2, Monster Hunter Wilds, RE9, DMC5, DD2, and any other RE Engine title. Per-game endpoints (Mr. X tracker, monster HP, adaptive difficulty) light up automatically.

The [web dashboard](#web-dashboard) -- player stats, enemy lists, inventory, Mr. X real-time tracker -- was built entirely by AI agents using these MCP tools, in single conversations, without prior knowledge of each game's internals.

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

### 2. Connect an MCP client

The repo root contains `.mcp.json` — agents that support workspace-level MCP config (Claude Code, Cursor, etc.) will detect it automatically when you open the project.

To register manually with any MCP client, add this to your MCP config:

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

Or use the helper scripts in `mcp-server/` (`install.cmd` / `install.sh`) to register via the `claude` CLI.

### 3. Launch the game

Start the game with REFramework loaded. The plugin compiles automatically and starts the HTTP server. The MCP server connects to it on first tool call.

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

| Game | Convenience endpoints |
|------|----------------------|
| **Monster Hunter Wilds** | Player, equipment, monsters, map, weather, lobby, inventory, palico, materials, chat, hunt log |
| **Resident Evil 2** | Player, enemies, inventory, game info (scenario/difficulty/adaptive rank), Mr. X tracker (AI state, distance, timers) |
| **Resident Evil 9** | Player, equipment, enemies, map, inventory |
| Devil May Cry 5, Dragon's Dogma 2, RE 3/4/7/8, MH Rise, SF6, ... | Core explorer endpoints (works on any RE Engine game) |

The plugin has per-game convenience endpoints gated behind preprocessor symbols (`#if MHWILDS`, `#elif RE9`, `#elif RE2`). The core explorer endpoints -- type search, object inspection, field read/write, method invocation -- work on any RE Engine game without modification.

## 50+ MCP tools

| Category | Tools | What they do |
|----------|-------|-------------|
| **Game state** | `get_player`, `get_equipment`, `get_map`, `get_monsters`, `get_inventory`, `get_stalker`, `get_weather`, `get_lobby`, `get_camera` | Read player stats, gear, enemies, inventory, Mr. X tracker, weather, lobby |
| **Type database** | `search_types`, `get_type`, `get_tdb` | Search and inspect the 100k+ type database |
| **Object explorer** | `get_singleton`, `get_singletons`, `inspect_object`, `summary`, `read_field`, `call_method`, `get_array` | Navigate, inspect, and read any live object in memory |
| **Mutation** | `invoke_method`, `write_field`, `set_health`, `set_position` | Write fields, call methods, modify game state |
| **Traversal** | `chain`, `batch` | Multi-step object graph navigation and batched operations |
| **Materials** | `get_materials`, `set_material_visibility` | Inspect and toggle mesh material visibility |
| **Localization** | `localize_guid` | Resolve message GUIDs to localized text |
| **Dev tools** | `compile_status`, `wait_compile`, `get_errors`, `clear_errors`, `get_log`, `clear_log` | Monitor hot-reload, read compile errors, tail logs |
| **System** | `get_game_info`, `get_plugins`, `help` | Game paths, loaded plugins, agent navigation guide |

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

### Real-world example: querying every large monster on the map (Monster Hunter Wilds, ~7 min)

A user asked: *"What large monsters are currently loaded? Give me each one's species, HP, max HP, position, and distance from my character."*

The agent had zero prior context about the game's enemy system. It found the enemy manager singleton, navigated a multi-step object graph through `chain` queries, and filtered down to boss-type enemies. It resolved monster names via GUID localization, read HP and positions, and computed 3D distances. Along the way it discovered and corrected a non-obvious enum offset bug between `SPECIES_Fixed` and `SPECIES` variants. 15-word question in, formatted table out.

| Monster | Species | HP | Max HP | Distance |
|---------|---------|---:|-------:|---------:|
| Doshaguma | Fanged Beast | 14,400 | 14,400 | 512m |
| Lala Barina | Temnoceran | 10,483 | 10,483 | 348m |
| Rathian | Flying Wyvern | 11,772 | 11,794 | 207m |
| Rathalos | Flying Wyvern | 13,500 | 13,500 | 309m |
| Uth Duna | Leviathan | 20,196 | 20,196 | 607m |

<details>
<summary>Full step-by-step breakdown</summary>

1. **Get the player's position** via `get_player` -- needed later for distance calculation.
2. **Find the enemy manager** -- `get_singleton("app.EnemyManager")` to get the root of the enemy object graph.
3. **Navigate the enemy list** -- a multi-step `chain` query: `EnemyManager` -> `_EnemyList` (a `cManagedArray`) -> `get_Array()` -> expand the array -> filter by `get_CharacterValid=True` -> `get_Context()` -> `get_Em()` -> `Basic` -> filter by `get_IsBoss=True`. This yielded 5 boss-type enemies out of the full enemy pool.
4. **Resolve monster names** -- called `EnemyDef.EnemyName(ID)` for each boss to get a GUID, then batch-resolved those GUIDs via `localize_guid` to get localized names.
5. **Read HP** -- navigated each enemy's `Context` -> `Chara` -> `HealthManager` and called `get_Health()` / `get_MaxHealth()`.
6. **Read positions** -- called `get_Pos()` on each enemy's `ManageInfo` object to get world coordinates.
7. **Resolve species names** -- `EnemyDef.Species(ID)` returns a `SPECIES_Fixed` enum, but `EnemyDef.EmSpeciesName()` expects a non-Fixed `SPECIES` enum. These look identical (same member names) but have different underlying integer values -- the `_Fixed` variant starts at 0, while the non-Fixed variant starts at -1, shifting every value by 1. The agent discovered this by comparing backing fields on a data object, inferred the offset pattern, corrected the enum values, and got the right species names.
8. **Compute distances** -- Euclidean 3D distance from each monster's position to the player.

</details>

### Real-world example: building a Mr. X tracker for Resident Evil 2 (~5 min)

A user asked: *"Add a Mr. X tracker to the dashboard."*

The agent had never seen RE2's enemy system. It explored `EnemyManager`, discovered the `Em6200ChaserController` list (count=1 when Mr. X is spawned), and navigated to the `Em6200Think` AI brain with its state flags and timers.

From there it wrote the full feature: API endpoint with graceful absent-case handling, dashboard card with color-coded status badges and a distance proximity bar, CSS, and MCP tool registration. All deployed via hot-reload and tested against the running game.

```json
{
  "active": true,
  "status": "Chasing (Aggressive)",
  "state": "Move",
  "location": "RPD",
  "playerDistance": 3.19,
  "ai": { "isSleeping": false, "isEncounting": true, "isKillingMode": true, "isGhostMode": false },
  "timers": { "killingTimer": 19.8, "sleepTimer": 0 }
}
```

<details>
<summary>Full step-by-step breakdown</summary>

1. **Find the entry point** -- `get_singleton("app.ropeway.EnemyManager")`, then discovered `get_Em6200ChaserController()` returns a `List<Em6200ChaserController>` (count=1 when Mr. X is spawned, 0 when he's not in the current scenario).
2. **Map the controller** -- inspected the controller object: movement state (Stop/Move/Wait/DoorOpening/DoorPassing/GhostDoorOpening), ghost mode, location ID, safe room detection, always-chaser mode with disable timer.
3. **Navigate to the AI brain** -- `get_Think()` on the controller yields `Em6200Think`, which holds the real AI state: `IsSleeping`, `IsEncounting`, `IsKillingMode`, `IsGhostMode`, `IsScreenTarget`, plus distance tracking (`PlayerDistanceSq`), kill/stun timers, and attack sight timers.
4. **Handle the absent case** -- when Mr. X isn't spawned, the chaser controller list is empty. The endpoint returns `{ active: false, reason: "Mr. X not spawned" }` instead of crashing.
5. **Write the endpoint** -- authored `GetStalkerRE2()` in the plugin, registered the route, deployed via hot-reload.
6. **Build the dashboard card** -- color-coded status badge (red=Chasing, yellow=Searching, green=Stunned, purple=Teleporting), distance proximity bar that turns red as Mr. X gets closer, AI flag badges, and non-zero timer display.

</details>

### Autonomous development

The agent doesn't just read game state -- it can run a full development loop against the live game:

1. **Discover.** Search the type database for relevant singletons. Inspect their fields and methods, probe live objects to understand data layouts. The agent fans out parallel queries to find populated collections of the right shape.
2. **Write.** Author a C# plugin (or modify an existing one) and save it to the game's `reframework/plugins/source/` directory.
3. **Build.** The plugin hot-compiles automatically. The agent reads compile status and errors through the MCP server -- no manual checking needed.
4. **Test.** Deploy test plugins that exercise specific behaviors, read the results from the game log, and verify correctness. If something fails, the agent reads the error, fixes the code, and recompiles -- all without human intervention.
5. **Iterate.** Repeat until the tests pass. The agent can locate candidate objects, write targeted tests, fix field names or type mismatches from error output, and converge on working code autonomously.

For example: given a C++ API with broken collection iteration, the agent probed live `Dictionary`, `HashSet`, `List`, and `Array` objects across multiple game singletons to characterize the failures. It read the C++ source to identify the root cause, wrote a fix, and built it.

Then it needed test data. It queried the type database and inspected live singletons to find populated collections of each type -- a `Dictionary<String, GameSlotSaveHandler>` with 37 entries, a `HashSet<ItemID>` with 113 entries, a `Dictionary<SaveSlotAddress, SaveSlotInfo>` with struct keys for diversity. It assembled a 22-test regression suite, deployed it as a hot-compiled plugin, read back the log, fixed wrong field names from the error output, redeployed, and converged to 22 PASS, 0 FAIL. No human touched the keyboard between "fix the broken enumerator" and the final results.

> **Tip:** Pair with [IDA Pro MCP](https://github.com/mrexodia/ida-pro-mcp) for even deeper reverse engineering. If the IDA database has RE Engine method addresses mapped to names (REFramework ships a Python script for this), the agent can read decompiled function bodies alongside live object inspection. Not required, but powerful when you need to understand what a function actually does rather than just what it's called.

The agent guide (`help` tool) provides detailed navigation paths, chain examples, and tips -- call it first in any new session.

## Web dashboard

Open `http://localhost:8899` in a browser while the game is running. The dashboard auto-detects which game is running and shows the relevant cards:

- **Player** -- HP bar with slider, position, status badges (poison, combat, etc.)
- **Enemies** -- Active enemy list with HP bars, distance, kill tracking
- **Inventory** -- Item pouch with weapon ammo/durability bars, equipped weapon, item counts
- **Mr. X Tracker** (RE2) -- Real-time AI state (Chasing/Searching/Stunned/Teleporting), distance proximity bar, location, kill/stun timers, on-screen detection
- **Game Info** -- Scenario, difficulty, adaptive difficulty rank with damage/break multipliers, save count
- **Monsters** (MH Wilds) -- Large monster HP, species, position, distance
- **Weather** (MH Wilds) -- Current/next weather, blend rates, in-game clock
- **Equipment** -- Weapon stats, armor pieces, decorations
- **Object explorer** -- Browse singletons, navigate object graphs, inspect fields and methods interactively

Cards that don't apply to the current game are hidden automatically. The same plugin binary, same dashboard code -- it just adapts.

The entire dashboard -- every card, every endpoint, every line of CSS -- was built by AI agents using the MCP tools in this repo. No human wrote the player HP bars, the enemy list, the inventory renderer, or the Mr. X tracker.

The process: an agent explored the live game's object graph to discover what data was available. It wrote API endpoints to expose that data, authored the HTML/JS/CSS to render it, tested against the running game, and iterated until it worked. The full RE2 dashboard -- player, enemies, inventory, game info, Mr. X tracker -- took about 30 minutes.

The per-game detection, the adaptive card visibility, the RE2-specific features -- all autonomously built through the same tools the repo ships. The dashboard is both a useful feature and a proof of what the MCP server makes possible.

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
