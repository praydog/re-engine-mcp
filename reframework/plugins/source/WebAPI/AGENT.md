# REFramework.NET Web API — Agent Guide

This document is for AI agents that need to explore and interact with a running RE Engine game via the HTTP API at `http://localhost:8899`.

## Quick Start

The game must be running with the REFramework plugin loaded. The API is always available — no setup needed.

```bash
# Find a singleton by type name
curl -s "http://localhost:8899/api/explorer/singleton?typeName=via.Application"

# Search the type database
curl -s "http://localhost:8899/api/explorer/search?query=HealthManager&limit=10"

# Inspect a type's schema (no live instance needed)
curl -s "http://localhost:8899/api/explorer/type?typeName=app.cHealthManager"
```

## Core Workflow

1. **Search** for a type by name substring → `GET /api/explorer/search`
2. **Find** its live singleton instance → `GET /api/explorer/singleton`
3. **Inspect** the object's fields and methods → `GET /api/explorer/object`
4. **Read** values via getter methods → `GET /api/explorer/method` (or POST for any method)
5. **Write** values via POST field/method endpoints

## Endpoints

### Read-Only (GET)

| Endpoint | Params | Returns |
|----------|--------|---------|
| `/api/explorer/singletons` | — | `{ managed: [...], native: [...] }` all singletons |
| `/api/explorer/singleton` | `typeName` | `{ address, kind, typeName }` for one singleton |
| `/api/explorer/search` | `query`, `limit?` (default 50) | Substring search across all TDB types |
| `/api/explorer/type` | `typeName`, `includeInherited?`, `noFields?`, `noMethods?` | Full type schema (fields, methods) without needing a live instance. Own members only by default. |
| `/api/explorer/object` | `address`, `kind`, `typeName`, `fields?`, `methods?`, `noFields?`, `noMethods?` | One level: fields (with values for primitives), methods, array info. Optional filters: `fields`/`methods` = comma-separated names to include; `noFields`/`noMethods` = `true` to skip entirely |
| `/api/explorer/field` | `address`, `kind`, `typeName`, `fieldName` | Resolves a reference-type field → child address/kind/typeName |
| `/api/explorer/method` | `address`, `kind`, `typeName`, `methodName`, `methodSignature?` | Invokes a getter (0-param `get_*`/`Get*`/`ToString`) and returns the result |
| `/api/explorer/array` | `address`, `kind`, `typeName`, `offset?`, `count?` | Paginated array elements |

### Write (POST, JSON body)

**`POST /api/explorer/method`** — Invoke any method with arguments
```json
{
  "address": "0xF51C1D0",
  "kind": "native",
  "typeName": "via.Application",
  "methodName": "set_MaxFps",
  "methodSignature": "set_MaxFps(System.Single)",
  "args": [{ "value": 120.0, "type": "float" }]
}
```

**`POST /api/explorer/field`** — Write a value-type field
```json
{
  "address": "0x...",
  "kind": "managed",
  "typeName": "app.SomeType",
  "fieldName": "_Health",
  "value": 100.0,
  "valueType": "float"
}
```

**`POST /api/explorer/batch`** — Multiple operations in one request
```json
{
  "operations": [
    { "type": "singleton", "params": { "typeName": "via.Application" } },
    { "type": "method", "params": { "address": "0xF51C1D0", "kind": "native", "typeName": "via.Application", "methodName": "get_BaseFps" } },
    { "type": "method", "params": { "address": "0xF51C1D0", "kind": "native", "typeName": "via.Application", "methodName": "get_DeltaTime" } }
  ]
}
```

Batch operation types: `singleton`, `object`, `field`, `method`, `search`, `type`, `setField`.

**`POST /api/explorer/chain`** — Follow a path of fields/methods/arrays in one request

The chain endpoint starts from a singleton or address, then applies a sequence of steps. When a step hits an array, it fans out — subsequent steps apply to every element. This replaces the need for multiple sequential batch calls.

