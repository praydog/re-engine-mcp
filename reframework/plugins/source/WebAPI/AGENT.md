# RE Engine MCP — Agent Guide

This document is for AI agents using the MCP tools to explore and interact with a running RE Engine game. The game must be running with the REFramework plugin loaded.

> For raw HTTP endpoint docs, see [HTTP_API.md](HTTP_API.md).

## Core Workflow

1. **Search** for a type by name substring → `search_types`
2. **Inspect** the type schema (no live instance needed) → `get_type`
3. **Find** a live singleton instance → `get_singleton`
4. **Read** fields and call methods → `summary`, `read_field`, `call_method`
5. **Navigate** deep object graphs → `chain`
6. **Write** values → `write_field`, `invoke_method`

## Key Rules

- **Start with `help`** in a new session — it returns this guide.
- **Use `summary` before `inspect_object`** — lightweight scan saves tokens.
- **Use `chain` for deep navigation.** One call replaces 5+ sequential reads.
- **Use `batch` for independent reads.** Multiple getters in one round-trip.
- **Use `localize_guid` for ALL GUID→text resolution.** Supports batch (comma-separated). Never manually call `via.gui.message.get()`.
- **Addresses change between sessions.** Always resolve singletons by type name.
- **The game must be past the title screen** for most game-specific singletons. Engine singletons like `via.Application` are always available.

## Chain Queries

The most powerful tool. Starts from a singleton or address, applies steps sequentially. Arrays fan out — subsequent steps apply to every element.

| Step | Params | Behavior |
|------|--------|----------|
| `field` | `name` | Follow a reference field |
| `method` | `name`, `signature?` | Call 0-param method, follow returned object |
| `array` | `offset?`, `count?` | Expand into array elements (fans out). **Only works on `T[]` arrays**, NOT `List<T>`, `Dictionary`, etc. For `List<T>`, navigate to the `_items` field first, or use `collect` with getter methods. |
| `filter` | `method`, `value?` (default `"True"`) | Keep objects where method result matches value |
| `collect` | `methods` (string[]) | **Terminal.** Read multiple methods, return values |

Without `collect`, returns object addresses. With `collect`, returns `{ count, results: [{method: value, ...}] }`.

## Object Kinds

- **`managed`** — GC'd .NET objects. Most game singletons.
- **`native`** — Engine objects. `via.Application`, `via.SceneManager`, etc.

## Argument Types

For `invoke_method` args and `write_field` values:

| Type | Notes |
|------|-------|
| `int`, `float`, `double`, `bool`, `string` | Standard primitives |
| `byte`, `short`, `long`, `uint`, `ulong` | Extended primitives |
| `System.Guid` | String format `"xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"` |
| Structs (e.g. `via.vec3`) | Pass as JSON object: `{"x": 1.0, "y": 2.0, "z": 3.0}` |
| Enums | Pass as integer value |

For static method calls, omit `address` and `kind` — only `typeName` and `methodName` required.

## Key Singletons (MHWilds)

These differ per game. Use `get_singletons` to discover them.

| Type | Kind | What it has |
|------|------|-------------|
| `via.Application` | native | FPS, delta time, frame count, uptime |
| `app.PlayerManager` | managed | Player access (→ health, position, etc.) |
| `ace.WeatherManager` | managed | Current weather, transitions |
| `via.SceneManager` | native | Scene/camera info |
| `app.NetworkManager` | managed | Lobby, matchmaking, user info |

## Navigation Examples (MHWilds)

**Player equipment:**
`PlayerManager` → `getMasterPlayer()` → `get_ContextHolder()` → `get_Hunter()` → `get_CreateInfo()` → `app.cHunterCreateInfo`

**Player transform:**
`PlayerManager` → `getMasterPlayer()` → `get_Object()` → `get_Transform()`

**Lobby members (chain):**
```json
{
  "start": { "singleton": "app.NetworkManager" },
  "steps": [
    { "type": "field", "name": "_UserInfoManager" },
    { "type": "field", "name": "_mlInfo" },
    { "type": "field", "name": "_ListInfo" },
    { "type": "array" },
    { "type": "filter", "method": "get_IsValid", "value": "True" },
    { "type": "collect", "methods": ["get_PlName", "get_HunterRank", "get_IsSelf"] }
  ]
}
```

**Resolving equipment names:**
1. Call `WeaponDef.Name(TYPE, id)` → returns `System.Guid`
2. Pass GUID to `localize_guid` → get display name

## Useful Static Types (MHWilds)

