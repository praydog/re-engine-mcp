using System.ComponentModel;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace REFrameworkMcp;

static class Http
{
    static readonly string Base = Environment.GetEnvironmentVariable("REFRAMEWORK_API_URL") ?? "http://localhost:8899";
    static readonly HttpClient Client = new();

    public static async Task<string> Get(string path, Dictionary<string, string?>? query = null)
    {
        var url = Base + path;
        if (query is { Count: > 0 })
        {
            var qs = string.Join("&", query
                .Where(kv => kv.Value is not null)
                .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value!)}"));
            if (qs.Length > 0) url += "?" + qs;
        }
        return await Client.GetStringAsync(url);
    }

    public static async Task<string> Post(string path, object body)
    {
        var json = JsonSerializer.Serialize(body);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var res = await Client.PostAsync(Base + path, content);
        return await res.Content.ReadAsStringAsync();
    }
}

// ── Named pipe tools (work even when web API plugin is down) ────────

[McpServerToolType]
public static class PipeTools
{
    [McpServerTool(Name = "reframework_get_errors")]
    [Description("Get recent compile errors from REFramework.NET (via named pipe, works even when web API is down)")]
    public static async Task<string> GetErrors()
        => await Pipe.Request("get_errors") ?? """{"error":"pipe not connected"}""";

    [McpServerTool(Name = "reframework_get_log")]
    [Description("Get recent log entries from REFramework.NET (via named pipe, works even when web API is down). Returns the full ring buffer — contains output from the most recent compile cycle. To get isolated output: clear_log BEFORE editing a .cs file, then wait_compile, then get_log.")]
    public static async Task<string> GetLog()
        => await Pipe.Request("get_log") ?? """{"error":"pipe not connected"}""";

    [McpServerTool(Name = "reframework_get_game_info")]
    [Description("Get game executable path, directory, working directory, plugins directory, and framework version (via named pipe, works even when web API is down)")]
    public static async Task<string> GetGameInfo()
        => await Pipe.Request("get_info") ?? """{"error":"pipe not connected"}""";

    [McpServerTool(Name = "reframework_get_plugins")]
    [Description("Get loaded plugin states: paths, dynamic/static, alive status (via named pipe, works even when web API is down)")]
    public static async Task<string> GetPlugins()
        => await Pipe.Request("get_plugins") ?? """{"error":"pipe not connected"}""";

    [McpServerTool(Name = "reframework_clear_errors")]
    [Description("Clear the error ring buffer (via named pipe). Use after fixing compile errors to avoid stale errors confusing future checks.")]
    public static async Task<string> ClearErrors()
        => await Pipe.Request("clear_errors") ?? """{"error":"pipe not connected"}""";

    [McpServerTool(Name = "reframework_clear_log")]
    [Description("Clear the log ring buffer (via named pipe). Call BEFORE editing a .cs file if you need isolated output from the next compile cycle — the plugin runs at compile time, so output is already in the buffer by the time wait_compile returns.")]
    public static async Task<string> ClearLog()
        => await Pipe.Request("clear_log") ?? """{"error":"pipe not connected"}""";

    [McpServerTool(Name = "reframework_compile_status")]
    [Description("Quick compile health check: returns compileCycleId, status (ok/error), errorCount, compiling flag, and list of files with errors. Much lighter than get_errors — use this for status checks, get_errors only when you need the full error text.")]
    public static async Task<string> CompileStatus()
        => await Pipe.Request("compile_status") ?? """{"error":"pipe not connected"}""";

    [McpServerTool(Name = "reframework_wait_compile")]
    [Description("Block until the current compile cycle finishes (or timeout). Returns compileCycleId, status, errorCount, timedOut. Use after editing a .cs file instead of sleeping — waits for the hot-reload to complete. May return pipe not connected if the named pipe is not active yet — fall back to compile_status in that case.")]
    public static async Task<string> WaitCompile(
        [Description("Timeout in milliseconds (default 15000)")] int timeout = 15000)
        => await Pipe.Request("wait_compile", new() { ["timeout"] = timeout }, readTimeoutMs: timeout + 5000)
           ?? """{"error":"pipe not connected"}""";
}

// ── Agent guide ─────────────────────────────────────────────────────

[McpServerToolType]
public static class GuideTools
{
    [McpServerTool(Name = "reframework_help")]
    [Description("Get the agent guide for navigating the game engine. Returns navigation paths, chain examples (lobby members, player equipment, health), key singletons, gotchas, and tips. **Call this FIRST in a new session before exploring** — it will save many round-trips by showing you the exact paths to common data.")]
    public static async Task<string> Help() => await Http.Get("/api/help");
}