```json
{
  "start": { "singleton": "app.NetworkManager" },
  "steps": [
    { "type": "field", "name": "_UserInfoManager" },
    { "type": "field", "name": "_mlInfo" },
    { "type": "field", "name": "_ListInfo" },
    { "type": "array" },
    { "type": "filter", "method": "get_IsValid", "value": "True" },
    { "type": "collect", "methods": ["get_PlName", "get_HunterRank"] }
  ]
}
```

Step types:

| Step | Params | Behavior |
|------|--------|----------|
| `method` | `name`, `signature?` | Call a 0-param method, follow the returned object |
| `field` | `name` | Read a reference field, follow the pointer |
| `array` | `offset?`, `count?` | Expand into array elements (fans out) |
| `filter` | `method`, `value?` (default `"True"`) | Keep only objects where the method result matches `value` |
| `collect` | `methods` (string array) | **Terminal.** Read multiple methods on each object, return `{ count, results: [{method: value, ...}] }` |

Start can be `{ "singleton": "app.TypeName" }` or `{ "address": "0x...", "kind": "managed", "typeName": "..." }`.

If no `collect` step is used, returns `{ count, results: [{ address, kind, typeName }] }`.

### Static Method Calls

To call a **static method**, omit `address` and `kind` from the POST body — only `typeName` and `methodName` are required. The API creates a temporary instance internally.

```json
{
  "typeName": "via.SceneManager",
  "methodName": "get_MainView"
}
```

This works for both native and managed types. You can chain static calls by following the returned object:

```bash
# Static: via.SceneManager.get_MainView() → SceneView
# Then: SceneView.get_PrimaryCamera() → Camera
# Then: Camera.get_FOV() → 45.0
```

### Argument Types

For `args` in method calls and `valueType` in field writes:

| Type string | CLR type |
|-------------|----------|
| `int` / `System.Int32` | int |
| `float` / `System.Single` | float |
| `double` / `System.Double` | double |
| `bool` / `System.Boolean` | bool |
| `string` / `System.String` | string |
| `byte` / `System.Byte` | byte |
| `short` / `System.Int16` | short |
| `long` / `System.Int64` | long |
| `uint` / `System.UInt32` | uint |
| `ulong` / `System.UInt64` | ulong |
| `System.Guid` | Guid (string format `"xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"`) |

### Struct / Value Type Arguments

Methods that take struct parameters (e.g., `via.vec3`, `via.Quaternion`) can be called by passing a JSON object as the `value` with the struct's type name. The API will create the struct instance and populate its fields automatically.

```json
{
  "address": "0x...", "kind": "native", "typeName": "via.Transform",
  "methodName": "set_Position",
  "args": [{ "value": {"x": 100.0, "y": 200.0, "z": 300.0}, "type": "via.vec3" }]
}
```

If the method signature is known, the `type` field can be omitted — it will be inferred from the parameter type:

```json
{
  "address": "0x...", "kind": "native", "typeName": "via.Transform",
  "methodName": "set_Position",
  "args": [{ "value": {"x": 100.0, "y": 200.0, "z": 300.0} }]
}
```

Nested structs are supported recursively. To discover a struct's field names, use `/api/explorer/type?typeName=via.vec3`.

### Enum Arguments

Enum values can be passed as integers:

```json
{ "value": 2, "type": "app.SomeEnumType" }
```

## Object Kinds

- **`managed`** — Garbage-collected .NET objects. Have reference counts. Resolved via `ManagedObject.ToManagedObject(address)`.
- **`native`** — Native engine objects. Resolved via `NativeObject(address, typeDefinition)`. Most engine singletons (like `via.Application`) are native.

### Convenience Endpoints (GET)

| Endpoint | Returns |
|----------|---------|
| `/api/player` | Name, level, health, maxHealth, zenny, points, position, distToCamera, otomoName, seikretName |
| `/api/camera` | Position, FOV, near/far clip |
| `/api/tdb` | Type/method/field counts |
| `/api/lobby` | All lobby members: name, hunterRank, weaponType, isSelf, isQuest |
| `/api/equipment` | Weapon (name, description, type, attack, critical, defense, element, rarity, slots) + armor (name, description, slot, series, upgradeLevel) + palico gear |
| `/api/inventory` | Item pouch: `{ count, capacity, items: [{ slotIndex, id, name, quantity }] }` |
| `/api/meshes` | Player mesh children: `{ count, meshes: [{ name, label, visible }] }` |
| `/api/materials` | Per-material visibility for all player meshes: `{ count, meshSettings: [{ gameObject, label, materialCount, visible, materials: [{ index, name, enabled }] }] }` |
| `/api/map` | Current stage (code + name), areaNo, prevStage, quest info (active, playing, remainTime, elapsedTime) |
| `/api/singletons` | Managed singleton list with type names, addresses, method/field counts |