**`app.WeaponDef`** — all methods take `(WeaponDef.TYPE wpType, int wpId)`:
`Name`, `Attack`, `Critical`, `Defense`, `Attribute`, `AttributeValue`, `Rare`, `SlotLevel(TYPE, id, slotIdx)`, `Data`

**`app.ArmorDef`** — `Name(ARMOR_PARTS, SERIES)`, `Name(SERIES)`

**`app.EquipUtil`** — `getEquipCurrentWeaponData(int)`, `getEquipCurrentArmorDatas()`

## Gotchas

- **Value type method returns are inlined.** Structs come back as `{ isValueType: true, typeName, value/fields }` — no follow-up address to chase.
- **`set_Rotation` on a player Transform doesn't stick** — character controller overwrites it. Use `rotateAxis(via.vec3, float)` instead (angle in radians).
- **`set_Position` works** — teleportation sticks (player falls due to gravity).
- **Some void methods return `System.Void` as a native object** — check for `childTypeName: "System.Void"` and treat as success.
- **RE Engine `System.Guid` fields** use `mData1`/`mData2`/`mData3`/`mData4_0..7`, NOT standard .NET `_a`/`_b`/`_c`/`_d`.
- **`_Name` on `WeaponData.cData`** is the weapon DESCRIPTION, not display name. Use `WeaponDef.Name(TYPE, id)` for the name GUID.
- **Fields with `isValueType: true`** have values inline. Reference-type fields need `read_field` to follow the pointer.
- **Array pagination**: 50 elements default. Use `offset` and `count`.
- **For large `Def` types** (e.g. `app.EnemyDef`, `app.WeaponDef`) with hundreds of static fields, inspect with `noFields=true` — you only need the methods.
- **`GetField()` uses raw field names, not property names.** Auto-properties have backing fields named `<PropertyName>k__BackingField`, not `PropertyName`. `obj.GetField("AlwaysChaserMode")` silently returns null — you need `obj.Call("get_AlwaysChaserMode")` instead. Use typed proxies (`obj.As<T>().AlwaysChaserMode`) for compile-time safe property access, or `Call("get_...")` for the reflection path. Only use `GetField()` for actual declared fields (check `get_type` to see the real field names).

## Plugin Development

**Hot reload:** Saving a `.cs` file in `reframework/plugins/source/` triggers automatic recompilation of ALL plugins. Every plugin unloads (hooks removed, state lost) and reloads. No build step needed — do NOT run `dotnet build`.

**Workflow:**
1. Write/edit the `.cs` file
2. `wait_compile` — blocks until hot-reload finishes (if it returns `pipe not connected`, fall back to `compile_status`)
3. `compile_status` — quick health check (ok/error, error count)
4. `get_errors` — only if you need full error text
5. `get_log` — read plugin output (the ring buffer includes output from the compile that just finished)

**Reading test output from plugins:**
The correct sequence is: edit file → `wait_compile` → `get_log`. Do NOT `clear_log` between edit and read — the plugin runs at compile time and the output is already in the buffer by the time `wait_compile` returns. If you need a clean log, `clear_log` BEFORE editing the file.

**Forcing a recompile** when nothing changed: make a trivial edit (add a comment) and save. The file watcher triggers on any write.

## Discovering Field Names

**Always use `summary` to find exact field names before writing plugin code.** Field names in RE Engine are not guessable — `_GameSlotSaveHandlers` not `_SaveSlotHandlerDict`, `_AcquiredIDSet` not `_AcquiredItemIDSet`. A wrong name silently returns null via `GetField()` and wastes a compile cycle.

Pattern: `get_singleton` → `summary` → note exact field names → use them in code.

## ManagedObject vs Typed Proxy (Plugin Authors)

Two ways to access game objects in C# plugins:

**ManagedObject (untyped, dynamic):**
```csharp
var ssm = API.GetManagedSingleton("app.SaveServiceManager");
var dict = ssm.GetField("_GameSlotSaveHandlers") as ManagedObject;
int count = (int)dict.Call("get_Count");
foreach (var item in dict) { /* item is object — cast to IObject for Call() */ }
```

**Typed proxy (preferred — compile-time safety, properties):**
```csharp
var ssm = API.GetManagedSingletonT<app.SaveServiceManager>();
bool init = ssm.IsInitialized;  // property access, not get_IsInitialized()
int max = ssm._MaxUseSaveSlotCount;  // field access
```