// ── Convenience GET endpoints ───────────────────────────────────────

[McpServerToolType]
public static class ConvenienceTools
{
    [McpServerTool(Name = "reframework_get_player")]
    [Description("Get player info: name, level, HP, max HP, position, palico name, seikret name")]
    public static async Task<string> GetPlayer() => await Http.Get("/api/player");

    [McpServerTool(Name = "reframework_get_equipment")]
    [Description("Get equipped weapon (name, type, attack, element, slots, description), armor pieces with descriptions, and palico gear")]
    public static async Task<string> GetEquipment() => await Http.Get("/api/equipment");

    [McpServerTool(Name = "reframework_get_map")]
    [Description("Get current stage/area, stage name, previous stage, and active quest info (time remaining, elapsed, quest ID)")]
    public static async Task<string> GetMap() => await Http.Get("/api/map");

    [McpServerTool(Name = "reframework_get_monsters")]
    [Description("Get large monsters on the current map: name, species, HP, max HP, position, distance from player")]
    public static async Task<string> GetMonsters() => await Http.Get("/api/monsters");

    [McpServerTool(Name = "reframework_get_weather")]
    [Description("Get current weather, next weather, blend rates, in-game clock time")]
    public static async Task<string> GetWeather() => await Http.Get("/api/weather");

    [McpServerTool(Name = "reframework_get_lobby")]
    [Description("Get lobby members: names, hunter ranks, weapon types, who is on quest")]
    public static async Task<string> GetLobby() => await Http.Get("/api/lobby");

    [McpServerTool(Name = "reframework_get_camera")]
    [Description("Get camera position (x,y,z), FOV, near/far clip planes")]
    public static async Task<string> GetCamera() => await Http.Get("/api/camera");

    [McpServerTool(Name = "reframework_get_tdb")]
    [Description("Get type database stats: total types, methods, fields, properties count")]
    public static async Task<string> GetTdb() => await Http.Get("/api/tdb");
}

// ── Explorer read endpoints ─────────────────────────────────────────

[McpServerToolType]
public static class ExplorerReadTools
{
    [McpServerTool(Name = "reframework_search_types")]
    [Description("Search the type database by substring. Returns matching type names with field/method counts. Use this to discover types.")]
    public static async Task<string> SearchTypes(
        [Description("Substring to search (case-insensitive)")] string query,
        [Description("Max results (default 50)")] int? limit = null)
        => await Http.Get("/api/explorer/search", new() { ["query"] = query, ["limit"] = limit?.ToString() });

    [McpServerTool(Name = "reframework_get_type")]
    [Description("Get full type schema: all fields (with types/offsets) and methods (with signatures/return types). No live instance needed. By default returns only the type's own members (not inherited). Use includeInherited=true to get the full hierarchy. For large Def/utility types (e.g. EnemyDef, WeaponDef) that have hundreds of static fields, use noFields=true to skip fields and only see methods.")]
    public static async Task<string> GetType(
        [Description("Full type name, e.g. 'app.PlayerManager'")] string typeName,
        [Description("Include inherited fields/methods from parent types (default false)")] bool? includeInherited = null,
        [Description("Skip all fields")] bool? noFields = null,
        [Description("Skip all methods")] bool? noMethods = null)
        => await Http.Get("/api/explorer/type", new() {
            ["typeName"] = typeName,
            ["includeInherited"] = includeInherited?.ToString()?.ToLower(),
            ["noFields"] = noFields?.ToString()?.ToLower(),
            ["noMethods"] = noMethods?.ToString()?.ToLower()
        });

    [McpServerTool(Name = "reframework_get_singleton")]
    [Description("Find a singleton instance by exact type name. Returns address, kind, typeName.")]
    public static async Task<string> GetSingleton(
        [Description("Full type name of the singleton")] string typeName)
        => await Http.Get("/api/explorer/singleton", new() { ["typeName"] = typeName });

    [McpServerTool(Name = "reframework_get_singletons")]
    [Description("List all managed and native singletons with their addresses")]
    public static async Task<string> GetSingletons() => await Http.Get("/api/explorer/singletons");