### Convenience Endpoints (POST)

| Endpoint | Body | Effect |
|----------|------|--------|
| `/api/player/health` | `{ "value": 100 }` | Set player health |
| `/api/player/position` | `{ "x": 0, "y": 0, "z": 0 }` | Set player position (partial OK) |
| `/api/meshes` | `{ "name": "ch03_013_0013", "visible": false }` | Toggle mesh child visibility |
| `/api/materials` | `{ "gameObject": "ch03_013_0013", "materialIndex": 2, "enabled": false }` | Toggle individual material on a mesh |

## Key Singletons

These are common entry points in MHWilds (will differ per game):

| Type | Kind | What it has |
|------|------|-------------|
| `via.Application` | native | FPS, delta time, frame count, uptime |
| `app.PlayerManager` | managed | Player access (→ health, position, etc.) |
| `ace.WeatherManager` | managed | Current weather, weather list, transitions |
| `via.SceneManager` | native | Scene/camera info |
| `app.NetworkManager` | managed | Network services: session, lobby, user info, friends |

To discover singletons for a new game, start with `GET /api/explorer/singletons` and browse, or search for specific keywords.

### Lobby Navigation (MHWilds)

The path to lobby member names: `NetworkManager` → `_UserInfoManager` → `_mlInfo` (lobby) → `_ListInfo` (array) → filter `get_IsValid` → `get_PlName`.

Other `Net_UserInfoManager` lists: `_mqInfo` (quest), `_mmInfo` (matchmaking), `_mlkInfo` (link).

## Gotchas

- **Value type (struct) method returns are inlined.** Methods returning structs (e.g., `via.vec3`, `via.mat4`, `System.Guid`) return their contents directly in the response as `{ isValueType: true, typeName, value/fields }`. For `System.Guid`, the value is a string (`"xxxxxxxx-xxxx-...""`). For other structs, `fields` is a dict of field name → string value. This avoids the ephemeral address problem (the ValueType lives on the .NET heap temporarily and its address is meaningless for subsequent requests).
- **`set_Rotation` on a player Transform doesn't stick** — the game's character controller overwrites it every frame. Use `rotateAxis(via.vec3, System.Single)` instead, which applies a relative rotation the controller respects. The angle is in radians.
- **`set_Position` on a player Transform does work** — teleportation sticks (the player will fall due to gravity afterward).
- **Some methods return `System.Void` as a native object** — this is normal for void-returning methods called via POST. Check for `childTypeName: "System.Void"` and treat it as success.
- **RE Engine `System.Guid` field names** are NOT standard .NET (`_a`, `_b`, `_c`, `_d`..`_k`). They use: `mData1` (uint32), `mData2` (uint16), `mData3` (uint16), `mData4_0`..`mData4_7` (bytes). Total size = 32 bytes, field offsets start at 0x10.
- **`_Name` on `WeaponData.cData` is the weapon DESCRIPTION**, not the display name. The `_Explain` field is empty (all zeros). Use `WeaponDef.Name(TYPE, id)` for the weapon name GUID.
- **GUID → text resolution**: **Always use the `localize_guid` MCP tool** (or `GET /api/localize?guid=...` REST endpoint) to resolve GUIDs to localized text. It supports batch resolution via comma-separated GUIDs. Do NOT manually call `via.gui.message.get()` through the explorer — the dedicated endpoint is simpler, faster, and avoids unnecessary round-trips.

## Tips