**Collection iteration via proxy (preferred for typed access):**
```csharp
using Col = REFrameworkNET.Collections;
var dictMo = (ssm as IObject).GetField("_GameSlotSaveHandlers") as ManagedObject;
var dict = dictMo.As<Col.IDictionary<string, app.SaveServiceManager.GameSlotSaveHandler>>();
foreach (var key in dict.Keys) { /* real string */ }
foreach (var val in dict.Values) { /* typed proxy */ }
var handler = dict["CharacterManager"]; // indexer works
```

**Collection iteration via ManagedObject (fallback — works for all collection types):**
```csharp
var dict = ssm.GetField("_GameSlotSaveHandlers") as ManagedObject;
foreach (var item in dict) {
    // Dictionary: item is ValueType<KeyValuePair<K,V>>
    var kvp = item as IObject;
    var key = kvp.Call("get_Key");   // string
    var val = kvp.Call("get_Value"); // ManagedObject
}
```
ManagedObject foreach works for `Dictionary`, `HashSet`, `List`, `Array`, and any `IEnumerable`. Struct elements (like `KeyValuePair`) come back as `ValueType` — call methods on them via `Call()`.

## Named Pipe Tools

These work even when the HTTP API is down (connect via `\\.\pipe\REFrameworkNET`):
`compile_status`, `wait_compile`, `get_errors`, `clear_errors`, `get_log`, `clear_log`, `get_game_info`, `get_plugins`

> **WARNING:** `get_log` returns the full ring buffer. Use `compile_status` for compilation health, `get_plugins` for plugin status. Only use `get_log` when you need actual log output (e.g. reading test results from a plugin).

## Player Mesh Hierarchy (MHWilds)

Navigation: `PlayerManager` → `getMasterPlayer()` → `Object` → `Transform` → walk child linked list (`get_Child()` → `get_Next()` until null).

**Toggling visibility:**
- Individual piece: `via.GameObject.set_DrawSelf(bool)` on the child
- All at once: `app.MeshSettingController.set_Visible(bool)` on root player component
- **Must call `set_Visible()` method** — writing `_Visible` field directly does NOT work

| Child Name Pattern | Slot |
|--------------------|------|
| `ch03_013_*` | Helmet |
| `ch03_031_*` | Chest |
| `ch03_005_*` (lower ID) | Gauntlets |
| `ch03_005_*` (higher ID) | Waist |
| `ch03_022_*` | Leggings |
| `Player_Face` | Face |
| `ch02_000_*` | Body base |
| `ch03_002_*` | Inner layer |
| `Acc000_*` | Accessories |
| `wp*` | Weapons |

## REFramework Documentation

For writing Lua scripts and C# plugins:

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
  - [API, TDB & VM Reference](https://cursey.github.io/reframework-book/api_cs/general/api-reference.html) — `API` static class, `TDB`, managed/native singletons, logging
  - [Type System](https://cursey.github.io/reframework-book/api_cs/general/type-system.html) — `TypeDefinition`, `MethodDefinition`, `FieldDefinition`, runtime reflection
  - [Attributes](https://cursey.github.io/reframework-book/api_cs/general/attributes.html) — `[PluginEntryPoint]`, `[Callback]`, `[MethodHook]` attribute reference
  - [Method Hooks](https://cursey.github.io/reframework-book/api_cs/general/hooks.html) — Pre/post hooks, `Span<ulong> args`, return value modification, skip original
  - [Typed Proxies](https://cursey.github.io/reframework-book/api_cs/general/typed-proxies.html) — Generated interfaces, `.As<T>()`, property access, collection iteration (`IDictionary`, `IList`, `ISet`)
  - [ManagedObject & IObject](https://cursey.github.io/reframework-book/api_cs/general/managed-objects.html) — Reflection-style access, `GetField`, `Call`, `NativeObject`, lifetime/`Globalize()`
  - [Arrays](https://cursey.github.io/reframework-book/api_cs/general/arrays.html) — `SystemArray` creation, reading, writing game arrays
  - [Notes on Threading](https://cursey.github.io/reframework-book/api_cs/general/Notes-On-Threading.html)
  - [Benchmarks](https://cursey.github.io/reframework-book/api_cs/general/benchmarks.html)
  - [Example: RE9 Additional Save Slots](https://cursey.github.io/reframework-book/api_cs/examples/additional-save-slots.html) — Full mod walkthrough

### Tools
  - [Object Explorer](https://cursey.github.io/reframework-book/object_explorer/object_explorer.html) — understanding the type system, singletons, fields, methods

When writing mods, **read the relevant scripting docs first** before guessing at APIs. The Lua and C# surfaces have different capabilities and idioms.