    [McpServerTool(Name = "reframework_inspect_object")]
    [Description("Full detailed inspect of an object: field values (primitives inline), method signatures with params, array info. PREFER reframework_summary first for a lightweight scan — this tool returns much more data. Use the fields/methods filters or noMethods=true to limit output when you only need specific details.")]
    public static async Task<string> InspectObject(
        [Description("Object address (0xHEX)")] string address,
        [Description("Object kind")] string kind,
        [Description("Full type name")] string typeName,
        [Description("Comma-separated field names to include (omit for all)")] string? fields = null,
        [Description("Comma-separated method names to include (omit for all)")] string? methods = null,
        [Description("Skip all fields")] bool? noFields = null,
        [Description("Skip all methods")] bool? noMethods = null)
        => await Http.Get("/api/explorer/object", new() {
            ["address"] = address, ["kind"] = kind, ["typeName"] = typeName,
            ["fields"] = fields, ["methods"] = methods,
            ["noFields"] = noFields?.ToString()?.ToLower(), ["noMethods"] = noMethods?.ToString()?.ToLower()
        });

    [McpServerTool(Name = "reframework_summary")]
    [Description("Quick lightweight overview of an object's fields and methods as compact strings. Returns 'name: Type = value' for fields and 'name(params) → ReturnType' for methods. Use this FIRST to scan an object before using inspect_object for full details on specific fields/methods. Much cheaper on tokens than inspect_object.")]
    public static async Task<string> Summary(
        [Description("Object address (0xHEX)")] string address,
        [Description("Object kind")] string kind,
        [Description("Full type name")] string typeName)
        => await Http.Get("/api/explorer/summary", new() {
            ["address"] = address, ["kind"] = kind, ["typeName"] = typeName
        });

    [McpServerTool(Name = "reframework_read_field")]
    [Description("Resolve a reference-type field to get the child object's address/kind/typeName. For navigating object graphs.")]
    public static async Task<string> ReadField(
        [Description("Parent object address (0xHEX)")] string address,
        [Description("Object kind")] string kind,
        [Description("Parent type name")] string typeName,
        [Description("Field name to resolve")] string fieldName)
        => await Http.Get("/api/explorer/field", new() {
            ["address"] = address, ["kind"] = kind, ["typeName"] = typeName, ["fieldName"] = fieldName
        });

    [McpServerTool(Name = "reframework_call_method")]
    [Description("Call a 0-parameter getter method (get_*, Get*, ToString) and return its result. For reading properties.")]
    public static async Task<string> CallMethod(
        [Description("Object address (0xHEX)")] string address,
        [Description("Object kind")] string kind,
        [Description("Full type name")] string typeName,
        [Description("Method name (e.g. 'get_MaxFps')")] string methodName,
        [Description("Optional signature to disambiguate overloads")] string? methodSignature = null)
        => await Http.Get("/api/explorer/method", new() {
            ["address"] = address, ["kind"] = kind, ["typeName"] = typeName,
            ["methodName"] = methodName, ["methodSignature"] = methodSignature
        });

    [McpServerTool(Name = "reframework_get_array")]
    [Description("Read array elements with pagination. Returns element values or object references.")]
    public static async Task<string> GetArray(
        [Description("Array object address (0xHEX)")] string address,
        [Description("Object kind")] string kind,
        [Description("Array type name")] string typeName,
        [Description("Start index (default 0)")] int? offset = null,
        [Description("Number of elements (default 50)")] int? count = null)
        => await Http.Get("/api/explorer/array", new() {
            ["address"] = address, ["kind"] = kind, ["typeName"] = typeName,
            ["offset"] = offset?.ToString(), ["count"] = count?.ToString()
        });
}

// ── Explorer write endpoints ────────────────────────────────────────

[McpServerToolType]
public static class ExplorerWriteTools
{
    [McpServerTool(Name = "reframework_invoke_method")]
    [Description("Invoke any method with arguments, including static methods (omit address/kind). Supports int, float, bool, string, enum, struct args. NOTE: For GUID-to-text resolution, use reframework_localize_guid instead of calling via.gui.message.get() through this tool.")]
    public static async Task<string> InvokeMethod(
        [Description("Full type name")] string typeName,
        [Description("Method name")] string methodName,
        [Description("Object address (0xHEX). Omit for static methods.")] string? address = null,
        [Description("Omit for static methods.")] string? kind = null,
        [Description("Optional signature to disambiguate overloads")] string? methodSignature = null,
        [Description("Method arguments as JSON array of {value, type?} objects")] JsonElement? args = null)
    {
        var body = new Dictionary<string, object?> {
            ["typeName"] = typeName, ["methodName"] = methodName,
            ["address"] = address, ["kind"] = kind, ["methodSignature"] = methodSignature,
        };
        if (args is { } a) body["args"] = a;
        return await Http.Post("/api/explorer/method", body);
    }