- **Use `localize_guid` for ALL GUID-to-text resolution.** Never manually call `via.gui.message.get()` through the explorer API. The `localize_guid` tool (MCP) or `GET /api/localize?guid=...` (REST) is purpose-built for this, supports batch (comma-separated GUIDs), and is the correct way to resolve display names, descriptions, and any localized text.
- **Use chain for deep navigation.** If you need to follow a path of fields/methods/arrays, `POST /api/explorer/chain` does it in one request. Use `collect` as the terminal step to read multiple properties from each result.
- **Use batch for independent operations.** If you need 5 getter results on different objects, batch them.
- **Search is substring-based and case-insensitive.** Searching "Health" will match `app.cHealthManager`, `app.cHunterHealth`, etc. Set a limit to avoid flooding results.
- **Type introspection doesn't need a live object.** Use `/api/explorer/type` to understand a type's schema before finding an instance. Supports `noFields=true` and `noMethods=true` to reduce response size. **For large `Def` types** (e.g. `app.EnemyDef`, `app.WeaponDef`) that have hundreds of static constant fields, always use `noFields=true` — you only need the methods.
- **The `isGetter` flag on methods** tells you which methods are safe to call without side effects (0 params, name starts with `get_`/`Get` or is `ToString`).
- **Fields with `isValueType: true`** have their values inline in the `/api/explorer/object` response. Reference-type fields need a separate `/api/explorer/field` call to follow the pointer.
- **Array pagination**: arrays return 50 elements at a time by default. Use `offset` and `count` to page through.
- **Addresses can change between game sessions.** Always resolve singletons by type name, never hardcode addresses.
- **The game must be actively running (not just the title screen) for most game-specific singletons to be populated.** Engine-level singletons like `via.Application` are always available.

## Example: Read Player Equipment (MHWilds)

Navigation path: `PlayerManager` → `getMasterPlayer()` → `get_ContextHolder()` → `get_Hunter()` → `get_CreateInfo()` → `app.cHunterCreateInfo`.

```bash
curl -s -X POST "http://localhost:8899/api/explorer/chain" -H "Content-Type: application/json" -d '{
  "start": { "singleton": "app.PlayerManager" },
  "steps": [
    { "type": "method", "name": "getMasterPlayer" },
    { "type": "method", "name": "get_ContextHolder" },
    { "type": "method", "name": "get_Hunter" },
    { "type": "method", "name": "get_CreateInfo" }
  ]
}'
# → returns app.cHunterCreateInfo object
```

**`app.cHunterCreateInfo` key fields:**

| Field | Type | Description |
|-------|------|-------------|
| `_WpType` | `app.WeaponDef.TYPE` (enum) | Current weapon type (0=GREAT_SWORD, 5=CHARGE_AXE, etc.) |
| `_WpID` | int | Weapon ID within its type |
| `_WpModelID` | int | Weapon visual model ID |
| `_WpCharmType` | int | Weapon charm type (-1 = none) |
| `_ReserveWpType` | `app.WeaponDef.TYPE` | Reserve/secondary weapon type |
| `_ReserveWpID` | int | Reserve weapon ID |
| `_ArmorSeriesID` | int[6] | Armor series IDs for each slot |
| `_ArmorUpgradeLevel` | uint[6] | Upgrade level for each armor piece |
| `_OuterArmorSeriesID` | int[6] | Layered/transmog armor IDs |
| `_AmuletType` | int | Amulet/talisman type |
| `_AmuletLevel` | int | Amulet level |
| `_Gender` | int | 0=Male, 1=Female |

**Armor slot order** (from `ArmorDef.ARMOR_PARTS` enum): HELM(0), BODY(1), ARM(2), WAIST(3), LEG(4), SLINGER(5).

**Useful static utility types:**

`app.WeaponDef` — all methods take `(WeaponDef.TYPE wpType, int wpId)`:

