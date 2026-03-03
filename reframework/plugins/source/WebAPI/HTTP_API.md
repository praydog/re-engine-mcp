# REFramework.NET Web API — HTTP Reference

Raw HTTP endpoint documentation for the REST API served by the game plugin on `http://localhost:8899`. Most users should use the MCP server instead of calling these directly.

## Quick Start

```bash
# Find a singleton by type name
curl -s "http://localhost:8899/api/explorer/singleton?typeName=via.Application"

# Search the type database
curl -s "http://localhost:8899/api/explorer/search?query=HealthManager&limit=10"

# Inspect a type's schema (no live instance needed)
curl -s "http://localhost:8899/api/explorer/type?typeName=app.cHealthManager"
```

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

If the method signature is known, the `type` field can be omitted — it will be inferred from the parameter type.

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
| `/api/materials` | Per-material visibility for all player meshes |
| `/api/map` | Current stage (code + name), areaNo, prevStage, quest info |
| `/api/singletons` | Managed singleton list with type names, addresses, method/field counts |

### Convenience Endpoints (POST)

| Endpoint | Body | Effect |
|----------|------|--------|
| `/api/player/health` | `{ "value": 100 }` | Set player health |
| `/api/player/position` | `{ "x": 0, "y": 0, "z": 0 }` | Set player position (partial OK) |
| `/api/meshes` | `{ "name": "ch03_013_0013", "visible": false }` | Toggle mesh child visibility |
| `/api/materials` | `{ "gameObject": "ch03_013_0013", "materialIndex": 2, "enabled": false }` | Toggle individual material on a mesh |