    [McpServerTool(Name = "reframework_write_field")]
    [Description("Write a value to a value-type field (int, float, bool, etc.)")]
    public static async Task<string> WriteField(
        [Description("Object address (0xHEX)")] string address,
        [Description("Object kind")] string kind,
        [Description("Full type name")] string typeName,
        [Description("Field name to write")] string fieldName,
        [Description("New value (JSON)")] JsonElement value,
        [Description("Optional type hint (int, float, bool, string, etc.)")] string? valueType = null)
        => await Http.Post("/api/explorer/field", new {
            address, kind, typeName, fieldName, value, valueType
        });

    [McpServerTool(Name = "reframework_batch")]
    [Description("Execute multiple operations in one request. Each operation has a type (singleton, object, field, method, search, type, setField) and params matching the individual endpoint. Errors in one operation don't abort others.")]
    public static async Task<string> Batch(
        [Description("Array of operations")] JsonElement operations)
        => await Http.Post("/api/explorer/batch", new { operations });

    [McpServerTool(Name = "reframework_chain")]
    [Description("Navigate a path of fields/methods/arrays from a starting point. Steps: field (follow ref to child object), method (call 0-arg method, ONLY continues if result is an object — use for navigation not value reads), array (expand into elements, fans out — ONLY works on actual System.Array types like T[], NOT on collection wrappers like List/RingBuffer/etc; for those, first navigate to the inner array field e.g. _Buffer or call get_Array), filter (keep objects where method result matches value), collect (TERMINAL — reads multiple 0-arg methods on each object and returns their values; use this for reading primitives like get_Size, get_Count, get_Name etc). If no collect step, returns object addresses. IMPORTANT: to read a primitive/value-type return at the end of a chain, always use collect as the last step.")]
    public static async Task<string> Chain(
        [Description("Starting point: { singleton: 'typeName' } or { address, kind, typeName }")] JsonElement start,
        [Description("Array of step objects. Each has 'type' (field/method/array/filter/collect) and type-specific params: field={name}, method={name, signature?}, array={offset?, count?}, filter={method, value?='True'}, collect={methods: string[]}")] JsonElement steps)
        => await Http.Post("/api/explorer/chain", new { start, steps });
}

// ── Materials endpoints ─────────────────────────────────────────────

[McpServerToolType]
public static class MaterialTools
{
    [McpServerTool(Name = "reframework_get_materials")]
    [Description("Get per-material visibility for all player meshes. Returns mesh settings with material names and enabled states.")]
    public static async Task<string> GetMaterials() => await Http.Get("/api/materials");

    [McpServerTool(Name = "reframework_set_material_visibility")]
    [Description("Toggle an individual material on a player mesh piece on/off")]
    public static async Task<string> SetMaterialVisibility(
        [Description("GameObject name (e.g. 'ch03_013_0013')")] string gameObject,
        [Description("Material index within the mesh")] int materialIndex,
        [Description("true to show, false to hide")] bool enabled)
        => await Http.Post("/api/materials", new { gameObject, materialIndex, enabled });
}

// ── Localization ────────────────────────────────────────────────────

[McpServerToolType]
public static class LocalizationTools
{
    [McpServerTool(Name = "reframework_localize_guid")]
    [Description("PREFERRED way to resolve message GUID(s) to localized text. Always use this instead of manually calling via.gui.message.get() through invoke_method. Supports batch: pass multiple comma-separated GUIDs to resolve them all in one call.")]
    public static async Task<string> LocalizeGuid(
        [Description("One or more GUIDs, comma-separated (e.g. 'guid1,guid2,guid3'). Single GUID returns {guid, text}. Multiple returns {count, results: [{guid, text}, ...]}")] string guid)
        => await Http.Get("/api/localize", new() { ["guid"] = guid });
}

// ── Player write endpoints ──────────────────────────────────────────

[McpServerToolType]
public static class PlayerWriteTools
{
    [McpServerTool(Name = "reframework_set_health")]
    [Description("Set the player's current health value")]
    public static async Task<string> SetHealth(
        [Description("New health value")] double value)
        => await Http.Post("/api/player/health", new { value });

    [McpServerTool(Name = "reframework_set_position")]
    [Description("Set the player's position. Can update individual axes (partial update OK).")]
    public static async Task<string> SetPosition(
        [Description("X coordinate")] double? x = null,
        [Description("Y coordinate")] double? y = null,
        [Description("Z coordinate")] double? z = null)
        => await Http.Post("/api/player/position", new { x, y, z });
}