| Method | Return Type | Notes |
|--------|-------------|-------|
| `Name(TYPE, id)` | `System.Guid` | Resolve via `via.gui.message.get()` for display name |
| `Attack(TYPE, id)` | `int` | Raw attack value |
| `Critical(TYPE, id)` | `int` | Affinity % (can be negative) |
| `Defense(TYPE, id)` | `int` | Defense bonus (0 for most weapons) |
| `Attribute(TYPE, id)` | `WeaponDef.ATTR_TYPE` (enum) | Element type: NONE, FIRE, WATER, THUNDER, ICE, DRAGON, POISON, SLEEP, PARALYSIS, BLAST |
| `AttributeValue(TYPE, id)` | `int` | Element damage value |
| `SubAttribute(TYPE, id)` | `WeaponDef.ATTR_TYPE` (enum) | Secondary element (NONE if absent) |
| `SubAttributeValue(TYPE, id)` | `int` | Secondary element damage |
| `Rare(TYPE, id)` | `WeaponDef.RARE_LV` (enum) | Rarity: RARE01..RARE10 |
| `SlotLevel(TYPE, id, uint slotIdx)` | `int` | Deco slot level (0=empty, 1-4=gem size), slotIdx 0-2 |
| `Data(TYPE, id)` | `WeaponData.cData` | Full weapon data object |

`app.ArmorDef`:

| Method | Return Type | Notes |
|--------|-------------|-------|
| `Name(ARMOR_PARTS, SERIES)` | `System.Guid` | Armor piece name GUID |
| `Name(SERIES)` | `System.Guid` | Armor series name GUID |

`app.EquipUtil`:

| Method | Return Type | Notes |
|--------|-------------|-------|
| `getEquipCurrentWeaponData(int)` | `WeaponData.cData` | Current weapon data object |
| `getEquipCurrentArmorDatas()` | `ArmorData.cData[]` | Current armor set |

**Resolving equipment names (full pipeline):**
```bash
# 1. Get weapon name GUID (static call, no address needed)
curl -s -X POST "http://localhost:8899/api/explorer/method" -H "Content-Type: application/json" \
  -d '{"typeName":"app.WeaponDef","methodName":"Name","args":[{"value":9,"type":"int"},{"value":77,"type":"int"}]}'
# → { "isValueType": true, "typeName": "System.Guid", "value": "68015a97-..." }

# 2. Resolve GUID to localized text (use the dedicated endpoint, NOT via.gui.message.get)
curl -s "http://localhost:8899/api/localize?guid=68015a97-fba5-449b-bd93-b45d23fb2a31"
# → { "guid": "68015a97-...", "text": "Mizuniya Drill I" }

# Armor: ArmorDef.Name(part_index, series_id) → GUID → /api/localize?guid=GUID → name
```

## Example: Navigate to Player Transform (MHWilds)

The chain is: `PlayerManager` → `getMasterPlayer()` → `get_Object()` → `get_Transform()`.

```bash
# 1. Find PlayerManager singleton
curl -s "http://localhost:8899/api/explorer/singleton?typeName=app.PlayerManager"
# → { "address": "0x10ED8B9D0", "kind": "managed", "typeName": "app.PlayerManager" }

# 2. Get master player
curl -s -X POST "http://localhost:8899/api/explorer/method" -H "Content-Type: application/json" \
  -d '{"address":"0x10ED8B9D0","kind":"managed","typeName":"app.PlayerManager","methodName":"getMasterPlayer"}'
# → { "childAddress": "0x...", "childKind": "managed", "childTypeName": "app.cPlayerManageInfo" }

# 3. Get the GameObject
curl -s -X POST "http://localhost:8899/api/explorer/method" -H "Content-Type: application/json" \
  -d '{"address":"<from step 2>","kind":"managed","typeName":"app.cPlayerManageInfo","methodName":"get_Object"}'
# → { "childAddress": "0x...", "childKind": "managed", "childTypeName": "via.GameObject" }

# 4. Get the Transform
curl -s -X POST "http://localhost:8899/api/explorer/method" -H "Content-Type: application/json" \
  -d '{"address":"<from step 3>","kind":"managed","typeName":"via.GameObject","methodName":"get_Transform"}'
# → { "childAddress": "0x...", "childKind": "managed", "childTypeName": "via.Transform" }

# 5. Teleport: set position with struct arg
curl -s -X POST "http://localhost:8899/api/explorer/method" -H "Content-Type: application/json" \
  -d '{"address":"<transform>","kind":"managed","typeName":"via.Transform","methodName":"set_Position","args":[{"value":{"x":100,"y":50,"z":-100}}]}'

# 6. Rotate 180°: use rotateAxis (set_Rotation gets overridden by character controller)
curl -s -X POST "http://localhost:8899/api/explorer/method" -H "Content-Type: application/json" \
  -d '{"address":"<transform>","kind":"managed","typeName":"via.Transform","methodName":"rotateAxis","args":[{"value":{"x":0,"y":1,"z":0},"type":"via.vec3"},{"value":3.14159,"type":"float"}]}'
```

There is also a convenience endpoint `GET /api/player` that returns name, level, health, and position without manual navigation.

## Example: Read Player Health (MHWilds)

Navigate to the player (steps 1-2 above), then follow fields: `cPlayerManageInfo` → `ContextHolder` → `Chara` → `HealthManager` → `_Health`.

## Example: Chain — Get All Lobby Names in One Request

```bash
curl -s -X POST "http://localhost:8899/api/explorer/chain" -H "Content-Type: application/json" -d '{
  "start": { "singleton": "app.NetworkManager" },
  "steps": [
    { "type": "field", "name": "_UserInfoManager" },
    { "type": "field", "name": "_mlInfo" },
    { "type": "field", "name": "_ListInfo" },
    { "type": "array" },
    { "type": "filter", "method": "get_IsValid", "value": "True" },
    { "type": "collect", "methods": ["get_PlName", "get_HunterRank", "get_IsSelf", "get_IsQuest"] }
  ]
}'
# → { "count": 50, "results": [{ "get_PlName": "praydog", "get_HunterRank": "999", ... }, ...] }
```

This replaces what previously took 6+ sequential HTTP requests with one.

## Example: Resolve a GUID to Text

Many game data objects store display names/descriptions as `System.Guid` references into the localization system.

**Preferred: Use the dedicated localization endpoint** (or `localize_guid` MCP tool):

```bash
# Single GUID
curl -s "http://localhost:8899/api/localize?guid=501f2b74-c9a5-4c3f-bdb3-f8395494dd83"
# → { "guid": "501f2b74-...", "text": "An elegant charge blade known for staining foes' clothing a deep blood red." }

# Batch: comma-separated GUIDs in one call
curl -s "http://localhost:8899/api/localize?guid=501f2b74-c9a5-4c3f-bdb3-f8395494dd83,68015a97-fba5-449b-bd93-b45d23fb2a31"
# → { "count": 2, "results": [{ "guid": "501f2b74-...", "text": "An elegant charge blade..." }, { "guid": "68015a97-...", "text": "Mizuniya Drill I" }] }
```

**Do NOT** manually call `via.gui.message.get()` through the explorer API — the dedicated endpoint above is simpler and avoids unnecessary round-trips.

GUID fields in object responses are displayed as `"xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"` strings. You can find them by inspecting objects with `/api/explorer/object` — look for fields with type `System.Guid`.

## Example: Batch Read Game State

```bash
curl -s -X POST "http://localhost:8899/api/explorer/batch" -H "Content-Type: application/json" -d '{
  "operations": [
    {"type":"singleton","params":{"typeName":"via.Application"}},
    {"type":"method","params":{"address":"0xF51C1D0","kind":"native","typeName":"via.Application","methodName":"get_BaseFps"}},
    {"type":"method","params":{"address":"0xF51C1D0","kind":"native","typeName":"via.Application","methodName":"get_DeltaTime"}},
    {"type":"method","params":{"address":"0xF51C1D0","kind":"native","typeName":"via.Application","methodName":"get_FrameCount"}},
    {"type":"method","params":{"address":"0xF51C1D0","kind":"native","typeName":"via.Application","methodName":"get_UpTimeSecond"}}
  ]
}'
```

## MCP Server

An MCP (Model Context Protocol) server wraps the REST API so that Claude Code (or any MCP client) can call endpoints as native tools instead of using `curl` commands.

**Location:** `csharp-api/test/Test/WebAPI/mcp-server-dotnet/` (C# / .NET)

**Configuration:** `.mcp.json` at the repo root (Claude Code), or `.vscode/mcp.json` (VS Code / Copilot):
```json
{
  "mcpServers": {
    "reframework": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["run", "--project", "csharp-api/test/Test/WebAPI/mcp-server-dotnet"],
      "env": {
        "REFRAMEWORK_API_URL": "http://localhost:8899"
      }
    }
  }
}
```

Each REST endpoint maps 1:1 to an MCP tool (e.g., `reframework_get_player` → `GET /api/player`). POST endpoints accept the same JSON parameters as the REST body. The server is a thin proxy — it adds no logic beyond forwarding requests and returning responses.

**Available MCP tools:**

*Web API tools* (require the web API plugin to be compiled and running):
`help`, `get_player`, `get_equipment`, `get_map`, `get_weather`, `get_lobby`, `get_camera`, `get_tdb`, `search_types`, `get_type`, `get_singleton`, `get_singletons`, `inspect_object`, `summary`, `read_field`, `call_method`, `get_array`, `invoke_method`, `write_field`, `batch`, `chain`, `set_health`, `set_position`, `get_materials`, `set_material_visibility`, `localize_guid`.

*Named pipe tools* (work even when web API is down — connect directly to the REFramework.NET host layer via `\\.\pipe\REFrameworkNET`):
- `reframework_compile_status` — **preferred for checking compilation**. Returns `{ compileCycleId, status: "ok"/"error", errorCount, compiling, files: [...] }`. Lightweight — no error text, just the summary.
- `reframework_wait_compile` — block until the current compile cycle finishes (default 15s timeout). Use after editing a .cs file instead of `sleep`. Returns `{ compileCycleId, status, errorCount, timedOut }`.
- `reframework_get_errors` — get full error text (up to 200 entries). Use only when you need the actual error messages. Error messages include the source file path.
- `reframework_get_log` — get recent log entries (assembly loading, plugin compilation, etc.). **WARNING: Returns the full log ring buffer, which can be very large and wastes tokens. Do NOT use for routine status checks.** Only use when debugging a specific issue that requires log text.
- `reframework_get_game_info` — game executable path, directory, framework version
- `reframework_get_plugins` — loaded plugin states: paths, dynamic/static, alive status. **Use this (not `get_log`) to verify a plugin is loaded and alive** — it returns just a compact list of paths and flags.
- `reframework_clear_errors` — clear the error ring buffer. Use **after fixing** compile errors to avoid stale entries. Errors auto-clear on each new compile cycle, so this is rarely needed.
- `reframework_clear_log` — clear the log ring buffer for clean monitoring.

**Key tool rules:**
- **Start with `reframework_help`** — it returns this guide, giving you navigation paths and examples that save many round-trips.
- **For GUID → text, always use `localize_guid`** — supports batch (comma-separated). Never manually call `via.gui.message.get()` through `invoke_method`/`call_method`.
- **Use `summary` before `inspect_object`** — lightweight scan (field names/types + method signatures) saves tokens.
- **Plugin source files are hot-reloaded automatically.** When the game is running, saving a `.cs` plugin file triggers recompilation with no manual build step. Do NOT run `dotnet build` or any external build command — the game handles compilation internally.
- **After editing code**: use `wait_compile` to block until hot-reload finishes, then `compile_status` for a quick health check. Only use `get_errors` if you need the full error text.
- **Use `get_plugins` to verify plugin load status** — it returns just paths and alive/dead flags. **Do NOT use `get_log`** for routine checks — it dumps the full log ring buffer and wastes many tokens. Only use `get_log` when you need to debug a specific issue.

To install: run `install.cmd` (Windows) or `bash install.sh` (Linux) from the `mcp-server-dotnet/` directory. After restarting your MCP client, tools appear automatically.

## Player Mesh Hierarchy (MHWilds)

The player's visual model is a tree of `via.GameObject` children under the root MasterPlayer object. Each child represents a mesh piece (armor, body, face, weapons, accessories).

**Navigation path:** `PlayerManager` → `getMasterPlayer()` → `Object` (via.GameObject) → `Transform` → walk child linked list (`get_Child` → `get_Next`).

The child transform hierarchy uses a **linked list**, not an array:
- `transform.get_Child()` returns the first child
- `child.get_Next()` returns the next sibling
- Continue until `get_Next()` returns null

**Toggling visibility:**
- **Individual piece:** `via.GameObject.set_DrawSelf(bool)` on the child GameObject
- **All at once:** `app.MeshSettingController.set_Visible(bool)` on the root player's component (propagates to all children)
- **Important:** Writing `_Visible` field directly does NOT work — you must call the `set_Visible()` method for the change to propagate

**Known child object → armor slot mapping (MHWilds):**

| Child Name Pattern | Slot |
|--------------------|------|
| `ch03_013_*` | Helmet |
| `ch03_031_*` | Chest |
| `ch03_005_*` (lower ID) | Gauntlets |
| `ch03_005_*` (higher ID) | Waist |
| `ch03_022_*` | Leggings |
| `Player_Face` | Face |
| `ch02_000_*` | Body base (under armor, usually invisible) |
| `ch03_002_*` | Inner layer (usually invisible) |
| `Acc000_*` | Accessories |
| `wp*` | Weapons |

Note: The exact child names include numeric suffixes that vary by equipped gear (e.g., `ch03_013_0013` for a specific helmet). The `GuessMeshLabel()` function in the C# backend maps prefixes to friendly labels.


## REFramework Documentation

For writing Lua scripts and C# plugins that run inside the game:

- **REFramework Book**: https://cursey.github.io/reframework-book/

### Lua scripting
  - [General notes](https://cursey.github.io/reframework-book/api/general/README.html) — return types, method arguments
  - [Best practices](https://cursey.github.io/reframework-book/api/general/best-practices.html) — shared state, hooking gotchas
  - [re (Callbacks)](https://cursey.github.io/reframework-book/api/re.html) — `re.on_frame`, `re.on_draw_ui`, `re.on_pre_application_entry`, `re.on_config_save`
  - [sdk](https://cursey.github.io/reframework-book/api/sdk.html) — `sdk.find_type_definition`, `sdk.hook`, `sdk.call_native_func`, `sdk.get_managed_singleton`
  - [draw](https://cursey.github.io/reframework-book/api/draw.html) — world-space drawing
  - [imgui](https://cursey.github.io/reframework-book/api/imgui.html) — immediate mode GUI
  - [fs](https://cursey.github.io/reframework-book/api/fs.html) — filesystem access
  - [json](https://cursey.github.io/reframework-book/api/json.html) — JSON parsing/serialization
  - [log](https://cursey.github.io/reframework-book/api/log.html) — logging
  - Types: [RETypeDefinition](https://cursey.github.io/reframework-book/api/types/RETypeDefinition.html), [REManagedObject](https://cursey.github.io/reframework-book/api/types/REManagedObject.html), [REMethodDefinition](https://cursey.github.io/reframework-book/api/types/REMethodDefinition.html), [REField](https://cursey.github.io/reframework-book/api/types/REField.html), [REComponent](https://cursey.github.io/reframework-book/api/types/REComponent.html), [RETransform](https://cursey.github.io/reframework-book/api/types/RETransform.html), [SystemArray](https://cursey.github.io/reframework-book/api/types/SystemArray.html), [ValueType](https://cursey.github.io/reframework-book/api/types/ValueType.html)
  - [Example scripts](https://cursey.github.io/reframework-book/examples/Example-Scripts.html) — full working examples
  - [Example snippets](https://cursey.github.io/reframework-book/examples/Example-Snippets.html) — common patterns

### C# scripting (REFramework.NET)
  - [Introduction](https://cursey.github.io/reframework-book/api_cs/general/README.html) — `[PluginEntryPoint]`, `[MethodHook]`, `[Callback]`, typed proxies, `ManagedObject`, `TypeDefinition`
  - [Notes on threading](https://cursey.github.io/reframework-book/api_cs/general/Notes-On-Threading.html)
  - [Benchmarks](https://cursey.github.io/reframework-book/api_cs/general/benchmarks.html)

### Tools
  - [Object Explorer](https://cursey.github.io/reframework-book/object_explorer/object_explorer.html) — understanding the type system, singletons, fields, methods

When writing mods, **read the relevant scripting docs first** before guessing at APIs. The Lua and C# surfaces have different capabilities and idioms.