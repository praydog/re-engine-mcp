using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Runtime.InteropServices;
using REFrameworkNET;
using REFrameworkNET.Attributes;

class REFrameworkWebAPI {
    static HttpListener s_listener;
    static Thread s_thread;
    static CancellationTokenSource s_cts = new();
    static int s_port = 8899;
    static string s_webRoot;

    static readonly Dictionary<string, string> s_mimeTypes = new() {
        { ".html", "text/html; charset=utf-8" },
        { ".css",  "text/css; charset=utf-8" },
        { ".js",   "application/javascript; charset=utf-8" },
    };

    // Enum boxing helper — method name differs across RE Engine versions.
    // RE9+: InternalBoxEnum(RuntimeType, Int64), RE2/older: boxEnum(Type, Int64)
    static REFrameworkNET.Method s_boxEnumMethod;
    static REFrameworkNET.Method GetBoxEnumMethod() {
        if (s_boxEnumMethod != null) return s_boxEnumMethod;
        var enumTd = TDB.Get().FindType("System.Enum");
        if (enumTd == null) return null;
        s_boxEnumMethod = enumTd.FindMethod("InternalBoxEnum") ?? enumTd.FindMethod("boxEnum");
        return s_boxEnumMethod;
    }
    static IObject BoxEnum(REFrameworkNET.TypeDefinition enumType, long value) {
        var m = GetBoxEnumMethod();
        if (m == null) return null;
        var rt = enumType.GetRuntimeType();
        if (rt == null) return null;
        var ret = m.Invoke(null, new object[] { rt, value });
        if (ret.Ptr == 0) return null;
        return ManagedObject.ToManagedObject(ret.Ptr) as IObject;
    }

    [PluginEntryPoint]
    public static void Main() {
        try {
            var pluginDir = API.GetPluginDirectory(typeof(REFrameworkWebAPI).Assembly);
            s_webRoot = Path.Combine(pluginDir, "WebAPI");

            if (!Directory.Exists(s_webRoot)) {
                API.LogError($"[WebAPI] WebAPI folder not found at {s_webRoot}");
                return;
            }

            s_listener = new HttpListener();
            s_listener.Prefixes.Add($"http://localhost:{s_port}/");
            s_listener.Start();

            s_thread = new Thread(ListenLoop) { IsBackground = true };
            s_thread.Start();

            API.LogInfo($"[WebAPI] Listening on http://localhost:{s_port}/ (serving from {s_webRoot})");
        } catch (Exception e) {
            API.LogError("[WebAPI] Failed to start: " + e.Message);
        }
    }

    [PluginExitPoint]
    public static void OnUnload() {
        s_cts.Cancel();
        s_listener?.Stop();
        s_thread?.Join(2000);
        s_listener?.Close();
        API.LogInfo("[WebAPI] Stopped");
    }

    static void ListenLoop() {
        while (!s_cts.IsCancellationRequested) {
            try {
                var ctx = s_listener.GetContext();
                ThreadPool.QueueUserWorkItem(_ => HandleRequest(ctx));
            } catch (HttpListenerException) {
                break;
            } catch (ObjectDisposedException) {
                break;
            }
        }
    }

    static void HandleRequest(HttpListenerContext ctx) {
        if (s_cts.IsCancellationRequested) return;
        bool calledGameApi = false;
        try {
            var path = ctx.Request.Url.AbsolutePath.TrimEnd('/').ToLower();

            // API endpoints
            if (path.StartsWith("/api")) {
                calledGameApi = true;
                var method = ctx.Request.HttpMethod;

                // POST endpoints
                if (method == "POST") {
                    object postResult = path switch {
#if MHWILDS
                        "/api/player/health" => SetPlayerHealth(ctx.Request),
                        "/api/player/position" => SetPlayerPosition(ctx.Request),
                        "/api/meshes" => SetMeshVisibility(ctx.Request),
                        "/api/materials" => SetMaterialVisibility(ctx.Request),
                        "/api/chat" => SendChat(ctx.Request),
#elif RE9
                        "/api/player/health" => SetPlayerHealthRE9(ctx.Request),
#elif RE2
                        "/api/player/health" => SetPlayerHealthRE2(ctx.Request),
                        "/api/player/position" => SetPlayerPositionRE2(ctx.Request),
#endif
                        "/api/explorer/field" => PostExplorerField(ctx.Request),
                        "/api/explorer/method" => PostExplorerMethod(ctx.Request),
                        "/api/explorer/batch" => PostExplorerBatch(ctx.Request),
                        "/api/explorer/chain" => PostExplorerChain(ctx.Request),
                        _ => null
                    };

                    if (postResult == null) {
                        ctx.Response.StatusCode = 404;
                        WriteJson(ctx.Response, new { error = "Not found" });
                        return;
                    }

                    WriteJson(ctx.Response, postResult);
                    return;
                }

                // Plain-text endpoints (not JSON)
                if (path == "/api/help") {
                    var agentMd = Path.Combine(s_webRoot, "AGENT.md");
                    if (File.Exists(agentMd)) {
                        var text = File.ReadAllText(agentMd);
                        ctx.Response.ContentType = "text/plain; charset=utf-8";
                        ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");
                        var bytes = Encoding.UTF8.GetBytes(text);
                        ctx.Response.OutputStream.Write(bytes);
                        ctx.Response.Close();
                        return;
                    }
                }

                // GET endpoints
                object result = path switch {
                    "/api" => GetIndex(),
                    "/api/camera" => GetCameraInfo(),
                    "/api/tdb" => GetTDBStats(),
                    "/api/singletons" => GetSingletonList(),
                    "/api/explorer/singletons" => GetExplorerSingletons(),
                    "/api/explorer/object" => GetExplorerObject(ctx.Request),
                    "/api/explorer/summary" => GetExplorerSummary(ctx.Request),
                    "/api/explorer/field" => GetExplorerField(ctx.Request),
                    "/api/explorer/method" => GetExplorerMethod(ctx.Request),
                    "/api/explorer/array" => GetExplorerArray(ctx.Request),
                    "/api/explorer/search" => GetExplorerSearch(ctx.Request),
                    "/api/explorer/type" => GetExplorerType(ctx.Request),
                    "/api/explorer/singleton" => GetExplorerSingleton(ctx.Request),
                    "/api/localize" => ResolveGuid(ctx.Request),
                    "/api/memory" => ReadMemory(ctx.Request),
                    "/api/memory/typed" => ReadMemoryTyped(ctx.Request),
#if MHWILDS
                    "/api/player" => GetPlayerInfo(),
                    "/api/lobby" => GetLobbyMembers(),
                    "/api/weather" => GetWeather(),
                    "/api/equipment" => GetEquipment(),
                    "/api/inventory" => GetInventory(),
                    "/api/meshes" => GetMeshList(),
                    "/api/materials" => GetMaterials(),
                    "/api/map" => GetMapInfo(),
                    "/api/gameinfo" => GetMapInfo(),
                    "/api/monsters" => GetMonsters(),
                    "/api/enemies" => GetMonsters(),
                    "/api/chat" => GetChatHistory(),
                    "/api/huntlog" => GetHuntLog(),
                    "/api/palico" => GetPalicoStats(),
                    "/api/debug/byref" => DebugByRef(),
#elif RE9
                    "/api/player" => GetPlayerInfoRE9(),
                    "/api/enemies" => GetEnemiesRE9(),
                    "/api/gameinfo" => GetGameInfoRE9(),
#elif RE2
                    "/api/player" => GetPlayerInfoRE2(),
                    "/api/enemies" => GetEnemiesRE2(),
                    "/api/gameinfo" => GetGameInfoRE2(),
                    "/api/inventory" => GetInventoryRE2(),
                    "/api/stalker" => GetStalkerRE2(),
                    "/api/stats" => GetStatsRE2(),
#endif
                    _ => null
                };

                if (result == null) {
                    ctx.Response.StatusCode = 404;
                    WriteJson(ctx.Response, new { error = "Not found" });
                    return;
                }

                WriteJson(ctx.Response, result);
                return;
            }

            // Static file serving
            ServeFile(ctx, path);
        } catch (ObjectDisposedException) {
            // Listener closed during hot-reload
        } catch (Exception e) {
            try {
                ctx.Response.StatusCode = 500;
                WriteJson(ctx.Response, new { error = e.Message });
            } catch (ObjectDisposedException) { }
        } finally {
            // Clean up thread-local managed objects created by game API calls.
            // Only call when we actually invoked game APIs — calling on a thread
            // that only served static files can crash if there's no frame to GC.
            if (calledGameApi) {
                try { API.LocalFrameGC(); } catch { }
            }
        }
    }

    static void ServeFile(HttpListenerContext ctx, string path) {
        if (path == "" || path == "/") path = "/index.html";

        // Sanitize: only allow filenames directly in WebAPI folder
        var fileName = Path.GetFileName(path);
        var filePath = Path.Combine(s_webRoot, fileName);

        if (!File.Exists(filePath)) {
            ctx.Response.StatusCode = 404;
            WriteJson(ctx.Response, new { error = "Not found" });
            return;
        }

        try {
            var ext = Path.GetExtension(fileName).ToLower();
            ctx.Response.ContentType = s_mimeTypes.GetValueOrDefault(ext, "application/octet-stream");
            var bytes = File.ReadAllBytes(filePath);
            ctx.Response.OutputStream.Write(bytes);
            ctx.Response.Close();
        } catch (ObjectDisposedException) { }
    }

    static void WriteJson(HttpListenerResponse response, object data) {
        try {
            response.ContentType = "application/json";
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            var json = JsonSerializer.SerializeToUtf8Bytes(data, new JsonSerializerOptions { WriteIndented = true });
            response.OutputStream.Write(json);
            response.Close();
        } catch (ObjectDisposedException) {
            // Listener was closed during hot-reload while request was in-flight
        }
    }

    static object GetIndex() {
        var endpoints = new List<string> {
            "/api/camera", "/api/tdb", "/api/singletons", "/api/localize", "/api/help",
            "/api/explorer/singletons", "/api/explorer/singleton",
            "/api/explorer/object", "/api/explorer/summary", "/api/explorer/field", "/api/explorer/method",
            "/api/explorer/array", "/api/explorer/search", "/api/explorer/type",
            "/api/explorer/batch", "/api/explorer/chain",
            "/api/memory", "/api/memory/typed"
        };
#if MHWILDS
        endpoints.AddRange(new[] { "/api/player", "/api/lobby", "/api/weather", "/api/equipment",
            "/api/inventory", "/api/meshes", "/api/materials", "/api/map", "/api/monsters",
            "/api/chat", "/api/huntlog", "/api/palico" });
#elif RE9
        endpoints.AddRange(new[] { "/api/player", "/api/enemies", "/api/gameinfo" });
#elif RE2
        endpoints.AddRange(new[] { "/api/player", "/api/enemies", "/api/gameinfo", "/api/inventory", "/api/stalker", "/api/stats" });
#endif
        return new {
            name = "REFramework.NET Web API",
            game = System.Diagnostics.Process.GetCurrentProcess().ProcessName,
            endpoints
        };
    }

    static string ResolveGuid(_System.Guid guid) {
        try { return via.gui.message.get(guid)?.ToString(); } catch { return null; }
    }

#if MHWILDS
    static object GetEquipment() {
        try {
            var pm = API.GetManagedSingletonT<app.PlayerManager>();
            if (pm == null) return new { error = "PlayerManager not available" };

            var player = pm.getMasterPlayer();
            if (player == null) return new { error = "Player is null" };

            var createInfo = player.ContextHolder.Hunter.CreateInfo;
            if (createInfo == null) return new { error = "CreateInfo not available" };

            var wpType = (app.WeaponDef.TYPE)createInfo._WpType;
            int wpId = (int)createInfo._WpID;

            string weaponName = null, weaponDesc = null;
            try { weaponName = ResolveGuid(app.WeaponDef.Name(wpType, wpId)); } catch { }
            try { weaponDesc = ResolveGuid(app.WeaponDef.Explain(wpType, wpId)); } catch { }
            if (weaponDesc == null) try { weaponDesc = ResolveGuid(app.WeaponDef.Data(wpType, wpId).Explain); } catch { }

            string wpTypeName = null;
            try { wpTypeName = wpType.ToString(); } catch { }

            int attack = 0, critical = 0, defense = 0, attributeValue = 0, subAttributeValue = 0;
            string attribute = null, subAttribute = null, rarity = null;
            int[] slotLevels = new int[3];
            try { attack = (int)app.WeaponDef.Attack(wpType, wpId); } catch { }
            try { critical = (int)app.WeaponDef.Critical(wpType, wpId); } catch { }
            try { defense = (int)app.WeaponDef.Defense(wpType, wpId); } catch { }
            try { attribute = app.WeaponDef.Attribute(wpType, wpId).ToString(); } catch { }
            try { attributeValue = (int)app.WeaponDef.AttributeValue(wpType, wpId); } catch { }
            try { subAttribute = app.WeaponDef.SubAttribute(wpType, wpId).ToString(); } catch { }
            try { subAttributeValue = (int)app.WeaponDef.SubAttributeValue(wpType, wpId); } catch { }
            try { rarity = app.WeaponDef.Rare(wpType, wpId).ToString(); } catch { }
            try {
                for (uint s = 0; s < 3; s++)
                    slotLevels[s] = (int)app.WeaponDef.SlotLevel(wpType, wpId, s);
            } catch { }

            var armorIds = new int[5];
            var armorLevels = new uint[5];
            try {
                var seriesArr = createInfo._ArmorSeriesID;
                var levelArr = createInfo._ArmorUpgradeLevel;
                for (int i = 0; i < 5; i++) {
                    armorIds[i] = seriesArr[i];
                    armorLevels[i] = levelArr[i];
                }
            } catch { }

            string[] armorSlotNames = { "Helm", "Body", "Arms", "Waist", "Legs" };
            var armorPieces = new List<object>();
            for (int i = 0; i < 5; i++) {
                string name = null, desc = null;
                if (armorIds[i] > 0) {
                    var parts = (app.ArmorDef.ARMOR_PARTS)i;
                    var series = (app.ArmorDef.SERIES)armorIds[i];
                    try { name = ResolveGuid(app.ArmorDef.Name(parts, series)); } catch { }
                    try { desc = ResolveGuid(app.ArmorDef.Explain(parts, series)); } catch { }
                }
                armorPieces.Add(new {
                    slot = armorSlotNames[i],
                    name = name ?? (armorIds[i] > 0 ? $"Series {armorIds[i]}" : "Empty"),
                    description = desc,
                    seriesId = armorIds[i],
                    upgradeLevel = armorLevels[i]
                });
            }

            // Palico equipment
            object palico = null;
            try {
                var om = API.GetManagedSingletonT<app.OtomoManager>();
                var otomoInfo = om?.getMasterOtomoInfo();
                if (otomoInfo != null && otomoInfo.Valid) {
                    var otomoCtx = otomoInfo.ContextHolder?.Otomo?.CreateInfo;
                    if (otomoCtx != null && otomoCtx._IsValid) {
                        var wpDataId = (app.OtEquipDef.EQUIP_DATA_ID)otomoCtx._WeaponDataId;
                        var helmDataId = (app.OtEquipDef.EQUIP_DATA_ID)otomoCtx._HeadDataId;
                        var bodyDataId = (app.OtEquipDef.EQUIP_DATA_ID)otomoCtx._ArmorDataId;

                        string wpName = null, helmName = null, bodyName = null;
                        string wpDesc = null, helmDesc = null, bodyDesc = null;
                        string wpRare = null, helmRare = null, bodyRare = null;
                        try {
                            var vdm = API.GetManagedSingletonT<app.VariousDataManager>();
                            var otWpData = vdm?.Setting?.EquipDatas?.OtomoWeaponData;
                            if (otWpData != null) {
                                var idx = vdm.Setting.EquipDatas.OtomoWeaponDataIndex[(int)wpDataId];
                                if (idx >= 0) {
                                    var cData = otWpData.getDataByIndex(idx);
                                    if (cData != null) {
                                        wpName = ResolveGuid(cData.Name);
                                        wpDesc = ResolveGuid(cData.Explain);
                                    }
                                }
                            }
                        } catch { }
                        if (wpName == null) try { wpName = ResolveGuid(app.OtEquipDef.Name(wpDataId)); } catch { }
                        try {
                            var d = app.OtEquipDef.Data(app.OtEquipDef.EQUIP_TYPE.HELM, helmDataId);
                            helmName = ResolveGuid(d.Name);
                            try { helmDesc = ResolveGuid(d.Explain); } catch { }
                        } catch { }
                        try {
                            var d = app.OtEquipDef.Data(app.OtEquipDef.EQUIP_TYPE.BODY, bodyDataId);
                            bodyName = ResolveGuid(d.Name);
                            try { bodyDesc = ResolveGuid(d.Explain); } catch { }
                        } catch { }
                        try { wpRare = app.OtEquipDef.Rare(wpDataId).ToString(); } catch { }
                        try { helmRare = app.OtEquipDef.Rare(helmDataId).ToString(); } catch { }
                        try { bodyRare = app.OtEquipDef.Rare(bodyDataId).ToString(); } catch { }

                        palico = new {
                            weapon = new { name = wpName ?? $"Weapon {otomoCtx._WeaponDataId}", description = wpDesc, rarity = wpRare },
                            helm = new { name = helmName ?? $"Helm {otomoCtx._HeadDataId}", description = helmDesc, rarity = helmRare },
                            body = new { name = bodyName ?? $"Body {otomoCtx._ArmorDataId}", description = bodyDesc, rarity = bodyRare }
                        };
                    }
                }
            } catch { }

            return new {
                weapon = new {
                    name = weaponName ?? $"Weapon {wpId}",
                    description = weaponDesc,
                    type = wpTypeName ?? wpType.ToString(),
                    typeId = (int)wpType,
                    id = wpId,
                    attack,
                    critical,
                    defense,
                    element = attribute,
                    elementValue = attributeValue,
                    subElement = subAttribute,
                    subElementValue = subAttributeValue,
                    rarity,
                    slots = slotLevels
                },
                armor = armorPieces,
                palico
            };
        } catch (Exception e) {
            return new { error = e.Message };
        }
    }

    static readonly Dictionary<string, string> s_stageNames = new() {
        { "ST101", "Windward Plains" },
        { "ST102", "Scarlet Forest" },
        { "ST103", "Oilwell Basin" },
        { "ST104", "Iceshard Cliffs" },
        { "ST105", "Wounded Hollow" },
        { "ST201", "Training Area" },
        { "ST202", "Arena" },
        { "ST203", "Forlorn Arena" },
        { "ST204", "Special Arena" },
        { "ST401", "Kunafa" },
        { "ST402", "Research Base" },
        { "ST403", "Aslana" },
        { "ST404", "Capcom HQ" },
        { "ST405", "Elder's Lair" },
        { "ST502", "Hub" },
        { "ST503", "Gathering Hub" },
    };

    static object GetMapInfo() {
        try {
            var fm = API.GetManagedSingletonT<app.MasterFieldManager>();
            if (fm == null) return new { error = "MasterFieldManager not available" };

            var currentStage = fm.CurrentStage;
            var prevStage = fm.PrevStage;
            var stageCode = currentStage.ToString().Split(' ')[0]; // "ST502 (14)" -> "ST502"
            var prevCode = prevStage.ToString().Split(' ')[0];

            s_stageNames.TryGetValue(stageCode, out var stageName);
            s_stageNames.TryGetValue(prevCode, out var prevName);

            // Quest info from MissionManager
            var mm = API.GetManagedSingletonT<app.MissionManager>();
            bool isQuest = false;
            bool isPlaying = false;
            string questId = null;
            float? questRemainTime = null;
            float? questElapsedTime = null;
            string questBeforeStage = null;

            if (mm != null) {
                try { isQuest = mm.IsActiveQuest; } catch { }
                try { isPlaying = mm.IsPlayingQuest; } catch { }
                try { questId = mm.AcceptedQuestID.ToString(); } catch { }

                if (isQuest) {
                    try {
                        var qd = mm.QuestDirector;
                        if (qd != null) {
                            try { questRemainTime = qd.QuestRemainTime; } catch { }
                            try { questElapsedTime = qd.QuestElapsedTime; } catch { }
                            try {
                                var bs = qd.QuestBeforeStage.ToString().Split(' ')[0];
                                s_stageNames.TryGetValue(bs, out questBeforeStage);
                                questBeforeStage = questBeforeStage ?? bs;
                            } catch { }
                        }
                    } catch { }
                }
            }

            return new {
                stage = stageCode,
                stageName = stageName ?? stageCode,
                areaNo = fm._CurrentAreaNo,
                prevStage = prevCode,
                prevStageName = prevName ?? prevCode,
                quest = new {
                    active = isQuest,
                    playing = isPlaying,
                    id = questId,
                    remainTime = questRemainTime,
                    elapsedTime = questElapsedTime,
                    beforeStage = questBeforeStage
                }
            };
        } catch (Exception e) {
            return new { error = e.Message };
        }
    }

    static object GetInventory() {
        try {
            var sdm = API.GetManagedSingletonT<app.SaveDataManager>();
            if (sdm == null) return new { error = "SaveDataManager not available" };

            var saves = sdm.UserSaveData;
            if (saves == null) return new { error = "No save data" };

            app.savedata.cUserSaveParam activeSave = null;
            for (int i = 0; i < saves.Length; i++) {
                if (saves[i] != null && saves[i].Active == 1) { activeSave = saves[i]; break; }
            }
            if (activeSave == null) return new { error = "No active save" };

            var itemParam = activeSave._Item;
            if (itemParam == null) return new { error = "Item data not available" };

            var pouch = itemParam._PouchItem;
            if (pouch == null) return new { error = "Pouch not available" };

            var items = new List<object>();
            for (int i = 0; i < pouch.Length; i++) {
                var slot = pouch[i];
                if (slot == null) continue;

                int num = (int)slot.Num;
                if (num <= 0) continue;

                app.ItemDef.ID itemId;
                try { itemId = slot.ItemId; } catch { itemId = (app.ItemDef.ID)slot.ItemIdFixed; }

                string name = null;
                try { name = app.ItemDef.NameString(itemId); } catch { }

                items.Add(new {
                    slotIndex = i,
                    id = (int)itemId,
                    name = name ?? $"Item {(int)itemId}",
                    quantity = num
                });
            }

            return new { count = items.Count, capacity = pouch.Length, items };
        } catch (Exception e) {
            return new { error = e.Message };
        }
    }

    static object GetPlayerInfo() {
        var pm = API.GetManagedSingletonT<app.PlayerManager>();
        if (pm == null) return new { error = "PlayerManager not available" };

        var player = pm.getMasterPlayer();
        if (player == null) return new { error = "Player is null" };

        var ctx = player.ContextHolder;
        var pl = ctx.Pl;

        float? posX = null, posY = null, posZ = null;
        try {
            var go = player.Object;
            if (go != null) {
                var tf = go.Transform;
                if (tf != null) {
                    var pos = tf.Position;
                    posX = pos.x; posY = pos.y; posZ = pos.z;
                }
            }
        } catch { }

        float? health = null, maxHealth = null;
        try {
            var hm = ctx.Chara.HealthManager;
            if (hm != null) {
                health = hm._Health.read();
                maxHealth = hm._MaxHealth.read();
            }
        } catch { }

        string otomoName = null, seikretName = null;
        int? zenny = null, points = null;
        uint? playTime = null;
        try {
            var sdm = API.GetManagedSingletonT<app.SaveDataManager>();
            if (sdm != null) {
                var saves = sdm.UserSaveData;
                if (saves != null) {
                    for (int i = 0; i < saves.Length; i++) {
                        if (saves[i] != null && saves[i].Active == 1) {
                            var basic = saves[i]._BasicData;
                            if (basic != null) {
                                try { otomoName = basic.OtomoName?.ToString(); } catch { }
                                try { seikretName = basic.SeikretName?.ToString(); } catch { }
                                try { zenny = basic.getMoney(); } catch { }
                                try { points = basic.getPoint(); } catch { }
                            }
                            try { playTime = saves[i].PlayTime; } catch { }
                            break;
                        }
                    }
                }
            }
        } catch { }

        return new {
            name = pl._PlayerName?.ToString(),
            level = (int)pl._CurrentStage,
            health,
            maxHealth,
            zenny,
            points,
            playTimeSeconds = playTime,
            position = new { x = posX, y = posY, z = posZ },
            generalPos = new { x = pl._GeneralPos.x, y = pl._GeneralPos.y, z = pl._GeneralPos.z },
            distToCamera = pl._DistToCamera,
            isMasterRow = pl._NetMemberInfo.IsMasterRow,
            otomoName,
            seikretName
        };
    }

    static object SetPlayerHealth(HttpListenerRequest request) {
        using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
        var body = reader.ReadToEnd();
        var doc = JsonDocument.Parse(body);
        var value = doc.RootElement.GetProperty("value").GetSingle();

        var pm = API.GetManagedSingletonT<app.PlayerManager>();
        if (pm == null) return new { error = "PlayerManager not available" };

        var player = pm.getMasterPlayer();
        if (player == null) return new { error = "Player is null" };

        var hm = player.ContextHolder.Chara.HealthManager;
        if (hm == null) return new { error = "HealthManager not available" };

        hm._Health.write(value);
        return new { ok = true, health = value };
    }

    static object SetPlayerPosition(HttpListenerRequest request) {
        try {
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            var body = reader.ReadToEnd();
            var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var pm = API.GetManagedSingletonT<app.PlayerManager>();
            if (pm == null) return new { error = "PlayerManager not available" };

            var player = pm.getMasterPlayer();
            if (player == null) return new { error = "Player is null" };

            var go = player.Object;
            if (go == null) return new { error = "GameObject is null" };

            var tf = go.Transform;
            if (tf == null) return new { error = "Transform is null" };

            var pos = tf.Position;

            if (root.TryGetProperty("x", out var xProp)) pos.x = xProp.GetSingle();
            if (root.TryGetProperty("y", out var yProp)) pos.y = yProp.GetSingle();
            if (root.TryGetProperty("z", out var zProp)) pos.z = zProp.GetSingle();

            tf.Position = pos;

            return new { ok = true, position = new { x = pos.x, y = pos.y, z = pos.z } };
        } catch (Exception e) {
            return new { error = e.Message };
        }
    }

    static readonly Dictionary<string, string> s_meshLabels = new() {
        { "Player_Face", "Face" },
        { "SlingerRope", "Slinger Rope" },
        { "HeadToHip", "Head-to-Hip" },
    };

    static string GuessMeshLabel(string name) {
        if (s_meshLabels.TryGetValue(name, out var label)) return label;
        if (name.StartsWith("Acc")) return "Accessory";
        if (name.StartsWith("ch02_")) return "Body";
        if (name.StartsWith("Wp_")) return "Weapon";
        if (name.StartsWith("WpSub_")) return "Sub Weapon";
        return name;
    }

    static List<(via.GameObject go, string name)> GetPlayerChildObjects() {
        var pm = API.GetManagedSingletonT<app.PlayerManager>();
        if (pm == null) return null;
        var player = pm.getMasterPlayer();
        if (player == null) return null;
        var go = player.Object;
        if (go == null) return null;
        var tf = go.Transform;
        if (tf == null) return null;

        var children = new List<(via.GameObject, string)>();
        var child = tf.Child;
        while (child != null) {
            try {
                var childGo = child.GameObject;
                if (childGo != null) {
                    children.Add((childGo, childGo.Name));
                }
            } catch { }
            try { child = child.Next; } catch { break; }
        }
        return children;
    }

    static object GetMeshList() {
        try {
            var children = GetPlayerChildObjects();
            if (children == null) return new { error = "Player not available" };

            var meshes = new List<object>();
            foreach (var (go, name) in children) {
                try {
                    meshes.Add(new {
                        name,
                        label = GuessMeshLabel(name),
                        visible = go.DrawSelf
                    });
                } catch { }
            }
            return new { count = meshes.Count, meshes };
        } catch (Exception e) {
            return new { error = e.Message };
        }
    }

    static object SetMeshVisibility(HttpListenerRequest request) {
        try {
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            var body = reader.ReadToEnd();
            var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var targetName = root.GetProperty("name").GetString();
            var visible = root.GetProperty("visible").GetBoolean();

            var children = GetPlayerChildObjects();
            if (children == null) return new { error = "Player not available" };

            foreach (var (go, name) in children) {
                if (name == targetName) {
                    go.DrawSelf = visible;
                    return new { ok = true, name, visible };
                }
            }
            return new { error = $"Mesh '{targetName}' not found" };
        } catch (Exception e) {
            return new { error = e.Message };
        }
    }

    static REFrameworkNET.IObject GetPlayerMeshSettingController() {
        try {
            var pm = API.GetManagedSingletonT<app.PlayerManager>();
            if (pm == null) return null;
            var player = pm.getMasterPlayer();
            if (player == null) return null;
            var go = player.Object;
            if (go == null) return null;

            var components = go.Components;
            if (components == null) return null;

            for (int i = 0; i < components.Length; i++) {
                var comp = components[i];
                if (comp == null) continue;
                var iobj = comp as REFrameworkNET.IObject;
                if (iobj == null) continue;
                var tname = iobj.GetTypeDefinition()?.GetFullName();
                if (tname == "app.MeshSettingController")
                    return iobj;
            }
            return null;
        } catch {
            return null;
        }
    }

    static List<(REFrameworkNET.IObject meshSetting, string goName)> GetPlayerMeshSettings() {
        var ctrl = GetPlayerMeshSettingController();
        if (ctrl == null) return null;

        // get_MeshSettingsAll() returns an IEnumerable (C# iterator state machine).
        // Must call the explicit interface GetEnumerator to get a properly initialized enumerator.
        var enumerable = ctrl.Call("get_MeshSettingsAll") as REFrameworkNET.IObject;
        if (enumerable == null) return null;

        var enumerator = enumerable.Call("System.Collections.IEnumerable.GetEnumerator") as REFrameworkNET.IObject;
        if (enumerator == null) return null;

        var results = new List<(REFrameworkNET.IObject, string)>();
        int safety = 0;
        while (safety++ < 100) {
            object moveResult = null;
            try { moveResult = enumerator.Call("MoveNext"); } catch { break; }
            if (moveResult == null || !(bool)moveResult) break;

            var msObj = enumerator.Call("System.Collections.IEnumerator.get_Current") as REFrameworkNET.IObject;
            if (msObj == null) continue;

            string goName = "";
            try {
                var goObj = msObj.Call("get_GameObject") as REFrameworkNET.IObject;
                if (goObj != null) goName = goObj.Call("get_Name") as string ?? "";
            } catch { }

            results.Add((msObj, goName));
        }
        return results;
    }

    static object GetMaterials() {
        try {
            var meshSettings = GetPlayerMeshSettings();
            if (meshSettings == null) return new { error = "MeshSettingController not available" };

            var meshSettingsList = new List<object>();
            foreach (var (msObj, goName) in meshSettings) {
                try {
                    var meshObj = msObj.Call("get_Mesh") as REFrameworkNET.IObject;
                    if (meshObj == null) continue;

                    uint matNum = 0;
                    try { matNum = (uint)meshObj.Call("get_MaterialNum"); } catch { }

                    var materials = new List<object>();
                    for (uint j = 0; j < matNum; j++) {
                        string matName = null;
                        try { matName = meshObj.Call("getMaterialName", (object)j) as string; } catch { }

                        bool matEnabled = true;
                        try { matEnabled = (bool)meshObj.Call("getMaterialsEnable", (object)(ulong)j); } catch { }

                        materials.Add(new {
                            index = j,
                            name = matName ?? $"Material {j}",
                            enabled = matEnabled
                        });
                    }

                    bool visible = true;
                    try { visible = (bool)msObj.Call("get_Visible"); } catch { }

                    meshSettingsList.Add(new {
                        gameObject = goName,
                        label = GuessMeshLabel(goName),
                        materialCount = matNum,
                        visible,
                        materials
                    });
                } catch { }
            }
            return new { count = meshSettingsList.Count, meshSettings = meshSettingsList };
        } catch (Exception e) {
            return new { error = e.Message };
        }
    }

    static object SetMaterialVisibility(HttpListenerRequest request) {
        try {
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            var body = reader.ReadToEnd();
            var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var targetGo = root.GetProperty("gameObject").GetString();
            var materialIndex = root.GetProperty("materialIndex").GetUInt32();
            var enabled = root.GetProperty("enabled").GetBoolean();

            var meshSettings = GetPlayerMeshSettings();
            if (meshSettings == null) return new { error = "MeshSettingController not available" };

            foreach (var (msObj, goName) in meshSettings) {
                if (goName == targetGo) {
                    var meshObj = msObj.Call("get_Mesh") as REFrameworkNET.IObject;
                    if (meshObj == null) return new { error = "No mesh on this MeshSetting" };
                    meshObj.Call("setMaterialsEnable", (object)(ulong)materialIndex, (object)enabled);
                    return new { ok = true, gameObject = targetGo, materialIndex, enabled };
                }
            }
            return new { error = $"MeshSetting for '{targetGo}' not found" };
        } catch (Exception e) {
            return new { error = e.Message };
        }
    }

    static object SendChat(HttpListenerRequest request) {
        try {
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            var body = reader.ReadToEnd();
            var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var message = root.GetProperty("message").GetString();
            if (string.IsNullOrEmpty(message)) return new { error = "message is required" };

            var chatMgr = API.GetManagedSingletonT<app.ChatManager>();
            if (chatMgr == null) return new { error = "ChatManager not available" };

            var iobj = chatMgr as REFrameworkNET.IObject;
            if (iobj == null) return new { error = "Cannot get IObject for ChatManager" };

            iobj.Call("sendText", (object)message);

            return new { ok = true, message };
        } catch (Exception e) {
            return new { error = e.Message };
        }
    }
#endif

    static object ResolveGuid(HttpListenerRequest req) {
        try {
            var guidStr = req.QueryString["guid"];
            if (string.IsNullOrEmpty(guidStr)) return new { error = "Missing 'guid' parameter" };

            // Support comma-separated GUIDs for batch resolution
            var guids = guidStr.Split(',');
            if (guids.Length == 1) {
                var text = via.gui.message.get(_System.Guid.Parse(guidStr.Trim()))?.ToString();
                return new { guid = guidStr, text = text ?? "" };
            }

            var results = new List<object>();
            foreach (var g in guids) {
                var trimmed = g.Trim();
                try {
                    var text = via.gui.message.get(_System.Guid.Parse(trimmed))?.ToString();
                    results.Add(new { guid = trimmed, text = text ?? "" });
                } catch {
                    results.Add(new { guid = trimmed, text = "", error = "Failed to parse" });
                }
            }
            return new { count = results.Count, results };
        } catch (Exception e) {
            return new { error = e.Message };
        }
    }

#if MHWILDS
    static object DebugByRef() {
        try {
            var tdef = REFrameworkNET.TDB.Get().FindType("ace.cFixedRingBuffer`1<app.ChatDef.MessageElement>");
            if (tdef == null) return new { error = "Type not found" };

            var methods = tdef.GetMethods();
            var results = new List<object>();
            foreach (var m in methods) {
                var name = m.GetName();
                if (name == "get_Item" || name == "front" || name == "back" || name == "get_Size") {
                    var retType = m.GetReturnType();
                    results.Add(new {
                        method = name,
                        returnType = retType?.GetFullName(),
                        isByRef = retType?.IsByRef(),
                        isValueType = retType?.IsValueType(),
                        isPointer = retType?.IsPointer(),
                    });
                }
            }
            return new { results };
        } catch (Exception e) {
            return new { error = e.Message };
        }
    }

    static object GetChatHistory() {
        try {
            var chatMgr = API.GetManagedSingletonT<app.ChatManager>();
            if (chatMgr is null) return new { error = "ChatManager not available" };

            var logObj = (chatMgr as REFrameworkNET.IObject)?.GetField("_AllLog") as REFrameworkNET.IObject;
            if (logObj is null) return new { error = "_AllLog is null" };

            int size = 0;
            try { var s = logObj.Call("get_Size"); if (s != null) size = (int)s; } catch { }

            // Cache via.gui.message.get(Guid) for resolving system message GUIDs
            REFrameworkNET.Method messageGetMethod = null;
            try {
                var msgTdef = REFrameworkNET.TDB.Get().FindType("via.gui.message");
                messageGetMethod = msgTdef?.GetMethod("get");
            } catch { }

            var messages = new List<object>();

            for (int i = 0; i < size; i++) {
                try {
                    var elem = logObj.Call("get_Item", (object)i) as REFrameworkNET.IObject;
                    if (elem is null) continue;

                    string typeName = null;
                    try { typeName = elem.GetTypeDefinition()?.GetFullName(); } catch { }

                    string msgType = null;
                    try { msgType = elem.GetField("<MsgType>k__BackingField")?.ToString(); } catch { }

                    string text = null, sender = null, target = null;
                    bool isChatBase = typeName != null && (
                        typeName.Contains("ChatBase") || typeName.Contains("ChatMessage") ||
                        typeName.Contains("ChatSystemLog") || typeName.Contains("ChatSystemSendLog"));

                    if (isChatBase) {
                        try { sender = elem.GetField("_SenderName") as string; } catch { }
                        try { target = elem.GetField("<SendTarget>k__BackingField")?.ToString(); } catch { }
                    }

                    // ChatMessage has <Text>k__BackingField for user-typed text
                    if (typeName != null && typeName.Contains("ChatMessage")) {
                        try { text = elem.GetField("<Text>k__BackingField") as string; } catch { }
                    }

                    // If no direct text, try resolving the MessageInfo GUID to localized text
                    if (string.IsNullOrEmpty(text)) {
                        try {
                            var msgInfo = elem.GetField("<MessageInfo>k__BackingField") as REFrameworkNET.IObject;
                            if (msgInfo != null && messageGetMethod != null) {
                                var msgId = msgInfo.GetField("<MsgID>k__BackingField");
                                if (msgId != null) {
                                    text = messageGetMethod.InvokeBoxed(typeof(string), null, new object[] { msgId }) as string;
                                }

                                // Substitute {0}, {1}, etc. with paramToString() results
                                if (!string.IsNullOrEmpty(text) && text.Contains("{0}")) {
                                    try {
                                        var paramArray = msgInfo.Call("paramToString") as REFrameworkNET.IObject;
                                        if (paramArray != null) {
                                            var arrTdef = paramArray.GetTypeDefinition();
                                            int len = 0;
                                            try { len = (int)paramArray.Call("get_Length"); } catch { }
                                            for (int p = 0; p < len; p++) {
                                                var paramVal = paramArray.Call("Get", (object)p);
                                                var paramStr = (paramVal as REFrameworkNET.IObject)?.Call("ToString") as string
                                                    ?? paramVal?.ToString() ?? "";
                                                text = text.Replace($"{{{p}}}", paramStr);
                                            }
                                        }
                                    } catch { }
                                }

                                // Strip markup tags: <BOLD>x</BOLD> -> x, <PLURAL n "singular" "plural"> -> pick by n
                                if (!string.IsNullOrEmpty(text)) {
                                    text = System.Text.RegularExpressions.Regex.Replace(text, @"<BOLD>(.*?)</BOLD>", "$1");
                                    text = System.Text.RegularExpressions.Regex.Replace(text, @"<PLURAL\s+(\d+)\s+""([^""]*)""\s+""([^""]*)"">",
                                        m => m.Groups[1].Value == "1" ? m.Groups[2].Value : m.Groups[3].Value);
                                    text = text.Replace("\r\n", " ");
                                }
                            }
                        } catch { }
                    }

                    messages.Add(new {
                        type = msgType ?? "unknown",
                        sender = sender ?? "System",
                        text = text ?? "",
                        target,
                        elementType = typeName
                    });
                } catch {
                    // Skip this element
                }
            }

            return new { count = messages.Count, messages };
        } catch (Exception e) {
            return new { error = e.Message };
        }
    }

    // ── Monsters (Large Enemies) ───────────────────────────────────

    static object GetMonsters() {
        try {
            var em = API.GetManagedSingletonT<app.EnemyManager>();
            if (em == null) return new { error = "EnemyManager not available" };

            // Player position for distance calculation
            float? playerX = null, playerY = null, playerZ = null;
            try {
                var pm = API.GetManagedSingletonT<app.PlayerManager>();
                if (pm != null) {
                    var player = pm.getMasterPlayer();
                    if (player?.Object?.Transform != null) {
                        var pp = player.Object.Transform.Position;
                        playerX = pp.x; playerY = pp.y; playerZ = pp.z;
                    }
                }
            } catch { }

            var nameMap = GetMonsterNameMap();
            var monsters = new List<object>();

            var emObj = em as REFrameworkNET.IObject;
            var listObj = emObj?.GetField("_EnemyList") as REFrameworkNET.IObject;
            if (listObj == null) return new { count = 0, monsters };

            int count = 0;
            try { count = (int)listObj.Call("get_Count"); } catch { }

            for (int i = 0; i < count; i++) {
                REFrameworkNET.IObject info;
                try { info = listObj.Call("get_Item", (object)i) as REFrameworkNET.IObject; } catch { continue; }
                if (info == null) continue;

                try {
                    bool charValid = false;
                    try { charValid = (bool)info.Call("get_CharacterValid"); } catch { }
                    if (!charValid) continue;

                    var ctx = info.Call("get_Context") as REFrameworkNET.IObject;
                    if (ctx == null) continue;

                    var emCtx = ctx.Call("get_Em") as REFrameworkNET.IObject;
                    if (emCtx == null) continue;

                    bool isBoss = false;
                    try { isBoss = (bool)emCtx.Call("get_IsBoss"); } catch { }
                    if (!isBoss) continue;

                    int emIdInt = 0;
                    try { emIdInt = (int)emCtx.Call("get_EmID"); } catch { continue; }
                    var emId = (app.EnemyDef.ID)emIdInt;

                    // Name
                    string name = null;
                    try {
                        int fixedId = (int)app.EnemyDef.enemyId(emId);
                        nameMap.TryGetValue(fixedId, out name);
                    } catch { }
                    if (string.IsNullOrEmpty(name)) {
                        try { name = app.EnemyDef.NameString(emId, 0, 0); } catch { }
                    }

                    // Species — SPECIES_Fixed has +1 offset vs SPECIES (INVARID=0 vs -1)
                    string species = null;
                    try {
                        var speciesFixed = app.EnemyDef.Species(emId);
                        var speciesEnum = (app.EnemyDef.SPECIES)((int)speciesFixed - 1);
                        var speciesGuid = app.EnemyDef.EmSpeciesName(speciesEnum, emId);
                        species = ResolveGuid(speciesGuid);
                    } catch { }

                    // HP
                    float? health = null, maxHealth = null;
                    try {
                        var charaCtx = ctx.Call("get_Chara") as REFrameworkNET.IObject;
                        var hm = charaCtx?.Call("get_HealthManager") as REFrameworkNET.IObject;
                        if (hm != null) {
                            try { health = (float)hm.Call("get_Health"); } catch { }
                            try { maxHealth = (float)hm.Call("get_MaxHealth"); } catch { }
                        }
                    } catch { }

                    // Position
                    float? posX = null, posY = null, posZ = null;
                    try {
                        var pos = info.Call("get_Pos");
                        if (pos is REFrameworkNET.IObject posObj) {
                            posX = (float)posObj.GetField("x");
                            posY = (float)posObj.GetField("y");
                            posZ = (float)posObj.GetField("z");
                        }
                    } catch { }

                    // Distance from player
                    float? distance = null;
                    if (playerX.HasValue && posX.HasValue) {
                        var dx = posX.Value - playerX.Value;
                        var dy = posY.Value - playerY.Value;
                        var dz = posZ.Value - playerZ.Value;
                        distance = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    }

                    monsters.Add(new {
                        id = emIdInt,
                        name = name ?? $"Monster {emIdInt}",
                        species,
                        health,
                        maxHealth,
                        position = new { x = posX, y = posY, z = posZ },
                        distance
                    });
                } catch { }
            }

            return new { count = monsters.Count, monsters };
        } catch (Exception e) {
            return new { error = e.Message };
        }
    }
    // ── Hunt Log ──────────────────────────────────────────────────────

    // Cache FixedId → monster name, built once from EnemyDef.ID enum
    static Dictionary<int, string> s_monsterNames;

    static Dictionary<int, string> GetMonsterNameMap() {
        if (s_monsterNames != null) return s_monsterNames;
        var map = new Dictionary<int, string>();
        try {
            for (int id = 0; id < 120; id++) {
                try {
                    var eid = (app.EnemyDef.ID)id;
                    if (!app.EnemyDef.isBossID(eid)) continue;
                    var fixedId = (int)app.EnemyDef.enemyId(eid);
                    var name = app.EnemyDef.NameString(eid, 0, 0);
                    if (!string.IsNullOrEmpty(name) && fixedId != 0)
                        map[fixedId] = name;
                } catch { }
            }
        } catch { }
        if (map.Count > 0) s_monsterNames = map;
        return map;
    }

    static object GetHuntLog() {
        try {
            var sdm = API.GetManagedSingletonT<app.SaveDataManager>();
            if (sdm == null) return new { error = "SaveDataManager not available" };

            var saves = sdm.UserSaveData;
            if (saves == null) return new { error = "No save data" };

            app.savedata.cUserSaveParam activeSave = null;
            for (int i = 0; i < saves.Length; i++) {
                if (saves[i] != null && saves[i].Active == 1) { activeSave = saves[i]; break; }
            }
            if (activeSave == null) return new { error = "No active save" };

            var report = activeSave._EnemyReport;
            if (report == null) return new { error = "EnemyReport not available" };

            var bossArr = report._Boss;
            if (bossArr == null) return new { error = "Boss report array not available" };

            var nameMap = GetMonsterNameMap();
            var monsters = new List<object>();

            for (int i = 0; i < bossArr.Length; i++) {
                var boss = bossArr[i];
                if (boss == null) continue;

                int hunt = 0, slay = 0, capture = 0;
                try { hunt = boss.getHuntingNum(); } catch { }
                try { slay = boss.getSlayingNum(); } catch { }
                try { capture = boss.getCaptureNum(); } catch { }

                if (hunt == 0 && slay == 0 && capture == 0) continue;

                int fixedId = boss.FixedId;
                string name = null;
                nameMap.TryGetValue(fixedId, out name);

                monsters.Add(new {
                    fixedId,
                    name = name ?? $"Monster {fixedId}",
                    huntCount = hunt,
                    slayCount = slay,
                    captureCount = capture,
                    totalCount = hunt + capture
                });
            }

            return new { count = monsters.Count, monsters };
        } catch (Exception e) {
            return new { error = e.Message };
        }
    }

    // ── Palico Stats ────────────────────────────────────────────────────

    static object GetPalicoStats() {
        try {
            var om = API.GetManagedSingletonT<app.OtomoManager>();
            if (om == null) return new { error = "OtomoManager not available" };

            var otomoInfo = om.getMasterOtomoInfo();
            if (otomoInfo == null || !otomoInfo.Valid) return new { error = "No palico info" };

            var ctxHolder = otomoInfo.ContextHolder;
            if (ctxHolder == null) return new { error = "No context holder" };

            var otomoCtx = ctxHolder.Otomo;
            if (otomoCtx == null) return new { error = "No otomo context" };

            int? level = null;
            float? hp = null, maxHp = null;

            var statusMgr = otomoCtx.StatusManager;
            if (statusMgr != null) {
                try { level = statusMgr._Level; } catch { }

                try {
                    var healthMgr = statusMgr._HealthManager;
                    if (healthMgr != null) {
                        try { hp = healthMgr.Health; } catch { }
                        try { maxHp = healthMgr.MaxHealth; } catch { }
                    }
                } catch { }
            }

            uint? attackMelee = null, attackRange = null, attributeValue = null;
            int? defense = null, critical = null;
            string attribute = null;

            var paramMgr = statusMgr?.OtomoStatusParamManager;
            if (paramMgr != null) {
                try { attackMelee = paramMgr._Attack_Melee; } catch { }
                try { attackRange = paramMgr._Attack_Range; } catch { }
                try { defense = paramMgr._Defence; } catch { }
                try { critical = paramMgr._Critical; } catch { }
                try { attribute = paramMgr._Attribute.ToString(); } catch { }
                try { attributeValue = paramMgr._AttributeValue; } catch { }
            }

            return new {
                level,
                health = hp,
                maxHealth = maxHp,
                attack = attackMelee,
                rangedAttack = attackRange,
                defense,
                critical,
                element = attribute,
                elementValue = attributeValue
            };
        } catch (Exception e) {
            return new { error = e.Message };
        }
    }
#endif

    static object GetCameraInfo() {
        try {
            var camera = via.SceneManager.MainView.PrimaryCamera;
            if (camera == null) return new { error = "No primary camera" };

            var tf = camera.GameObject.Transform;
            var pos = tf.Position;

            return new {
                position = new { x = pos.x, y = pos.y, z = pos.z },
                fov = camera.FOV,
                nearClip = camera.NearClipPlane,
                farClip = camera.FarClipPlane
            };
        } catch (Exception e) {
            return new { error = e.Message };
        }
    }

    static object GetTDBStats() {
        var tdb = TDB.Get();
        return new {
            types = tdb.GetNumTypes(),
            methods = tdb.GetNumMethods(),
            fields = tdb.GetNumFields(),
            properties = tdb.GetNumProperties(),
            stringsKB = tdb.GetStringsSize() / 1024,
            rawDataKB = tdb.GetRawDataSize() / 1024
        };
    }

    static object GetSingletonList() {
        var singletons = API.GetManagedSingletons();
        singletons.RemoveAll(s => s.Instance == null);

        var list = new List<object>();
        foreach (var desc in singletons) {
            var instance = desc.Instance;
            var tdef = instance.GetTypeDefinition();
            list.Add(new {
                type = tdef.GetFullName(),
                address = "0x" + instance.GetAddress().ToString("X"),
                methods = (int)tdef.GetNumMethods(),
                fields = (int)tdef.GetNumFields()
            });
        }

        return new { count = list.Count, singletons = list };
    }

    // ── Memory reading ─────────────────────────────────────────────

    static ulong ParseHexAddress(string s) {
        if (string.IsNullOrEmpty(s)) return 0;
        if (s.StartsWith("0x") || s.StartsWith("0X"))
            return Convert.ToUInt64(s.Substring(2), 16);
        return Convert.ToUInt64(s, 16);
    }

    static object ReadMemory(HttpListenerRequest request) {
        var qs = request.QueryString;
        var addressStr = qs["address"];
        if (string.IsNullOrEmpty(addressStr))
            return new { error = "address parameter required (hex, e.g. 0x1234ABCD)" };

        ulong addr;
        try { addr = ParseHexAddress(addressStr); }
        catch { return new { error = "Invalid address format" }; }
        if (addr == 0) return new { error = "Null address" };

        int size = 256;
        if (!string.IsNullOrEmpty(qs["size"])) {
            try { size = Math.Clamp(int.Parse(qs["size"]), 1, 8192); }
            catch { return new { error = "Invalid size" }; }
        }

        byte[] bytes;
        try {
            bytes = new byte[size];
            Marshal.Copy(new IntPtr((long)addr), bytes, 0, size);
        } catch (Exception e) {
            return new { error = $"Failed to read {size} bytes at 0x{addr:X}: {e.Message}" };
        }

        // Build hex dump lines: ADDR  XX XX .. XX  XX XX .. XX  |ASCII...........|
        var lines = new List<string>();
        for (int i = 0; i < size; i += 16) {
            var count = Math.Min(16, size - i);
            var hex = new StringBuilder();
            var ascii = new StringBuilder();
            for (int j = 0; j < 16; j++) {
                if (j < count) {
                    hex.AppendFormat("{0:X2} ", bytes[i + j]);
                    var b = bytes[i + j];
                    ascii.Append(b >= 0x20 && b < 0x7F ? (char)b : '.');
                } else {
                    hex.Append("   ");
                }
                if (j == 7) hex.Append(' ');
            }
            lines.Add($"0x{(addr + (ulong)i):X8}  {hex} |{ascii}|");
        }

        return new {
            address = "0x" + addr.ToString("X"),
            size,
            dump = lines
        };
    }

    static object ReadMemoryTyped(HttpListenerRequest request) {
        var qs = request.QueryString;
        var addressStr = qs["address"];
        if (string.IsNullOrEmpty(addressStr))
            return new { error = "address parameter required" };

        ulong addr;
        try { addr = ParseHexAddress(addressStr); }
        catch { return new { error = "Invalid address format" }; }
        if (addr == 0) return new { error = "Null address" };

        var type = qs["type"] ?? "u64";

        int count = 1;
        if (!string.IsNullOrEmpty(qs["count"])) {
            try { count = Math.Clamp(int.Parse(qs["count"]), 1, 256); }
            catch { return new { error = "Invalid count" }; }
        }

        int stride = type switch {
            "u8" or "i8" => 1,
            "u16" or "i16" => 2,
            "u32" or "i32" or "f32" => 4,
            "u64" or "i64" or "f64" or "ptr" or "pointer" => 8,
            _ => 0
        };
        if (stride == 0)
            return new { error = $"Unknown type '{type}'. Supported: u8, i8, u16, i16, u32, i32, u64, i64, f32, f64, ptr" };

        if (count == 1) {
            var single = ReadOneTyped(addr, type);
            if (single is string err)
                return new { error = err };
            var (val, hex) = ((object, string))single;
            return new { address = "0x" + addr.ToString("X"), type, value = val, hex };
        }

        // Multiple sequential reads
        var values = new List<object>();
        for (int i = 0; i < count; i++) {
            ulong a = addr + (ulong)(i * stride);
            var result = ReadOneTyped(a, type);
            if (result is string errMsg) {
                values.Add(new { offset = i * stride, address = "0x" + a.ToString("X"), error = errMsg });
            } else {
                var (v, h) = ((object, string))result;
                values.Add(new { offset = i * stride, address = "0x" + a.ToString("X"), value = v, hex = h });
            }
        }
        return new { baseAddress = "0x" + addr.ToString("X"), type, count, stride, values };
    }

    /// <summary>Returns (value, hex) tuple on success, or error string on failure.</summary>
    static object ReadOneTyped(ulong addr, string type) {
        var ptr = new IntPtr((long)addr);
        try {
            switch (type) {
                case "u8": { var v = Marshal.ReadByte(ptr); return ((object)v, v.ToString("X2")); }
                case "i8": { var v = (sbyte)Marshal.ReadByte(ptr); return ((object)v, ((byte)v).ToString("X2")); }
                case "u16": { var v = (ushort)Marshal.ReadInt16(ptr); return ((object)v, v.ToString("X4")); }
                case "i16": { var v = Marshal.ReadInt16(ptr); return ((object)v, ((ushort)v).ToString("X4")); }
                case "u32": { var v = (uint)Marshal.ReadInt32(ptr); return ((object)v, v.ToString("X8")); }
                case "i32": { var v = Marshal.ReadInt32(ptr); return ((object)v, ((uint)v).ToString("X8")); }
                case "u64": { var v = (ulong)Marshal.ReadInt64(ptr); return ((object)v, v.ToString("X16")); }
                case "i64": { var v = Marshal.ReadInt64(ptr); return ((object)v, ((ulong)v).ToString("X16")); }
                case "f32": {
                    var b = new byte[4];
                    Marshal.Copy(ptr, b, 0, 4);
                    var v = BitConverter.ToSingle(b, 0);
                    return ((object)v, BitConverter.ToUInt32(b, 0).ToString("X8"));
                }
                case "f64": {
                    var b = new byte[8];
                    Marshal.Copy(ptr, b, 0, 8);
                    var v = BitConverter.ToDouble(b, 0);
                    return ((object)v, BitConverter.ToUInt64(b, 0).ToString("X16"));
                }
                case "ptr": case "pointer": {
                    var v = (ulong)Marshal.ReadInt64(ptr);
                    return ((object)("0x" + v.ToString("X")), v.ToString("X16"));
                }
                default:
                    return $"Unknown type '{type}'";
            }
        } catch (Exception e) {
            return $"Failed to read {type} at 0x{addr:X}: {e.Message}";
        }
    }

    // ── Explorer helpers ──────────────────────────────────────────────

    static readonly TypeDefinition s_systemArrayT = TDB.Get().GetType("System.Array");

    static IObject ResolveObject(HttpListenerRequest request) {
        var qs = request.QueryString;
        var addressStr = qs["address"];
        var kind = qs["kind"];
        var typeName = qs["typeName"];

        if (string.IsNullOrEmpty(addressStr) || string.IsNullOrEmpty(kind) || string.IsNullOrEmpty(typeName))
            return null;

        ulong address = 0;
        if (addressStr.StartsWith("0x") || addressStr.StartsWith("0X"))
            address = Convert.ToUInt64(addressStr.Substring(2), 16);
        else
            address = Convert.ToUInt64(addressStr, 16);

        if (address == 0) return null;

        if (kind == "managed") {
            return ManagedObject.ToManagedObject(address);
        } else if (kind == "native") {
            var tdef = TDB.Get().GetType(typeName);
            if (tdef == null) return null;
            return new NativeObject(address, tdef);
        }

        return null;
    }

    static string ReadFieldValueAsString(IObject obj, Field field, TypeDefinition ft, bool isValueType = false) {
        var finalName = ft.IsEnum() ? ft.GetUnderlyingType().GetFullName() : ft.GetFullName();

        object fieldData = null;
        switch (finalName) {
            case "System.Byte":
                fieldData = field.GetDataT<byte>(obj.GetAddress(), isValueType);
                break;
            case "System.SByte":
                fieldData = field.GetDataT<sbyte>(obj.GetAddress(), isValueType);
                break;
            case "System.Int16":
                fieldData = field.GetDataT<short>(obj.GetAddress(), isValueType);
                break;
            case "System.UInt16":
                fieldData = field.GetDataT<ushort>(obj.GetAddress(), isValueType);
                break;
            case "System.Int32":
                fieldData = field.GetDataT<int>(obj.GetAddress(), isValueType);
                break;
            case "System.UInt32":
                fieldData = field.GetDataT<uint>(obj.GetAddress(), isValueType);
                break;
            case "System.Int64":
                fieldData = field.GetDataT<long>(obj.GetAddress(), isValueType);
                break;
            case "System.UInt64":
                fieldData = field.GetDataT<ulong>(obj.GetAddress(), isValueType);
                break;
            case "System.Single":
                fieldData = field.GetDataT<float>(obj.GetAddress(), isValueType);
                break;
            case "System.Boolean":
                fieldData = field.GetDataT<bool>(obj.GetAddress(), isValueType);
                break;
            case "System.String": {
                try {
                    var strObj = field.GetDataBoxed(obj.GetAddress(), isValueType);
                    return strObj?.ToString();
                } catch {
                    return null;
                }
            }
            case "System.Guid": {
                // Read GUID components from field memory
                var addr = obj.GetAddress() + (isValueType ? field.GetOffsetFromFieldPtr() : field.GetOffsetFromBase());
                var guidObj = new NativeObject(addr, ft);
                try {
                    object guidStr = null;
                    guidObj.HandleInvokeMember_Internal(
                        ft.GetMethod("ToString()"), null, ref guidStr);
                    return guidStr?.ToString();
                } catch {
                    return null;
                }
            }
            default:
                return null;
        }

        if (fieldData == null) return null;

        if (ft.IsEnum()) {
            long longValue = Convert.ToInt64(fieldData);
            try {
                var boxedEnum = BoxEnum(ft, longValue);
                return (boxedEnum as IObject).Call("ToString()") + " (" + fieldData.ToString() + ")";
            } catch {
                return fieldData.ToString();
            }
        }

        return fieldData.ToString();
    }

    // Read a ValueType's contents inline since its address is ephemeral (GC-managed).
    // Returns a serializable object with the value type's fields, or a string for known types.
    static object ReadValueTypeInline(IObject vtObj) {
        var tdef = vtObj.GetTypeDefinition();
        var fullName = tdef.GetFullName();

        // For known types, just call ToString() to get a clean representation
        switch (fullName) {
            case "System.Guid": {
                try {
                    object str = null;
                    vtObj.HandleInvokeMember_Internal(tdef.GetMethod("ToString()"), null, ref str);
                    return new { isValueType = true, typeName = fullName, value = str?.ToString() };
                } catch {
                    return new { isValueType = true, typeName = fullName, value = (string)null };
                }
            }
        }

        // Generic value type: read all instance fields using fieldptr offsets (no 0x10 header)
        var fields = new Dictionary<string, object>();
        for (var t = tdef; t != null; t = t.ParentType) {
            foreach (var field in t.GetFields()) {
                if (field.IsStatic()) continue;
                var fname = field.GetName();
                if (fields.ContainsKey(fname)) continue;
                var ft = field.GetType();
                if (ft == null) continue;
                try {
                    if (ft.IsValueType()) {
                        fields[fname] = ReadFieldValueAsString(vtObj, field, ft, true);
                    } else {
                        var child = field.GetDataBoxed(vtObj.GetAddress(), true);
                        if (child is IObject childObj) {
                            var childTdef = childObj.GetTypeDefinition();
                            fields[fname] = new {
                                address = "0x" + childObj.GetAddress().ToString("X"),
                                kind = childObj is ManagedObject ? "managed" : "native",
                                typeName = childTdef.GetFullName()
                            };
                        } else {
                            fields[fname] = child?.ToString();
                        }
                    }
                } catch { fields[fname] = null; }
            }
        }
        return new { isValueType = true, typeName = fullName, fields };
    }

    static IObject ResolveObjectFromParams(string addressStr, string kind, string typeName) {
        if (string.IsNullOrEmpty(addressStr) || string.IsNullOrEmpty(kind) || string.IsNullOrEmpty(typeName))
            return null;

        ulong address = 0;
        if (addressStr.StartsWith("0x") || addressStr.StartsWith("0X"))
            address = Convert.ToUInt64(addressStr.Substring(2), 16);
        else
            address = Convert.ToUInt64(addressStr, 16);

        if (address == 0) return null;

        if (kind == "managed") {
            return ManagedObject.ToManagedObject(address);
        } else if (kind == "native") {
            var tdef = TDB.Get().GetType(typeName);
            if (tdef == null) return null;
            return new NativeObject(address, tdef);
        }

        return null;
    }

    static object ParseValueFromJson(JsonElement value, string typeName) {
        switch (typeName) {
            case "System.Byte":
            case "byte":
                return value.GetByte();
            case "System.SByte":
            case "sbyte":
                return value.GetSByte();
            case "System.Int16":
            case "short":
                return value.GetInt16();
            case "System.UInt16":
            case "ushort":
                return value.GetUInt16();
            case "System.Int32":
            case "int":
                return value.GetInt32();
            case "System.UInt32":
            case "uint":
                return value.GetUInt32();
            case "System.Int64":
            case "long":
                return value.GetInt64();
            case "System.UInt64":
            case "ulong":
                return value.GetUInt64();
            case "System.Single":
            case "float":
                return value.GetSingle();
            case "System.Double":
            case "double":
                return value.GetDouble();
            case "System.Boolean":
            case "bool":
                return value.GetBoolean();
            case "System.String":
            case "string":
                return value.GetString();
            case "System.Guid":
                // Accept GUID as string "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
                if (value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var parsedGuid)) {
                    var guidTdef = TDB.Get().GetType("System.Guid");
                    if (guidTdef == null) return null;
                    var guidVt = guidTdef.CreateValueType();
                    // RE Engine GUID fields: mData1 (uint), mData2 (ushort), mData3 (ushort), mData4_0..mData4_7 (bytes)
                    var bytes = parsedGuid.ToByteArray();
                    var guidFields = new Dictionary<string, Field>();
                    for (var p = guidTdef; p != null; p = p.ParentType)
                        foreach (var f in p.GetFields())
                            if (!f.IsStatic()) guidFields.TryAdd(f.GetName(), f);
                    if (guidFields.TryGetValue("mData1", out var fd1)) fd1.SetDataBoxed(guidVt.GetAddress(), BitConverter.ToUInt32(bytes, 0), true);
                    if (guidFields.TryGetValue("mData2", out var fd2)) fd2.SetDataBoxed(guidVt.GetAddress(), BitConverter.ToUInt16(bytes, 4), true);
                    if (guidFields.TryGetValue("mData3", out var fd3)) fd3.SetDataBoxed(guidVt.GetAddress(), BitConverter.ToUInt16(bytes, 6), true);
                    for (int gi = 0; gi < 8; gi++) {
                        if (guidFields.TryGetValue($"mData4_{gi}", out var fdi))
                            fdi.SetDataBoxed(guidVt.GetAddress(), bytes[8 + gi], true);
                    }
                    return guidVt;
                }
                return null;
            default:
                return ParseComplexValueFromJson(value, typeName);
        }
    }

    static object ParseComplexValueFromJson(JsonElement value, string typeName) {
        var tdef = TDB.Get().GetType(typeName);
        if (tdef == null) return null;

        // Enum: accept integer or string representation
        if (tdef.IsEnum()) {
            long longValue;
            if (value.ValueKind == JsonValueKind.Number) {
                longValue = value.GetInt64();
            } else if (value.ValueKind == JsonValueKind.String) {
                if (!long.TryParse(value.GetString(), out longValue)) return null;
            } else {
                return null;
            }
            return BoxEnum(tdef, longValue) as ManagedObject;
        }

        // Value type (struct): create instance and populate fields from JSON object
        if (tdef.IsValueType() && value.ValueKind == JsonValueKind.Object) {
            var vt = tdef.CreateValueType();

            // Collect non-static fields from type hierarchy
            var fieldMap = new Dictionary<string, Field>();
            for (var parent = tdef; parent != null; parent = parent.ParentType) {
                foreach (var f in parent.GetFields()) {
                    if (!f.IsStatic()) fieldMap.TryAdd(f.GetName(), f);
                }
            }

            // Set each field from the JSON properties
            foreach (var prop in value.EnumerateObject()) {
                if (!fieldMap.TryGetValue(prop.Name, out var field)) continue;
                var ft = field.GetType();
                if (ft == null) continue;
                var fieldValue = ParseValueFromJson(prop.Value, ft.GetFullName());
                if (fieldValue != null) {
                    field.SetDataBoxed(vt.GetAddress(), fieldValue, true);
                }
            }

            return vt;
        }

        // Reference type: resolve hex address string to ManagedObject
        if (!tdef.IsValueType() && value.ValueKind == JsonValueKind.String) {
            var addrStr = value.GetString();
            if (addrStr != null && addrStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) {
                try {
                    var addr = Convert.ToUInt64(addrStr.Substring(2), 16);
                    if (addr != 0) {
                        return ManagedObject.ToManagedObject(addr);
                    }
                } catch { }
            }
        }

        return null;
    }

    static Method FindMethod(TypeDefinition tdef, string methodName, string methodSignature) {
        for (var parent = tdef; parent != null; parent = parent.ParentType) {
            foreach (var m in parent.GetMethods()) {
                if (m.GetName() == methodName) {
                    if (!string.IsNullOrEmpty(methodSignature) && m.GetMethodSignature() != methodSignature)
                        continue;
                    return m;
                }
            }
        }
        return null;
    }

    static object FormatMethodResult(object result, Method method) {
        if (result == null) return new { isObject = false, value = "null" };

        if (result is IObject objResult) {
            var childTdef = objResult.GetTypeDefinition();

            // ValueType results are ephemeral (GC-managed) — read inline before they go out of scope
            if (objResult is REFrameworkNET.ValueType) {
                return ReadValueTypeInline(objResult);
            }

            bool childManaged = objResult is ManagedObject;
            return new {
                isObject = true,
                childAddress = "0x" + objResult.GetAddress().ToString("X"),
                childKind = childManaged ? "managed" : "native",
                childTypeName = childTdef.GetFullName()
            };
        }

        var returnType = method.GetReturnType();
        if (returnType != null && returnType.IsEnum()) {
            long longValue = Convert.ToInt64(result);
            try {
                var boxedEnum = BoxEnum(returnType, longValue);
                return new { isObject = false, value = (boxedEnum as IObject).Call("ToString()") + " (" + result.ToString() + ")" };
            } catch { }
        }

        return new { isObject = false, value = result.ToString() };
    }

#if MHWILDS
    // ── Lobby endpoint ─────────────────────────────────────────────────

    static object GetLobbyMembers() {
        try {
            var nm = API.GetManagedSingletonT<app.NetworkManager>();
            if (nm == null) return new { error = "NetworkManager not available" };

            var userInfoMgr = nm._UserInfoManager;
            if (userInfoMgr == null) return new { error = "UserInfoManager not available" };

            var lobbyInfo = (IObject)userInfoMgr._mlInfo;
            if (lobbyInfo == null) return new { error = "Lobby info not available" };
            var listInfoObj = lobbyInfo.GetField("_ListInfo") as IObject;
            if (listInfoObj == null) return new { error = "ListInfo array is null" };

            var members = new List<object>();
            var arr = listInfoObj.As<_System.Array>();
            int len = arr.Length;

            for (int i = 0; i < len; i++) {
                try {
                    var element = arr.GetValue(i);
                    if (element == null) continue;

                    var userInfo = (element as IObject)?.As<app.Net_UserInfo>();
                    if (userInfo == null || !userInfo.IsValid) continue;

                    string name = null;
                    try { name = userInfo.PlName; } catch { }
                    if (string.IsNullOrEmpty(name)) continue;

                    string otomoName = null;
                    try { otomoName = userInfo.OtomoName; } catch { }

                    int hunterRank = 0;
                    try { hunterRank = userInfo.HunterRank; } catch { }

                    int weaponType = -1;
                    try { weaponType = userInfo.WeaponType; } catch { }

                    int weaponId = 0;
                    try { weaponId = userInfo.WeaponId; } catch { }

                    bool isSelf = false;
                    try { isSelf = userInfo.IsSelf; } catch { }

                    bool isQuest = false;
                    try { isQuest = userInfo.IsQuest; } catch { }

                    members.Add(new {
                        name,
                        otomoName,
                        hunterRank,
                        weaponType,
                        weaponId,
                        isSelf,
                        isQuest,
                        memberIndex = userInfo.MemberIndex
                    });
                } catch { }
            }

            return new { count = members.Count, members };
        } catch (Exception e) {
            return new { error = e.Message };
        }
    }

    // ── Weather endpoint ──────────────────────────────────────────────────

    static object GetWeather() {
        try {
            var wm = API.GetManagedSingletonT<ace.WeatherManager>();
            if (wm == null) return new { error = "WeatherManager not available" };

            var currentName = wm.CurrentWeatherName;

            string nextName = null;
            try { nextName = wm.NextWeatherName; } catch { }

            float blendRate = 0;
            try { blendRate = wm._CurrentBlendRate; } catch { }

            float arrivalTime = 0;
            try { arrivalTime = wm._ArrivalTime; } catch { }

            float time = 0;
            try { time = wm._Time; } catch { }

            // Get per-weather blend rates from _values array
            var blends = new List<object>();
            try {
                var valuesObj = ((IObject)wm).GetField("_values") as IObject;
                if (valuesObj != null) {
                    var arr = valuesObj.As<_System.Array>();
                    int len = arr.Length;
                    for (int i = 0; i < len; i++) {
                        try {
                            var el = arr.GetValue(i) as IObject;
                            if (el == null) continue;
                            var tdef = el.GetTypeDefinition();

                            string name = null;
                            float rate = 0;

                            var nameField = tdef.FindField("_WeatherName");
                            if (nameField != null) {
                                var nameObj = el.GetField("_WeatherName");
                                name = nameObj?.ToString();
                            }

                            var rateField = tdef.FindField("_BlendRate");
                            if (rateField != null) {
                                try { rate = (float)rateField.GetDataBoxed(el.GetAddress(), false); } catch { }
                            }

                            blends.Add(new { name, blendRate = rate });
                        } catch { }
                    }
                }
            } catch { }

            // In-game time of day
            string timeZone = null;
            int hour = 0;
            int minute = 0;
            try {
                var em = API.GetManagedSingletonT<app.EnvironmentManager>();
                if (em != null) {
                    var mainTime = em._MainTime;
                    if (mainTime != null) {
                        float gameCount = mainTime.Count;
                        float timeVal = em.convertGameCountToTime(gameCount);
                        float oneDay = em.getOneDayTimeSecond();
                        if (oneDay > 0) {
                            float fraction = timeVal / oneDay;
                            float hours = fraction * 24f;
                            hour = (int)hours;
                            minute = (int)((hours - hour) * 60);
                        }
                    }
                    try { timeZone = em.getTimeZone(0).ToString(); } catch { }
                }
            } catch { }

            return new {
                current = currentName,
                next = nextName,
                blendRate,
                arrivalTime,
                time,
                blends,
                clock = $"{hour:D2}:{minute:D2}",
                timeZone
            };
        } catch (Exception e) {
            return new { error = e.Message };
        }
    }
#endif

#if RE9
    // ── RE9 Name Resolution ─────────────────────────────────────────────

    static readonly Dictionary<string, string> s_characterNames = new() {
        { "cp_A000", "Leon" },
        { "cp_A100", "Grace" },
    };

    static readonly Dictionary<string, string> s_weaponNameCache = new();

    static string ResolveCharacterName(string kindId) {
        if (kindId == null) return null;
        return s_characterNames.TryGetValue(kindId, out var name) ? name : kindId;
    }

    static string ResolveWeaponName(string weaponIdStr) {
        if (weaponIdStr == null) return null;
        if (s_weaponNameCache.TryGetValue(weaponIdStr, out var cached)) return cached;

        try {
            // Navigate: EquipmentManager._ItemWeaponIDDic._Dict._entries[]
            // Each entry has .key (ItemID) and .value (ManagedItem with _Value = WeaponID)
            // Find the entry where value matches our weaponIdStr, get the ItemID
            // Then look up ItemDetailData from ItemManager._ItemCatalog, read _NameMessageId, localize

            var equipMgr = API.GetManagedSingletonT<app.EquipmentManager>() as IObject;
            var itemMgr = API.GetManagedSingletonT<app.ItemManager>() as IObject;
            if (equipMgr == null || itemMgr == null) return weaponIdStr;

            // Get ItemID for this WeaponID by iterating _ItemWeaponIDDic
            var iwDicField = equipMgr.GetTypeDefinition().FindField("_ItemWeaponIDDic");
            var iwDic = iwDicField?.GetDataBoxed(equipMgr.GetAddress(), false) as IObject;
            if (iwDic == null) return weaponIdStr;

            var dictField = iwDic.GetTypeDefinition().FindField("_Dict");
            var dict = dictField?.GetDataBoxed(iwDic.GetAddress(), false) as IObject;
            if (dict == null) return weaponIdStr;

            var entriesField = dict.GetTypeDefinition().FindField("_entries");
            var entries = entriesField?.GetDataBoxed(dict.GetAddress(), false) as IObject;
            if (entries == null) return weaponIdStr;

            var entryTd = entries.GetTypeDefinition();
            int entryCount = entries.GetAddress() != 0 ? (int)entries.Call("get_Length") : 0;

            IObject matchedItemId = null;
            for (int i = 0; i < entryCount && matchedItemId == null; i++) {
                try {
                    var entry = entries.Call("Get", i) as IObject;
                    if (entry == null) continue;

                    var valueMgd = entry.GetTypeDefinition().FindField("value")?.GetDataBoxed(entry.GetAddress(), false) as IObject;
                    if (valueMgd == null) continue;

                    var weaponVal = valueMgd.GetTypeDefinition().FindField("_Value")?.GetDataBoxed(valueMgd.GetAddress(), false) as IObject;
                    if (weaponVal == null) continue;

                    if (weaponVal.Call("ToString").ToString() == weaponIdStr) {
                        matchedItemId = entry.GetTypeDefinition().FindField("key")?.GetDataBoxed(entry.GetAddress(), false) as IObject;
                    }
                } catch { }
            }

            if (matchedItemId == null) return weaponIdStr;

            // Now look up ItemDetailData from ItemManager._ItemCatalog
            var catalogField = itemMgr.GetTypeDefinition().FindField("_ItemCatalog");
            var catalog = catalogField?.GetDataBoxed(itemMgr.GetAddress(), false) as IObject;
            if (catalog == null) return weaponIdStr;

            var catDictField = catalog.GetTypeDefinition().FindField("_Dict");
            var catDict = catDictField?.GetDataBoxed(catalog.GetAddress(), false) as IObject;
            if (catDict == null) return weaponIdStr;

            var catEntries = catDict.GetTypeDefinition().FindField("_entries")?.GetDataBoxed(catDict.GetAddress(), false) as IObject;
            if (catEntries == null) return weaponIdStr;

            int catCount = (int)catEntries.Call("get_Length");
            string matchedItemStr = matchedItemId.Call("ToString").ToString();

            for (int i = 0; i < catCount; i++) {
                try {
                    var entry = catEntries.Call("Get", i) as IObject;
                    if (entry == null) continue;

                    var key = entry.GetTypeDefinition().FindField("key")?.GetDataBoxed(entry.GetAddress(), false) as IObject;
                    if (key == null || key.Call("ToString").ToString() != matchedItemStr) continue;

                    var valueMgd = entry.GetTypeDefinition().FindField("value")?.GetDataBoxed(entry.GetAddress(), false) as IObject;
                    if (valueMgd == null) continue;

                    var detail = valueMgd.GetTypeDefinition().FindField("_Value")?.GetDataBoxed(valueMgd.GetAddress(), false) as IObject;
                    if (detail == null) continue;

                    // Read _NameMessageId (System.Guid, value type at offset 0x80)
                    var nameGuidField = detail.GetTypeDefinition().FindField("_NameMessageId");
                    if (nameGuidField == null) continue;

                    var guidObj = nameGuidField.GetDataBoxed(detail.GetAddress(), false) as IObject;
                    if (guidObj == null) continue;

                    // Call via.gui.message.get(guid) to localize
                    var msgTd = TDB.Get().FindType("via.gui.message");
                    var getMethod = msgTd?.FindMethod("get(System.Guid)");
                    if (getMethod == null) continue;

                    var localizedStr = getMethod.Invoke(null, new object[] { guidObj });
                    var result = localizedStr.ToString();
                    if (!string.IsNullOrEmpty(result)) {
                        s_weaponNameCache[weaponIdStr] = result;
                        return result;
                    }
                } catch { }
            }
        } catch { }

        return weaponIdStr;
    }

    // ── RE9 Player Info ──────────────────────────────────────────────────

    static object GetPlayerInfoRE9() {
        try {
            var cm = API.GetManagedSingletonT<app.CharacterManager>();
            if (cm == null) return new { error = "CharacterManager not available" };

            var pc = cm.getPlayerContextRefFast();
            if (pc == null) return new { error = "Player not available" };

            bool isInSafeRoom = false, isGameOver = false;
            try { isInSafeRoom = cm.IsPlayerInSafeRoom; } catch { }
            try { isGameOver = cm.IsGameOvered; } catch { }

            string characterId = null;
            try { characterId = pc.KindID?.ToString(); } catch { }

            float? posX = null, posY = null, posZ = null;
            try {
                var pos = pc.PositionFast;
                posX = pos.x; posY = pos.y; posZ = pos.z;
            } catch { }

            int? health = null, maxHealth = null;
            bool? isDead = null, invincible = null;
            try {
                var hp = pc.HitPoint;
                if (hp != null) {
                    health = hp.CurrentHitPoint;
                    maxHealth = hp.CurrentMaximumHitPoint;
                    isDead = hp.IsDead;
                    invincible = hp.Invincible;
                }
            } catch { }

            bool isSpawn = false;
            try { isSpawn = pc.IsSpawn; } catch { }

            return new {
                characterId,
                characterName = ResolveCharacterName(characterId),
                health,
                maxHealth,
                isDead,
                invincible,
                isInSafeRoom,
                isGameOver,
                isSpawn,
                position = new { x = posX, y = posY, z = posZ }
            };
        } catch (Exception e) {
            return new { error = e.Message };
        }
    }

    static object SetPlayerHealthRE9(HttpListenerRequest request) {
        try {
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            var body = reader.ReadToEnd();
            var doc = JsonDocument.Parse(body);
            var value = doc.RootElement.GetProperty("value").GetInt32();

            var cm = API.GetManagedSingletonT<app.CharacterManager>();
            if (cm == null) return new { error = "CharacterManager not available" };

            var pc = cm.getPlayerContextRefFast();
            if (pc == null) return new { error = "Player not available" };

            var hp = pc.HitPoint;
            if (hp == null) return new { error = "HitPoint not available" };

            hp.CurrentHitPoint = value;
            return new { ok = true, health = value };
        } catch (Exception e) {
            return new { error = e.Message };
        }
    }

    // ── RE9 Enemy List ───────────────────────────────────────────────────

    static object GetEnemiesRE9() {
        try {
            var cm = API.GetManagedSingletonT<app.CharacterManager>();
            if (cm == null) return new { error = "CharacterManager not available" };

            var enemies = new List<object>();
            var enemyList = cm.EnemyContextList;
            if (enemyList != null) {
                int count = enemyList.Count;
                for (int i = 0; i < count; i++) {
                    try {
                        var ec = enemyList[i];
                        if (ec == null) continue;

                        bool isSpawn = false, isSuspended = false;
                        try { isSpawn = ec.IsSpawn; } catch { }
                        try { isSuspended = ec.IsSuspended; } catch { }
                        if (!isSpawn || isSuspended) continue;

                        string kindId = null;
                        try { kindId = ec.KindID?.ToString(); } catch { }

                        int? hp = null, maxHp = null;
                        bool? isDead = null;
                        try {
                            var hitPoint = ec.HitPoint;
                            if (hitPoint != null) {
                                hp = hitPoint.CurrentHitPoint;
                                maxHp = hitPoint.CurrentMaximumHitPoint;
                                isDead = hitPoint.IsDead;
                            }
                        } catch { }

                        bool isElite = false;
                        try { isElite = ec.IsElite; } catch { }

                        float? posX = null, posY = null, posZ = null;
                        try {
                            var tf = ec.Transform;
                            if (tf != null) {
                                var pos = tf.Position;
                                posX = pos.x; posY = pos.y; posZ = pos.z;
                            }
                        } catch { }

                        enemies.Add(new {
                            kindId,
                            health = hp,
                            maxHealth = maxHp,
                            isDead,
                            isElite,
                            position = new { x = posX, y = posY, z = posZ }
                        });
                    } catch { }
                }
            }

            return new {
                count = enemies.Count,
                isPlayerInSafeRoom = cm.IsPlayerInSafeRoom,
                enemies
            };
        } catch (Exception e) {
            return new { error = e.Message };
        }
    }

    // ── RE9 Game Info ────────────────────────────────────────────────────

    static object GetGameInfoRE9() {
        try {
            var result = new Dictionary<string, object>();

            // Chapter / Story progress
            try {
                var lfm = API.GetManagedSingletonT<app.LevelFlowManager>();
                if (lfm != null) {
                    result["chapter"] = lfm.getDyingProgressName();
                    result["progressNo"] = lfm.getDyingProgressNo();
                }
            } catch { }

            // Scenario time
            try {
                var stm = API.GetManagedSingletonT<app.ScenarioTimeManager>();
                if (stm != null) {
                    result["scenarioTime"] = stm.CurrentKind?.ToString();
                }
            } catch { }

            // Difficulty
            try {
                var gdm = API.GetManagedSingletonT<app.GameDifficultyManager>();
                if (gdm != null) {
                    result["difficulty"] = gdm.DifficultyID?.ToString();
                }
            } catch { }

            // Adaptive rank
            try {
                var rm = API.GetManagedSingletonT<app.RankManager>();
                if (rm != null) {
                    result["rank"] = rm.getCurrentRank();
                    result["rankMax"] = 10;
                    result["enemyDamageFactor"] = rm.getEnemyDamageFactor();
                    result["playerDamageFactor"] = rm.getPlayerDamageFactor();
                    result["enemyMoveFactor"] = rm.getEnemyMoveFactor();
                    result["enemyWinceFactor"] = rm.getEnemyWinceFactor();
                }
            } catch { }

            // Play time from GameClock (OperationTimerType enum not in proxy, use reflection)
            try {
                var gc = API.GetManagedSingletonT<app.GameClock>();
                if (gc != null) {
                    var gcObj = gc as IObject;
                    var timerTd = TDB.Get().FindType("app.OperationTimerType");
                    if (timerTd != null) {
                        var enumVal = BoxEnum(timerTd, 2); // GameElapsedTime = 2
                        var gameTime = gcObj.Call("getElapsedTime", enumVal);
                        if (gameTime != null) {
                            var us = Convert.ToInt64(gameTime.ToString());
                            result["playTimeMicroseconds"] = us;
                            result["playTimeSeconds"] = (double)us / 1_000_000.0;
                        }
                    }
                }
            } catch { }

            // Game data (clear count, NG count)
            try {
                var gdm2 = API.GetManagedSingletonT<app.GameDataManager>();
                if (gdm2 != null) {
                    result["newGameStarts"] = gdm2.NewGameCount;
                    result["totalClears"] = gdm2.TotalClearCount;
                }
            } catch { }

            // Collectibles from GimmickManager (use reflection for max methods not in proxy)
            try {
                var gm = API.GetManagedSingletonT<app.GimmickManager>();
                if (gm != null) {
                    var gmObj = gm as IObject;
                    int? safeCount = null, safeMax = null;
                    int? containerCount = null, containerMax = null;
                    int? fragileCount = null, fragileMax = null;
                    try { safeCount = (int)gm.getAchievementSafeObjectCount(); } catch { }
                    try { safeMax = Convert.ToInt32(gmObj.Call("getAchievementSafeObjectMaxCount").ToString()); } catch { }
                    try { containerCount = (int)gm.getAchievementContainerObjectCount(); } catch { }
                    try { containerMax = Convert.ToInt32(gmObj.Call("getAchievementContainerObjectMaxCount").ToString()); } catch { }
                    try { fragileCount = (int)gm.getAchievementFragileSymbolCount(); } catch { }
                    try { fragileMax = Convert.ToInt32(gmObj.Call("getAchievementFragileSymbolMaxCount").ToString()); } catch { }
                    result["collectibles"] = new {
                        safes = new { found = safeCount, max = safeMax },
                        containers = new { found = containerCount, max = containerMax },
                        fragileSymbols = new { found = fragileCount, max = fragileMax }
                    };
                }
            } catch { }

            // Combat state from PlayerContextUnit_Common
            try {
                var cm = API.GetManagedSingletonT<app.CharacterManager>();
                var pc = cm?.getPlayerContextRefFast();
                if (pc != null) {
                    var common = pc.Common;
                    if (common != null) {
                        var weaponIdStr = common.EquipWeaponID?.ToString();
                        result["weapon"] = weaponIdStr;
                        result["weaponName"] = ResolveWeaponName(weaponIdStr);
                        try {
                            var commonObj = common as IObject;
                            var fearField = commonObj?.GetTypeDefinition()?.FindField("_FearLevelRate");
                            if (fearField != null) result["fearLevel"] = fearField.GetDataBoxed(commonObj.GetAddress(), false);
                        } catch { }
                        result["combat"] = new {
                            isHolding = common.IsHolding,
                            isShooting = common.IsShooting,
                            isReloading = common.IsReloading,
                            isMeleeAttack = common.IsMeleeAttack,
                            isCrouch = common.IsCrouch,
                            isRun = common.IsRun,
                            isIdle = common.IsIdle
                        };
                    }
                }
            } catch { }

            // Scene info from MainGameFlowManager
            try {
                var mgfm = API.GetManagedSingletonT<app.MainGameFlowManager>();
                if (mgfm != null) {
                    result["isMainGame"] = mgfm.IsMainGame();
                }
            } catch { }

            return result;
        } catch (Exception e) {
            return new { error = e.Message };
        }
    }
#endif

#if RE2
    // ── RE2 Player Info ──────────────────────────────────────────────────

    static readonly Dictionary<string, string> s_re2PlayerNames = new() {
        { "PL1000", "Leon" },
        { "PL2000", "Claire" },
        { "PL4000", "Hunk" },
        { "PL5000", "Tofu" },
        { "PL5100", "Konjac" },
        { "PL5200", "Uiro-Mochi" },
        { "PL5300", "Flan" },
        { "PL5400", "Annin Tofu" },
    };

    static readonly Dictionary<string, string> s_re2EnemyNames = new() {
        { "em0000", "Zombie" },
        { "em0100", "Zombie (Armored)" },
        { "em3000", "Licker" },
        { "em3100", "Licker \u03B2" },
        { "em4000", "G-Adult" },
        { "em4400", "Ivy" },
        { "em4500", "Ivy (Zombie)" },
        { "em5000", "Cerberus" },
        { "em6000", "G-Birkin (Phase 1)" },
        { "em6100", "G-Birkin (Phase 2)" },
        { "em6200", "Mr. X" },
        { "em6300", "Super Tyrant" },
        { "em6400", "G-Birkin (Phase 3)" },
        { "em6500", "G-Birkin (Phase 4)" },
        { "em6600", "G-Birkin (Phase 5)" },
        { "em7000", "Chief Irons" },
        { "em7100", "Alligator" },
        { "em9000", "G-Young" },
    };

    static readonly Dictionary<string, string> s_re2LocationNames = new() {
        { "PoliceStation", "R.P.D." },
        { "Sewers", "Sewers" },
        { "Laboratory", "NEST" },
        { "Orphanage", "Orphanage" },
        { "WasteWater", "Waste Water" },
    };

    // Helper: call a getter that returns an enum, resolve via BoxEnum
    static string CallEnumMethod(IObject obj, string methodName) {
        var tdef = obj.GetTypeDefinition();
        var method = FindMethod(tdef, methodName, null);
        if (method == null) return null;
        var result = obj.Call(methodName);
        if (result == null) return null;
        var returnType = method.GetReturnType();
        if (returnType != null && returnType.IsEnum()) {
            long longValue = Convert.ToInt64(result);
            try {
                var boxed = BoxEnum(returnType, longValue);
                return (boxed as IObject)?.Call("ToString()") + " (" + result + ")";
            } catch { }
        }
        return result.ToString();
    }

    static object GetPlayerInfoRE2() {
        try {
            var pm = API.GetManagedSingleton("app.ropeway.PlayerManager");
            if (pm == null) return new { error = "PlayerManager not available" };
            var pmObj = pm as IObject;

            // Player type and character name
            string playerTypeStr = null;
            string playerName = null;
            try {
                playerTypeStr = CallEnumMethod(pmObj, "get_CurrentPlayerType");
            } catch { }
            // Character identity from MainFlowManager (CurrentPlayerType is unreliable)
            try {
                var mfm = API.GetManagedSingleton("app.ropeway.gamemastering.MainFlowManager") as IObject;
                if (mfm != null) {
                    bool isLeon = false, isClaire = false;
                    try { isLeon = (bool)mfm.Call("get_IsLeon"); } catch { }
                    try { isClaire = (bool)mfm.Call("get_IsClaire"); } catch { }
                    if (isLeon) playerName = "Leon";
                    else if (isClaire) playerName = "Claire";
                }
            } catch { }
            // Fallback: try to match from playerType enum for Hunk/Tofu modes
            if (playerName == null && playerTypeStr != null) {
                var code = playerTypeStr.Split(' ')[0];
                s_re2PlayerNames.TryGetValue(code, out playerName);
                if (playerName == null) playerName = code;
            }

            // HP
            int? health = null, maxHealth = null;
            float? healthPercent = null;
            bool isDead = false;
            try { health = Convert.ToInt32(pmObj.Call("get_CurrentHP").ToString()); } catch { }
            try { healthPercent = Convert.ToSingle(pmObj.Call("get_CurrentHPPercentage").ToString()); } catch { }
            try { isDead = (bool)pmObj.Call("get_IsDead"); } catch { }
            // Max HP from HitPointController on the PlayerCondition
            try {
                var pc0 = pmObj.Call("get_CurrentPlayerCondition") as IObject;
                if (pc0 != null) {
                    var hpc = pc0.Call("get_HitPointController") as IObject;
                    if (hpc != null) {
                        maxHealth = Convert.ToInt32(hpc.Call("get_DefaultHitPoint").ToString());
                    }
                }
            } catch { }

            // Position
            float? posX = null, posY = null, posZ = null;
            try {
                var posObj = pmObj.Call("get_CurrentPosition") as IObject;
                if (posObj != null) {
                    posX = Convert.ToSingle(posObj.GetField("x").ToString());
                    posY = Convert.ToSingle(posObj.GetField("y").ToString());
                    posZ = Convert.ToSingle(posObj.GetField("z").ToString());
                }
            } catch { }

            // Player condition details
            bool isPoison = false, isCombat = false, isEvent = false;
            bool isHolding = false, isAttacked = false;
            string vital = null, weaponType = null, subWeaponType = null;
            string situation = null, costumeType = null;
            try {
                var pc = pmObj.Call("get_CurrentPlayerCondition") as IObject;
                if (pc != null) {
                    try { isPoison = (bool)pc.Call("get_IsPoison"); } catch { }
                    try { isCombat = (bool)pc.Call("get_IsCombat"); } catch { }
                    try { isEvent = (bool)pc.Call("get_IsEvent"); } catch { }
                    try { isHolding = (bool)pc.Call("get_IsHolding"); } catch { }
                    try { isAttacked = (bool)pc.Call("get_IsAttacked"); } catch { }
                    try { vital = CallEnumMethod(pc, "get_HitPointVital"); } catch { }
                    try { weaponType = CallEnumMethod(pc, "get_EquipWeaponType"); } catch { }
                    try { subWeaponType = CallEnumMethod(pc, "get_SubWeaponType"); } catch { }
                    try { situation = CallEnumMethod(pc, "get_Situation"); } catch { }
                    try { costumeType = CallEnumMethod(pc, "get_CostumeType"); } catch { }
                }
            } catch { }

            // Stats
            int? pedometer = null, damagedCount = null, counterAttackCount = null;
            float? totalDistance = null;
            try { pedometer = Convert.ToInt32(pmObj.Call("get_Pedometer").ToString()); } catch { }
            try { damagedCount = Convert.ToInt32(pmObj.Call("get_DamagedNumber").ToString()); } catch { }
            try { counterAttackCount = Convert.ToInt32(pmObj.Call("get_CounterAttackNumber").ToString()); } catch { }
            try { totalDistance = Convert.ToSingle(pmObj.Call("get_TotalMovingDistance").ToString()); } catch { }

            return new {
                playerType = playerTypeStr,
                playerName,
                health,
                maxHealth,
                healthPercent,
                isDead,
                position = new { x = posX, y = posY, z = posZ },
                status = new {
                    vital,
                    isPoison,
                    isCombat,
                    isEvent,
                    isHolding,
                    isAttacked,
                    situation
                },
                equipment = new {
                    weapon = weaponType,
                    subWeapon = subWeaponType,
                    costume = costumeType
                },
                stats = new {
                    pedometer,
                    damagedCount,
                    counterAttackCount,
                    totalDistance
                }
            };
        } catch (Exception e) {
            return new { error = e.Message };
        }
    }

    static object SetPlayerHealthRE2(HttpListenerRequest request) {
        try {
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            var body = reader.ReadToEnd();
            var doc = JsonDocument.Parse(body);
            var value = doc.RootElement.GetProperty("value").GetInt32();

            var pm = API.GetManagedSingleton("app.ropeway.PlayerManager");
            if (pm == null) return new { error = "PlayerManager not available" };
            var pmObj = pm as IObject;

            var pc = pmObj.Call("get_CurrentPlayerCondition") as IObject;
            if (pc == null) return new { error = "Player not available" };

            pc.Call("set_CurrentHitPoint", (object)value);
            return new { ok = true, health = value };
        } catch (Exception e) {
            return new { error = e.Message };
        }
    }

    static object SetPlayerPositionRE2(HttpListenerRequest request) {
        try {
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            var body = reader.ReadToEnd();
            var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var pm = API.GetManagedSingleton("app.ropeway.PlayerManager");
            if (pm == null) return new { error = "PlayerManager not available" };
            var pmObj = pm as IObject;

            var go = pmObj.Call("get_CurrentPlayer") as IObject;
            if (go == null) return new { error = "Player GameObject not found" };

            var tf = go.Call("get_Transform") as IObject;
            if (tf == null) return new { error = "Transform not found" };

            // Read current position from PlayerManager (reliable)
            var posObj = pmObj.Call("get_CurrentPosition") as IObject;
            float x = posObj != null ? Convert.ToSingle(posObj.GetField("x").ToString()) : 0;
            float y = posObj != null ? Convert.ToSingle(posObj.GetField("y").ToString()) : 0;
            float z = posObj != null ? Convert.ToSingle(posObj.GetField("z").ToString()) : 0;

            if (root.TryGetProperty("x", out var xProp)) x = xProp.GetSingle();
            if (root.TryGetProperty("y", out var yProp)) y = yProp.GetSingle();
            if (root.TryGetProperty("z", out var zProp)) z = zProp.GetSingle();

            // Build a vec3 value type and set position via Transform
            var vec3Td = TDB.Get().FindType("via.vec3");
            var vt = vec3Td.CreateValueType();
            var vtObj = vt as IObject;
            vec3Td.FindField("x").SetDataBoxed(vtObj.GetAddress(), x, true);
            vec3Td.FindField("y").SetDataBoxed(vtObj.GetAddress(), y, true);
            vec3Td.FindField("z").SetDataBoxed(vtObj.GetAddress(), z, true);
            tf.Call("set_Position", vtObj);

            return new { ok = true, position = new { x, y, z } };
        } catch (Exception e) {
            return new { error = e.Message };
        }
    }

    // ── RE2 Enemy List ───────────────────────────────────────────────────

    static object GetEnemiesRE2() {
        try {
            var em = API.GetManagedSingleton("app.ropeway.EnemyManager");
            if (em == null) return new { error = "EnemyManager not available" };
            var emObj = em as IObject;

            bool playerInSafeRoom = false;
            try { playerInSafeRoom = (bool)emObj.Call("get_PlayerInSafeRoom"); } catch { }

            uint totalKills = 0;
            try { totalKills = Convert.ToUInt32(emObj.Call("getEnemyKillCount").ToString()); } catch { }

            // Player position for distance calc
            float? playerX = null, playerY = null, playerZ = null;
            try {
                var pm = API.GetManagedSingleton("app.ropeway.PlayerManager") as IObject;
                var posObj = pm?.Call("get_CurrentPosition") as IObject;
                if (posObj != null) {
                    playerX = Convert.ToSingle(posObj.GetField("x").ToString());
                    playerY = Convert.ToSingle(posObj.GetField("y").ToString());
                    playerZ = Convert.ToSingle(posObj.GetField("z").ToString());
                }
            } catch { }

            var enemies = new List<object>();
            var listObj = emObj.GetField("<ActiveEnemyList>k__BackingField") as IObject;
            if (listObj != null) {
                int count = 0;
                try { count = (int)listObj.Call("get_Count"); } catch { }

                for (int i = 0; i < count; i++) {
                    try {
                        var ec = listObj.Call("get_Item", (object)i) as IObject;
                        if (ec == null) continue;

                        string kindId = null;
                        string enemyName = null;
                        try {
                            kindId = CallEnumMethod(ec, "get_KindID");
                            if (kindId != null) {
                                var code = kindId.Split(' ')[0];
                                s_re2EnemyNames.TryGetValue(code, out enemyName);
                            }
                        } catch { }

                        // HP from EnemyHitPointController
                        int? hp = null, maxHp = null;
                        bool? isEnemyDead = null;
                        try {
                            var hpc = ec.Call("get_HitPoint") as IObject;
                            if (hpc != null) {
                                try { hp = Convert.ToInt32(hpc.Call("get_CurrentHitPoint").ToString()); } catch { }
                                try { maxHp = Convert.ToInt32(hpc.Call("get_DefaultHitPoint").ToString()); } catch { }
                                try { isEnemyDead = (bool)hpc.Call("get_IsDead"); } catch { }
                            }
                        } catch { }

                        // Position from Transform
                        float? posX = null, posY = null, posZ = null;
                        try {
                            var go = ec.Call("get_GameObject") as IObject;
                            var tf = go?.Call("get_Transform") as IObject;
                            if (tf != null) {
                                var pos = tf.Call("get_Position") as IObject;
                                if (pos != null) {
                                    posX = Convert.ToSingle(pos.GetField("x").ToString());
                                    posY = Convert.ToSingle(pos.GetField("y").ToString());
                                    posZ = Convert.ToSingle(pos.GetField("z").ToString());
                                }
                            }
                        } catch { }

                        // Distance from player
                        float? distance = null;
                        if (playerX.HasValue && posX.HasValue) {
                            var dx = posX.Value - playerX.Value;
                            var dy = posY.Value - playerY.Value;
                            var dz = posZ.Value - playerZ.Value;
                            distance = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
                        }

                        enemies.Add(new {
                            kindId,
                            name = enemyName ?? kindId,
                            health = hp,
                            maxHealth = maxHp,
                            isDead = isEnemyDead,
                            position = new { x = posX, y = posY, z = posZ },
                            distance
                        });
                    } catch { }
                }
            }

            return new {
                count = enemies.Count,
                playerInSafeRoom,
                totalKills,
                enemies
            };
        } catch (Exception e) {
            return new { error = e.Message };
        }
    }

    // ── RE2 Game Info ────────────────────────────────────────────────────

    static object GetGameInfoRE2() {
        try {
            var result = new Dictionary<string, object>();

            // Scenario and main state from MainFlowManager
            try {
                var mfm = API.GetManagedSingleton("app.ropeway.gamemastering.MainFlowManager") as IObject;
                if (mfm != null) {
                    try { result["mainState"] = CallEnumMethod(mfm, "get_MainStateValue"); } catch { }
                    try { result["scenarioType"] = CallEnumMethod(mfm, "get_CurrentScenarioType"); } catch { }
                    try { result["difficulty"] = CallEnumMethod(mfm, "get_CurrentDifficulty"); } catch { }
                    try { result["gameStartType"] = CallEnumMethod(mfm, "get_GameStartValue"); } catch { }
                    try { result["isInGame"] = (bool)mfm.Call("get_IsInGame"); } catch { }
                    try { result["isInPause"] = (bool)mfm.Call("get_IsInPause"); } catch { }
                    try { result["isInTitle"] = (bool)mfm.Call("get_IsInTitle"); } catch { }
                    try { result["isFirstBoot"] = (bool)mfm.Call("get_IsFirstBoot"); } catch { }
                    try { result["isLeon"] = (bool)mfm.Call("get_IsLeon"); } catch { }
                    try { result["isClaire"] = (bool)mfm.Call("get_IsClaire"); } catch { }
                    try { result["is2ndStory"] = (bool)mfm.Call("get_Is2ndStory"); } catch { }
                    try { result["isExtraGame"] = (bool)mfm.Call("get_IsExtraGame"); } catch { }
                    try { result["saveTimes"] = Convert.ToInt32(mfm.Call("get_SaveTimes").ToString()); } catch { }
                    try {
                        var loc = CallEnumMethod(mfm, "get_LatestSaveDataLocation");
                        result["location"] = loc;
                        if (loc != null) {
                            var code = loc.Split(' ')[0];
                            s_re2LocationNames.TryGetValue(code, out var locName);
                            result["locationName"] = locName ?? code;
                        }
                    } catch { }
                }
            } catch { }

            // Adaptive difficulty (Game Rank System)
            try {
                var grs = API.GetManagedSingleton("app.ropeway.GameRankSystem") as IObject;
                if (grs != null) {
                    int? rank = null;
                    float? rankPoint = null;
                    float? enemyDamageRate = null, playerDamageRate = null, enemyBreakRate = null;
                    try { rank = Convert.ToInt32(grs.Call("get_GameRank").ToString()); } catch { }
                    try { rankPoint = Convert.ToSingle(grs.Call("get_RankPoint").ToString()); } catch { }
                    try { enemyDamageRate = Convert.ToSingle(grs.Call("getRankEnemyDamageRate").ToString()); } catch { }
                    try { playerDamageRate = Convert.ToSingle(grs.Call("getRankPlayerDamageRate").ToString()); } catch { }
                    try { enemyBreakRate = Convert.ToSingle(grs.Call("getRankEnemyBreakRate").ToString()); } catch { }
                    result["gameRank"] = new {
                        rank,
                        rankPoint,
                        enemyDamageRate,
                        playerDamageRate,
                        enemyBreakRate
                    };
                }
            } catch { }

            // Enemy manager state
            try {
                var emObj = API.GetManagedSingleton("app.ropeway.EnemyManager") as IObject;
                if (emObj != null) {
                    try { result["currentMap"] = CallEnumMethod(emObj, "get_LastPlayerStaySceneID"); } catch { }
                    try { result["currentArea"] = CallEnumMethod(emObj, "get_LastPlayerStayLocationID"); } catch { }
                    try { result["playerInSafeRoom"] = (bool)emObj.Call("get_PlayerInSafeRoom"); } catch { }
                    try { result["tyrantMap"] = CallEnumMethod(emObj, "getTyrantStayMapID"); } catch { }
                    try { result["totalEnemyKills"] = Convert.ToUInt32(emObj.Call("getEnemyKillCount").ToString()); } catch { }
                }
            } catch { }

            return result;
        } catch (Exception e) {
            return new { error = e.Message };
        }
    }

    // ── RE2 Inventory ────────────────────────────────────────────────────

    static object GetInventoryRE2() {
        try {
            var mgr = API.GetManagedSingleton("app.ropeway.gamemastering.InventoryManager") as IObject;
            if (mgr == null) return new { error = "InventoryManager not available" };

            int slotCount = 0, maxSlots = 0;
            try { slotCount = Convert.ToInt32(mgr.Call("get_InventoryItemSlotCurrentSize").ToString()); } catch { }
            try { maxSlots = Convert.ToInt32(mgr.Call("get_InventoryItemSlotMaxSize").ToString()); } catch { }

            // Equipped weapon info
            string equippedWeapon = null;
            int? mainAmmoLoaded = null, mainAmmoCarried = null;
            try { equippedWeapon = CallEnumMethod(mgr, "equipedWeapon"); } catch { }
            var wpCode = equippedWeapon?.Split(' ')[0];
            string matchedWeaponName = null;
            try { mainAmmoLoaded = Convert.ToInt32(mgr.Call("getMainWeaponRemainingBullet").ToString()); } catch { }
            try { mainAmmoCarried = Convert.ToInt32(mgr.Call("getMainWeaponReloadableBullet").ToString()); } catch { }

            // Iterate slots
            var items = new List<object>();
            try {
                var slots = mgr.Call("getInventorySlots") as IObject;
                if (slots != null) {
                    int arrLen = slotCount;
                    // Get array length via System.Array
                    try {
                        var lenObj = slots.Call("get_Length");
                        if (lenObj != null) arrLen = Convert.ToInt32(lenObj.ToString());
                    } catch { }

                    for (int i = 0; i < arrLen && i < slotCount; i++) {
                        try {
                            var slot = slots.Call("Get", (object)i) as IObject;
                            if (slot == null) continue;

                            bool isEmpty = false;
                            try { isEmpty = (bool)slot.Call("get_IsEmpty"); } catch { }
                            if (isEmpty) continue;

                            bool isBlank = false;
                            try { isBlank = (bool)slot.Call("get_IsBlank"); } catch { }
                            if (isBlank) continue;

                            string itemName = null, weaponName = null;
                            int number = 0, maxNumber = 0;
                            bool isWeapon = false, isItem = false, isCustomized = false;
                            float remainRatio = 1f;
                            string weaponParts = null;

                            try { itemName = slot.Call("get_ItemName")?.ToString(); } catch { }
                            try { weaponName = slot.Call("get_WeaponName")?.ToString(); } catch { }
                            try { number = Convert.ToInt32(slot.Call("get_Number").ToString()); } catch { }
                            try { maxNumber = Convert.ToInt32(slot.Call("get_MaxNumber").ToString()); } catch { }
                            try { isWeapon = (bool)slot.Call("get_IsWeapon"); } catch { }
                            try { isItem = (bool)slot.Call("get_IsItem"); } catch { }
                            try { isCustomized = (bool)slot.Call("get_IsCustomizedWeapon"); } catch { }
                            try { remainRatio = Convert.ToSingle(slot.Call("get_RemainingRatio").ToString()); } catch { }
                            try { weaponParts = CallEnumMethod(slot, "get_WeaponParts"); } catch { }

                            // Match equipped weapon by WP code
                            if (isWeapon && wpCode != null && matchedWeaponName == null) {
                                try {
                                    var slotWpEnum = CallEnumMethod(slot, "get_WeaponType");
                                    var slotWpCode = slotWpEnum?.Split(' ')[0];
                                    if (slotWpCode == wpCode) matchedWeaponName = weaponName;
                                } catch { }
                            }

                            string name = isWeapon ? (weaponName ?? itemName ?? "?") : (itemName ?? "?");

                            items.Add(new {
                                slotIndex = i,
                                name,
                                quantity = number,
                                maxQuantity = maxNumber,
                                isWeapon,
                                isItem,
                                isCustomized,
                                remainRatio,
                                weaponParts = isWeapon ? weaponParts : null
                            });
                        } catch { }
                    }
                }
            } catch { }

            return new {
                count = items.Count,
                capacity = maxSlots,
                equippedWeapon = matchedWeaponName ?? equippedWeapon,
                equippedWeaponType = wpCode,
                mainAmmoLoaded,
                mainAmmoCarried,
                items
            };
        } catch (Exception e) {
            return new { error = e.Message };
        }
    }

    // ── RE2 Mr. X (Tyrant) Tracker ─────────────────────────────────────

    static object GetStalkerRE2() {
        try {
            var em = API.GetManagedSingleton("app.ropeway.EnemyManager") as IObject;
            if (em == null) return new { error = "EnemyManager not available" };

            // Get the chaser controller list
            var chaserList = em.Call("get_Em6200ChaserController") as IObject;
            if (chaserList == null) return new { active = false, reason = "No chaser controller" };

            int chaserCount = 0;
            try { chaserCount = (int)chaserList.Call("get_Count"); } catch { }
            if (chaserCount == 0) return new { active = false, reason = "Mr. X not spawned" };

            var ctrl = chaserList.Call("get_Item", (object)0) as IObject;
            if (ctrl == null) return new { active = false, reason = "Controller null" };

            // Controller-level state
            string state = null;
            bool isGhostMode = false, isActiveArea = false, alwaysChaserMode = false;
            bool isTargetInSafeRoom = false;
            string stayMapID = null, stayLocationID = null;
            float alwaysChaserDisableTimer = 0;

            try {
                var stateVal = Convert.ToInt32(ctrl.GetField("State").ToString());
                var stateNames = new[] { "Stop", "Move", "Wait", "DoorOpening", "DoorPassing", "GhostDoorOpening" };
                state = stateVal >= 0 && stateVal < stateNames.Length ? stateNames[stateVal] : stateVal.ToString();
            } catch { }
            try { isGhostMode = (bool)ctrl.Call("get_IsGhostMode"); } catch { }
            try { isActiveArea = (bool)ctrl.GetField("IsActiveArea"); } catch { }
            try { alwaysChaserMode = (bool)ctrl.Call("get_AlwaysChaserMode"); } catch { }
            try { isTargetInSafeRoom = (bool)ctrl.Call("get_IsTargetInSafeRoom"); } catch { }
            try { stayMapID = CallEnumMethod(ctrl, "get_StayMapID"); } catch { }
            try { stayLocationID = CallEnumMethod(ctrl, "get_StayLocationID"); } catch { }
            try { alwaysChaserDisableTimer = Convert.ToSingle(ctrl.Call("get_AlwaysChaserDisableTimer").ToString()); } catch { }

            // Resolve location name
            string locationName = null;
            if (stayLocationID != null) {
                var locCode = stayLocationID.Split(' ')[0];
                s_re2LocationNames.TryGetValue(locCode, out locationName);
                locationName = locationName ?? locCode;
            }

            // Think (AI brain) state
            bool isSleeping = false, isEncounting = false, isKillingMode = false;
            bool isThinkGhostMode = false, isScreenTarget = false, isDoorOpening = false;
            float playerDistanceSq = 0, killingTimer = 0, sleepTimer = 0;
            float inAttackSightTimer = 0;

            try {
                var think = ctrl.Call("get_Think") as IObject;
                if (think != null) {
                    try { isSleeping = (bool)think.Call("get_IsSleeping"); } catch { }
                    try { isEncounting = (bool)think.Call("get_IsEncounting"); } catch { }
                    try { isKillingMode = (bool)think.Call("get_IsKillingMode"); } catch { }
                    try { isThinkGhostMode = (bool)think.Call("get_IsGhostMode"); } catch { }
                    try { isScreenTarget = (bool)think.Call("get_IsScreenTarget"); } catch { }
                    try { isDoorOpening = (bool)think.Call("get_IsDoorOpening"); } catch { }
                    try { playerDistanceSq = Convert.ToSingle(think.GetField("PlayerDistanceSq").ToString()); } catch { }
                    try { killingTimer = Convert.ToSingle(think.Call("get_KillingTimer").ToString()); } catch { }
                    try { sleepTimer = Convert.ToSingle(think.Call("get_SleepTimer").ToString()); } catch { }
                    try { inAttackSightTimer = Convert.ToSingle(think.Call("get_InAttackSightTimer").ToString()); } catch { }
                }
            } catch { }

            float playerDistance = (float)Math.Round(Math.Sqrt(playerDistanceSq), 2);

            // Derive a human-readable status
            string status = isSleeping ? "Stunned"
                : isThinkGhostMode ? "Teleporting"
                : !isEncounting ? "Searching"
                : isKillingMode ? "Chasing (Aggressive)"
                : "Chasing";

            return new {
                active = true,
                status,
                state,
                location = locationName ?? stayLocationID,
                locationId = stayLocationID,
                mapId = stayMapID,
                playerDistance,
                playerDistanceSq = Math.Round(playerDistanceSq, 2),
                ai = new {
                    isSleeping,
                    isEncounting,
                    isKillingMode,
                    isGhostMode = isThinkGhostMode || isGhostMode,
                    isScreenTarget,
                    isDoorOpening,
                    isTargetInSafeRoom,
                    alwaysChaserMode
                },
                timers = new {
                    killingTimer = Math.Round(killingTimer, 1),
                    sleepTimer = Math.Round(sleepTimer, 1),
                    inAttackSightTimer = Math.Round(inAttackSightTimer, 1),
                    alwaysChaserDisableTimer = Math.Round(alwaysChaserDisableTimer, 1)
                }
            };
        } catch (Exception e) {
            return new { error = e.Message };
        }
    }

    // ── RE2 Run Stats ────────────────────────────────────────────────────

    static object GetStatsRE2() {
        try {
            var result = new Dictionary<string, object>();

            // Play time from GameClock (values in microseconds)
            try {
                var clock = API.GetManagedSingleton("app.ropeway.GameClock") as IObject;
                if (clock != null) {
                    long gameElapsed = 0, actualPlaying = 0, inventoryTime = 0, pauseTime = 0, demoTime = 0, inSceneTime = 0;
                    try { gameElapsed = Convert.ToInt64(clock.Call("get_GameElapsedTime").ToString()); } catch { }
                    try { actualPlaying = Convert.ToInt64(clock.Call("get_ActualPlayingTime").ToString()); } catch { }
                    try { inventoryTime = Convert.ToInt64(clock.Call("get_InventorySpendingTime").ToString()); } catch { }
                    try { pauseTime = Convert.ToInt64(clock.Call("get_PauseSpendingTime").ToString()); } catch { }
                    try { demoTime = Convert.ToInt64(clock.Call("get_DemoSpendingTime").ToString()); } catch { }
                    try { inSceneTime = Convert.ToInt64(clock.Call("get_InSceneTime").ToString()); } catch { }
                    result["time"] = new {
                        totalSeconds = Math.Round(gameElapsed / 1000000.0, 1),
                        playingSeconds = Math.Round(actualPlaying / 1000000.0, 1),
                        inventorySeconds = Math.Round(inventoryTime / 1000000.0, 1),
                        pauseSeconds = Math.Round(pauseTime / 1000000.0, 1),
                        cutsceneSeconds = Math.Round(demoTime / 1000000.0, 1),
                        inSceneSeconds = Math.Round(inSceneTime / 1000000.0, 1)
                    };
                }
            } catch { }

            // Record stats from RecordManager
            try {
                var rec = API.GetManagedSingleton("app.ropeway.gamemastering.RecordManager") as IObject;
                if (rec != null) {
                    // Formatted play time
                    int hours = 0, minutes = 0, seconds = 0;
                    try { hours = Convert.ToInt32(rec.Call("get_RecordHour").ToString()); } catch { }
                    try { minutes = Convert.ToInt32(rec.Call("get_RecordMinute").ToString()); } catch { }
                    try { seconds = Convert.ToInt32(rec.Call("get_RecordSecond").ToString()); } catch { }
                    result["recordTime"] = $"{hours}:{minutes:D2}:{seconds:D2}";

                    // Run counters
                    try { result["steps"] = Convert.ToInt32(rec.Call("get_NumberOfSteps").ToString()); } catch { }
                    try { result["healsUsed"] = Convert.ToInt32(rec.Call("get_UseHealItem").ToString()); } catch { }
                    try { result["saveCount"] = Convert.ToInt32(rec.Call("get_CurrentSaveCount").ToString()); } catch { }
                    try { result["itemBoxOpens"] = Convert.ToInt32(rec.Call("get_OpenItemBox").ToString()); } catch { }
                    try { result["usedSpecialWeapon"] = (bool)rec.Call("get_UseSpecialWeapon"); } catch { }
                    try { result["usedAimAssist"] = (bool)rec.Call("get_UseAimAssist"); } catch { }

                    // Collectibles
                    int conceptArtTotal = 0, conceptArtOpened = 0, figureTotal = 0, figureOpened = 0;
                    try { conceptArtTotal = Convert.ToInt32(rec.Call("getConceptArtCount").ToString()); } catch { }
                    try { conceptArtOpened = Convert.ToInt32(rec.Call("getConceptArtCountOpened").ToString()); } catch { }
                    try { figureTotal = Convert.ToInt32(rec.Call("getFigureCount").ToString()); } catch { }
                    try { figureOpened = Convert.ToInt32(rec.Call("getFigureCountOpened").ToString()); } catch { }
                    result["collectibles"] = new {
                        conceptArt = new { opened = conceptArtOpened, total = conceptArtTotal },
                        figures = new { opened = figureOpened, total = figureTotal }
                    };

                    // Scenario clears
                    result["scenarioClears"] = new {
                        leon1st = SafeBool(rec, "get_IsClearedMainScenario1stLeon"),
                        claire1st = SafeBool(rec, "get_IsClearedMainScenario1stClaire"),
                        leon2nd = SafeBool(rec, "get_IsClearedMainScenario2ndLeon"),
                        claire2nd = SafeBool(rec, "get_IsClearedMainScenario2ndClaire")
                    };

                    // Unlocks
                    result["unlocks"] = new {
                        the4thSurvivor = SafeBool(rec, "get_IsOpened4th"),
                        tofu = SafeBool(rec, "get_IsOpenedTofu"),
                        hardMode = SafeBool(rec, "get_IsOpenedHardMode"),
                        rogue = SafeBool(rec, "get_IsOpenedRogue"),
                        claireB = SafeBool(rec, "get_IsOpenedClaireB"),
                        leonB = SafeBool(rec, "get_IsOpenedLeonB")
                    };
                }
            } catch { }

            return result;
        } catch (Exception e) {
            return new { error = e.Message };
        }
    }

    static bool SafeBool(IObject obj, string method) {
        try { return (bool)obj.Call(method); } catch { return false; }
    }
#endif

    // ── Chain endpoint ───────────────────────────────────────────────────

    static object PostExplorerChain(HttpListenerRequest request) {
        try {
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            var body = reader.ReadToEnd();
            var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            // Resolve start object
            var startProp = root.GetProperty("start");
            IObject startObj = null;

            if (startProp.TryGetProperty("singleton", out var singletonProp)) {
                var typeName = singletonProp.GetString();
                // Search managed singletons
                try {
                    foreach (var desc in API.GetManagedSingletons()) {
                        if (desc.Instance?.GetTypeDefinition().GetFullName() == typeName) {
                            startObj = desc.Instance; break;
                        }
                    }
                } catch { }
                // Search native singletons
                if (startObj == null) {
                    try {
                        foreach (var desc in API.GetNativeSingletons()) {
                            if (desc.Instance?.GetTypeDefinition().GetFullName() == typeName) {
                                startObj = desc.Instance; break;
                            }
                        }
                    } catch { }
                }
                if (startObj == null) return new { error = $"Singleton '{typeName}' not found" };
            } else {
                startObj = ResolveObjectFromParams(
                    startProp.GetProperty("address").GetString(),
                    startProp.GetProperty("kind").GetString(),
                    startProp.GetProperty("typeName").GetString());
            }

            if (startObj == null) return new { error = "Could not resolve start object" };

            // Process steps
            var steps = root.GetProperty("steps");
            var current = new List<IObject> { startObj };

            foreach (var step in steps.EnumerateArray()) {
                var stepType = step.GetProperty("type").GetString();

                switch (stepType) {
                    case "method": {
                        var methodName = step.GetProperty("name").GetString();
                        var sig = step.TryGetProperty("signature", out var sigP) ? sigP.GetString() : null;
                        var next = new List<IObject>();
                        foreach (var obj in current) {
                            try {
                                var tdef = obj.GetTypeDefinition();
                                var method = FindMethod(tdef, methodName, sig);
                                if (method == null) continue;
                                object result = null;
                                obj.HandleInvokeMember_Internal(method, null, ref result);
                                if (result is IObject ioResult) next.Add(ioResult);
                            } catch { }
                        }
                        current = next;
                        break;
                    }
                    case "field": {
                        var fieldName = step.GetProperty("name").GetString();
                        var next = new List<IObject>();
                        foreach (var obj in current) {
                            try {
                                var child = obj.GetField(fieldName) as IObject;
                                if (child != null) next.Add(child);
                            } catch { }
                        }
                        current = next;
                        break;
                    }
                    case "array": {
                        int offset = step.TryGetProperty("offset", out var offP) ? offP.GetInt32() : 0;
                        int count = step.TryGetProperty("count", out var cntP) ? cntP.GetInt32() : 10000;
                        var next = new List<IObject>();
                        foreach (var obj in current) {
                            try {
                                var easyArray = obj.TryAs<_System.Array>();
                                if (easyArray == null) continue;
                                int len = easyArray.Length;
                                int end = Math.Min(offset + count, len);
                                for (int i = offset; i < end; i++) {
                                    var el = easyArray.GetValue(i);
                                    if (el is IObject ioEl) next.Add(ioEl);
                                }
                            } catch { }
                        }
                        current = next;
                        break;
                    }
                    case "filter": {
                        var filterMethod = step.GetProperty("method").GetString();
                        var filterValue = step.TryGetProperty("value", out var fvP) ? fvP.GetString() : "True";
                        var next = new List<IObject>();
                        foreach (var obj in current) {
                            try {
                                var tdef = obj.GetTypeDefinition();
                                var method = FindMethod(tdef, filterMethod, null);
                                if (method == null) continue;
                                object result = null;
                                obj.HandleInvokeMember_Internal(method, null, ref result);
                                if (result?.ToString() == filterValue) next.Add(obj);
                            } catch { }
                        }
                        current = next;
                        break;
                    }
                    case "collect": {
                        // Terminal step: read multiple methods on each object, return results
                        var methods = step.GetProperty("methods");
                        var collected = new List<object>();
                        foreach (var obj in current) {
                            var entry = new Dictionary<string, object>();
                            var tdef = obj.GetTypeDefinition();
                            foreach (var mProp in methods.EnumerateArray()) {
                                var mName = mProp.GetString();
                                try {
                                    var method = FindMethod(tdef, mName, null);
                                    if (method == null) { entry[mName] = null; continue; }
                                    object result = null;
                                    obj.HandleInvokeMember_Internal(method, null, ref result);
                                    if (result is REFrameworkNET.ValueType vtRes) {
                                        entry[mName] = ReadValueTypeInline(vtRes);
                                    } else if (result is IObject ioRes) {
                                        entry[mName] = new {
                                            isObject = true,
                                            address = "0x" + ioRes.GetAddress().ToString("X"),
                                            kind = ioRes is ManagedObject ? "managed" : "native",
                                            typeName = ioRes.GetTypeDefinition().GetFullName()
                                        };
                                    } else {
                                        entry[mName] = result?.ToString();
                                    }
                                } catch { entry[mName] = null; }
                            }
                            collected.Add(entry);
                        }
                        return new { count = collected.Count, results = collected };
                    }
                }

                if (current.Count == 0)
                    return new { error = $"Chain broken at step '{stepType}': no results" };
            }

            // Default terminal: return addresses/types of current objects
            var finalResults = new List<object>();
            foreach (var obj in current) {
                try {
                    if (obj is REFrameworkNET.ValueType) {
                        finalResults.Add(ReadValueTypeInline(obj));
                    } else {
                        var tdef = obj.GetTypeDefinition();
                        bool managed = obj is ManagedObject;
                        finalResults.Add(new {
                            address = "0x" + obj.GetAddress().ToString("X"),
                            kind = managed ? "managed" : "native",
                            typeName = tdef.GetFullName()
                        });
                    }
                } catch { }
            }

            return new { count = finalResults.Count, results = finalResults };
        } catch (Exception e) {
            return new { error = e.Message };
        }
    }

    // ── Explorer endpoints ────────────────────────────────────────────

    static object GetExplorerSingletons() {
        var managedList = new List<object>();
        var nativeList = new List<object>();

        try {
            var managed = API.GetManagedSingletons();
            managed.RemoveAll(s => s.Instance == null);
            managed.Sort((a, b) => a.Instance.GetTypeDefinition().GetFullName()
                .CompareTo(b.Instance.GetTypeDefinition().GetFullName()));

            foreach (var desc in managed) {
                var instance = desc.Instance;
                var tdef = instance.GetTypeDefinition();
                managedList.Add(new {
                    type = tdef.GetFullName(),
                    address = "0x" + instance.GetAddress().ToString("X"),
                    kind = "managed"
                });
            }
        } catch { }

        try {
            var native = API.GetNativeSingletons();
            native.Sort((a, b) => a.Instance.GetTypeDefinition().GetFullName()
                .CompareTo(b.Instance.GetTypeDefinition().GetFullName()));

            foreach (var desc in native) {
                var instance = desc.Instance;
                if (instance == null) continue;
                var tdef = instance.GetTypeDefinition();
                nativeList.Add(new {
                    type = tdef.GetFullName(),
                    address = "0x" + instance.GetAddress().ToString("X"),
                    kind = "native"
                });
            }
        } catch { }

        return new { managed = managedList, native = nativeList };
    }

    static object GetExplorerObject(HttpListenerRequest request) {
        try {
            var obj = ResolveObject(request);
            if (obj == null) return new { error = "Could not resolve object" };

            var qs = request.QueryString;
            bool noFields = string.Equals(qs["noFields"], "true", StringComparison.OrdinalIgnoreCase);
            bool noMethods = string.Equals(qs["noMethods"], "true", StringComparison.OrdinalIgnoreCase);
            var filterFields = qs["fields"];   // comma-separated field names
            var filterMethods = qs["methods"]; // comma-separated method names

            var tdef = obj.GetTypeDefinition();
            var typeName = tdef.GetFullName();

            int? refCount = null;
            if (obj is ManagedObject managed) {
                refCount = managed.GetReferenceCount();
            }

            // Collect fields from type hierarchy
            List<object> fieldList = null;
            if (!noFields) {
                var fields = new List<Field>();
                for (var parent = tdef; parent != null; parent = parent.ParentType) {
                    fields.AddRange(parent.GetFields());
                }
                fields.Sort((a, b) => a.GetName().CompareTo(b.GetName()));

                HashSet<string> wantFields = null;
                if (filterFields != null) {
                    wantFields = new HashSet<string>(filterFields.Split(','), StringComparer.OrdinalIgnoreCase);
                }

                fieldList = new List<object>();
                foreach (var field in fields) {
                    if (wantFields != null && !wantFields.Contains(field.GetName())) continue;

                    var ft = field.GetType();
                    var ftName = ft != null ? ft.GetFullName() : "null";
                    bool isValueType = ft != null && ft.IsValueType();
                    bool isStatic = field.IsStatic();

                    string value = null;
                    if (ft != null && (isValueType || ftName == "System.String")) {
                        try { value = ReadFieldValueAsString(obj, field, ft); } catch { }
                    }

                    ulong fieldAddr = 0;
                    if (isStatic) {
                        try { fieldAddr = field.GetDataRaw(obj.GetAddress(), false); } catch { }
                    } else {
                        fieldAddr = (obj as UnifiedObject)?.GetAddress() ?? 0;
                        fieldAddr += field.GetOffsetFromBase();
                    }

                    fieldList.Add(new {
                        name = field.GetName(),
                        typeName = ftName,
                        isValueType,
                        isStatic,
                        offset = isStatic ? (string)null : "0x" + field.GetOffsetFromBase().ToString("X"),
                        value
                    });
                }
            }

            // Collect methods from type hierarchy
            List<object> methodList = null;
            if (!noMethods) {
                var methods = new List<Method>();
                for (var parent = tdef; parent != null; parent = parent.ParentType) {
                    methods.AddRange(parent.GetMethods());
                }
                methods.Sort((a, b) => a.GetName().CompareTo(b.GetName()));
                methods.RemoveAll(m => m.GetParameters().Exists(p => p.Type.Name.Contains("!")));

                HashSet<string> wantMethods = null;
                if (filterMethods != null) {
                    wantMethods = new HashSet<string>(filterMethods.Split(','), StringComparer.OrdinalIgnoreCase);
                }

                methodList = new List<object>();
                foreach (var method in methods) {
                    if (wantMethods != null && !wantMethods.Contains(method.GetName())) continue;

                    var returnT = method.GetReturnType();
                    var returnTName = returnT != null ? returnT.GetFullName() : "void";

                    var ps = method.GetParameters();
                    var paramList = new List<object>();
                    foreach (var p in ps) {
                        paramList.Add(new {
                            type = p.Type.GetFullName(),
                            name = p.Name
                        });
                    }

                    bool isGetter = (method.Name.StartsWith("get_") || method.Name.StartsWith("Get") || method.Name == "ToString") && ps.Count == 0;

                    methodList.Add(new {
                        name = method.GetName(),
                        returnType = returnTName,
                        parameters = paramList,
                        isGetter,
                        signature = method.GetMethodSignature()
                    });
                }
            }

            // Check if array
            bool isArray = tdef.IsDerivedFrom(s_systemArrayT);
            int? arrayLength = null;
            if (isArray) {
                try { arrayLength = (int)obj.Call("get_Length"); } catch { }
            }

            return new {
                typeName,
                address = "0x" + (obj as UnifiedObject).GetAddress().ToString("X"),
                refCount,
                fields = fieldList,
                methods = methodList,
                isArray,
                arrayLength
            };
        } catch (Exception e) {
            return new { error = e.Message };
        }
    }

    static object GetExplorerSummary(HttpListenerRequest request) {
        try {
            var obj = ResolveObject(request);
            if (obj == null) return new { error = "Could not resolve object" };

            var tdef = obj.GetTypeDefinition();
            var typeName = tdef.GetFullName();

            // Short type name helper: strip namespaces, clean backtick generics
            // "System.Collections.Generic.List`1<app.Foo.Bar>" → "List<Bar>"
            static string ShortType(string fullName) {
                if (fullName == null) return "?";
                // Strip namespaces inside generic args first
                var result = System.Text.RegularExpressions.Regex.Replace(
                    fullName, @"[\w]+\.", m => {
                        // Only strip if it looks like a namespace segment (lowercase or known prefixes)
                        var seg = m.Value.TrimEnd('.');
                        if (seg == "System" || seg == "Collections" || seg == "Generic" ||
                            seg == "app" || seg == "ace" || seg == "via" || seg == "soundlib" ||
                            seg.Contains("_") || char.IsLower(seg[0]))
                            return "";
                        return m.Value;
                    });
                // Clean up generic backtick: "List`1<Foo>" → "List<Foo>"
                result = System.Text.RegularExpressions.Regex.Replace(result, @"`\d+", "");
                return result;
            }

            // Fields: "name: ShortType" or "name: ShortType = value" for primitives
            var fields = new List<Field>();
            for (var parent = tdef; parent != null; parent = parent.ParentType)
                fields.AddRange(parent.GetFields());
            fields.Sort((a, b) => a.GetName().CompareTo(b.GetName()));

            var fieldLines = new List<string>();
            foreach (var field in fields) {
                var ft = field.GetType();
                var ftName = ft != null ? ft.GetFullName() : "?";
                bool isValueType = ft != null && ft.IsValueType();
                string line = field.GetName() + ": " + ShortType(ftName);
                if (field.IsStatic()) line += " [static]";
                if (ft != null && (isValueType || ftName == "System.String")) {
                    try {
                        var val = ReadFieldValueAsString(obj, field, ft);
                        if (val != null) line += " = " + val;
                    } catch { }
                }
                fieldLines.Add(line);
            }

            // Methods: "name(ParamType, ...) → ReturnType" but skip .ctor, .cctor, dupes
            var methods = new List<Method>();
            for (var parent = tdef; parent != null; parent = parent.ParentType)
                methods.AddRange(parent.GetMethods());
            methods.Sort((a, b) => a.GetName().CompareTo(b.GetName()));
            methods.RemoveAll(m => m.GetParameters().Exists(p => p.Type.Name.Contains("!")));

            var seen = new HashSet<string>();
            var methodLines = new List<string>();
            foreach (var method in methods) {
                var name = method.GetName();
                if (name == ".ctor" || name == ".cctor" || name == "Finalize" || name == "MemberwiseClone") continue;
                if (name == "Equals" || name == "GetHashCode" || name == "GetType") continue;
                // Skip compiler-generated lambda methods (noise)
                if (name.Contains(">g__") || name.Contains("<>")) continue;

                var ps = method.GetParameters();
                var paramStr = string.Join(", ", ps.Select(p => ShortType(p.Type.GetFullName())));
                var retType = method.GetReturnType();
                var retStr = retType != null ? ShortType(retType.GetFullName()) : "Void";
                var line = $"{name}({paramStr}) → {retStr}";
                if (seen.Add(line)) methodLines.Add(line);
            }

            return new { typeName, fields = fieldLines, methods = methodLines };
        } catch (Exception e) {
            return new { error = e.Message };
        }
    }

    static object GetExplorerField(HttpListenerRequest request) {
        try {
            var obj = ResolveObject(request);
            if (obj == null) return new { error = "Could not resolve object" };

            var fieldName = request.QueryString["fieldName"];
            if (string.IsNullOrEmpty(fieldName)) return new { error = "fieldName required" };

            var child = obj.GetField(fieldName) as IObject;
            if (child == null) return new { isNull = true };

            var childTdef = child.GetTypeDefinition();
            bool childManaged = child is ManagedObject;

            return new {
                isNull = false,
                childAddress = "0x" + child.GetAddress().ToString("X"),
                childKind = childManaged ? "managed" : "native",
                childTypeName = childTdef.GetFullName()
            };
        } catch (Exception e) {
            return new { error = e.Message };
        }
    }

    static object GetExplorerMethod(HttpListenerRequest request) {
        try {
            var obj = ResolveObject(request);
            if (obj == null) return new { error = "Could not resolve object" };

            var methodName = request.QueryString["methodName"];
            var methodSignature = request.QueryString["methodSignature"];
            if (string.IsNullOrEmpty(methodName)) return new { error = "methodName required" };

            // Find the method by name and optionally signature
            var tdef = obj.GetTypeDefinition();
            Method targetMethod = null;
            for (var parent = tdef; parent != null; parent = parent.ParentType) {
                foreach (var m in parent.GetMethods()) {
                    if (m.GetName() == methodName) {
                        if (!string.IsNullOrEmpty(methodSignature) && m.GetMethodSignature() != methodSignature)
                            continue;
                        targetMethod = m;
                        break;
                    }
                }
                if (targetMethod != null) break;
            }

            if (targetMethod == null) return new { error = "Method not found" };

            // Only invoke 0-parameter methods (getters, ToString, or other read-only calls)
            var ps = targetMethod.GetParameters();
            if (ps.Count != 0) return new { error = "Method has parameters, use invoke_method instead" };

            object result = null;
            obj.HandleInvokeMember_Internal(targetMethod, null, ref result);

            if (result == null) return new { isObject = false, value = "null" };

            if (result is IObject objResult) {
                // ValueType results are ephemeral — read inline
                if (objResult is REFrameworkNET.ValueType) {
                    return ReadValueTypeInline(objResult);
                }

                var childTdef = objResult.GetTypeDefinition();
                bool childManaged = objResult is ManagedObject;
                return new {
                    isObject = true,
                    childAddress = "0x" + objResult.GetAddress().ToString("X"),
                    childKind = childManaged ? "managed" : "native",
                    childTypeName = childTdef.GetFullName()
                };
            }

            // Primitive result - check for enum
            var returnType = targetMethod.GetReturnType();
            if (returnType != null && returnType.IsEnum()) {
                long longValue = Convert.ToInt64(result);
                try {
                    var boxedEnum = BoxEnum(returnType, longValue);
                    return new { isObject = false, value = (boxedEnum as IObject).Call("ToString()") + " (" + result.ToString() + ")" };
                } catch { }
            }

            return new { isObject = false, value = result.ToString() };
        } catch (Exception e) {
            return new { error = e.Message };
        }
    }

    static object GetExplorerArray(HttpListenerRequest request) {
        try {
            var obj = ResolveObject(request);
            if (obj == null) return new { error = "Could not resolve object" };

            var tdef = obj.GetTypeDefinition();
            if (!tdef.IsDerivedFrom(s_systemArrayT))
                return new { error = "Object is not an array" };

            int offset = 0;
            int count = 50;
            if (!string.IsNullOrEmpty(request.QueryString["offset"]))
                int.TryParse(request.QueryString["offset"], out offset);
            if (!string.IsNullOrEmpty(request.QueryString["count"]))
                int.TryParse(request.QueryString["count"], out count);

            var easyArray = obj.As<_System.Array>();
            int totalLength = easyArray.Length;

            int end = Math.Min(offset + count, totalLength);
            var elements = new List<object>();

            for (int i = offset; i < end; i++) {
                try {
                    var element = easyArray.GetValue(i);
                    if (element == null) {
                        elements.Add(new { index = i, isNull = true, isObject = false, value = "null" });
                        continue;
                    }

                    if (element is IObject objElement) {
                        string display = null;
                        try { display = objElement.Call("ToString()") as string; } catch { }
                        var elTdef = objElement.GetTypeDefinition();
                        bool elManaged = objElement is ManagedObject;
                        elements.Add(new {
                            index = i,
                            isNull = false,
                            isObject = true,
                            address = "0x" + objElement.GetAddress().ToString("X"),
                            kind = elManaged ? "managed" : "native",
                            typeName = elTdef.GetFullName(),
                            display
                        });
                    } else {
                        elements.Add(new {
                            index = i,
                            isNull = false,
                            isObject = false,
                            value = element.ToString()
                        });
                    }
                } catch {
                    elements.Add(new { index = i, isNull = false, isObject = false, value = "error" });
                }
            }

            return new {
                totalLength,
                offset,
                count = elements.Count,
                hasMore = end < totalLength,
                elements
            };
        } catch (Exception e) {
            return new { error = e.Message };
        }
    }

    static object GetExplorerSearch(HttpListenerRequest request) {
        try {
            var query = request.QueryString["query"];
            if (string.IsNullOrEmpty(query)) return new { error = "query parameter required" };

            int limit = 50;
            if (!string.IsNullOrEmpty(request.QueryString["limit"]))
                int.TryParse(request.QueryString["limit"], out limit);

            var tdb = TDB.Get();
            uint numTypes = tdb.GetNumTypes();
            var queryLower = query.ToLower();
            var results = new List<object>();

            for (uint i = 0; i < numTypes && results.Count < limit; i++) {
                try {
                    var t = tdb.GetType(i);
                    if (t == null) continue;
                    var fullName = t.GetFullName();
                    if (string.IsNullOrEmpty(fullName)) continue;
                    if (!fullName.ToLower().Contains(queryLower)) continue;

                    var parentT = t.ParentType;
                    results.Add(new {
                        fullName,
                        isValueType = t.IsValueType(),
                        isEnum = t.IsEnum(),
                        numFields = (int)t.GetNumFields(),
                        numMethods = (int)t.GetNumMethods(),
                        parentType = parentT?.GetFullName()
                    });
                } catch { }
            }

            return new { query, count = results.Count, results };
        } catch (Exception e) {
            return new { error = e.Message };
        }
    }

    static object GetExplorerType(HttpListenerRequest request) {
        try {
            var typeName = request.QueryString["typeName"];
            if (string.IsNullOrEmpty(typeName)) return new { error = "typeName parameter required" };

            var tdef = TDB.Get().GetType(typeName);
            if (tdef == null) return new { error = $"Type '{typeName}' not found" };

            var parentT = tdef.ParentType;
            var declaringT = tdef.DeclaringType;
            var qs = request.QueryString;
            bool includeInherited = string.Equals(qs["includeInherited"], "true", StringComparison.OrdinalIgnoreCase);
            bool noFields = string.Equals(qs["noFields"], "true", StringComparison.OrdinalIgnoreCase);
            bool noMethods = string.Equals(qs["noMethods"], "true", StringComparison.OrdinalIgnoreCase);

            // Collect fields — own type only by default, full hierarchy if requested
            var fields = new List<Field>();
            if (!noFields) {
                if (includeInherited) {
                    for (var parent = tdef; parent != null; parent = parent.ParentType)
                        fields.AddRange(parent.GetFields());
                } else {
                    fields.AddRange(tdef.GetFields());
                }
                fields.Sort((a, b) => a.GetName().CompareTo(b.GetName()));
            }

            var fieldList = new List<object>();
            foreach (var field in fields) {
                var ft = field.GetType();
                var ftName = ft != null ? ft.GetFullName() : "null";
                bool isValueType = ft != null && ft.IsValueType();
                bool isStatic = field.IsStatic();

                fieldList.Add(new {
                    name = field.GetName(),
                    typeName = ftName,
                    isValueType,
                    isStatic,
                    offset = isStatic ? (string)null : "0x" + field.GetOffsetFromBase().ToString("X")
                });
            }

            // Collect methods — own type only by default, full hierarchy if requested
            var methods = new List<Method>();
            if (!noMethods) {
                if (includeInherited) {
                    for (var parent = tdef; parent != null; parent = parent.ParentType)
                        methods.AddRange(parent.GetMethods());
                } else {
                    methods.AddRange(tdef.GetMethods());
                }
                methods.Sort((a, b) => a.GetName().CompareTo(b.GetName()));
                methods.RemoveAll(m => m.GetParameters().Exists(p => p.Type.Name.Contains("!")));
                // Filter noise: constructors, finalizers, common inherited methods, compiler-generated
                methods.RemoveAll(m => {
                    var name = m.GetName();
                    return name == ".ctor" || name == ".cctor" || name == "Finalize" || name == "MemberwiseClone"
                        || name == "Equals" || name == "GetHashCode" || name == "GetType"
                        || name.StartsWith("<")
                        || name.Contains(">g__") || name.Contains("<>");
                });
            }

            var seen = new HashSet<string>();
            var dedupedMethods = new List<(string returnType, string signature)>();
            foreach (var method in methods) {
                var returnT = method.GetReturnType();
                var returnTName = returnT != null ? returnT.GetFullName() : "void";
                var sig = method.GetMethodSignature();
                if (!seen.Add(sig)) continue;
                dedupedMethods.Add((returnTName, sig));
            }

            // Collapse repetitive auto-generated methods (names differing only in numbers).
            // Normalize full signature (digit runs → #), if group has >2 members show one + count.
            var methodList = new List<object>();
            var groups = dedupedMethods.GroupBy(m =>
                System.Text.RegularExpressions.Regex.Replace(m.signature, @"\d+", "#")
            ).ToList();

            foreach (var group in groups) {
                var first = group.First();
                if (group.Count() > 2) {
                    methodList.Add(new {
                        returnType = first.returnType,
                        signature = first.signature,
                        similarCount = group.Count()
                    });
                } else {
                    foreach (var m in group)
                        methodList.Add(new { returnType = m.returnType, signature = m.signature });
                }
            }

            // Count totals (before filtering) for informational purposes
            int totalFields = noFields ? tdef.GetFields().Count : fieldList.Count;
            int totalMethods = noMethods ? tdef.GetMethods().Count : methodList.Count;

            return new {
                fullName = tdef.GetFullName(),
                @namespace = tdef.GetNamespace(),
                isValueType = tdef.IsValueType(),
                isEnum = tdef.IsEnum(),
                size = tdef.GetSize(),
                parentType = parentT?.GetFullName(),
                declaringType = declaringT?.GetFullName(),
                fieldCount = totalFields,
                methodCount = totalMethods,
                fields = fieldList,
                methods = methodList
            };
        } catch (Exception e) {
            return new { error = e.Message };
        }
    }

    static object GetExplorerSingleton(HttpListenerRequest request) {
        try {
            var typeName = request.QueryString["typeName"];
            if (string.IsNullOrEmpty(typeName)) return new { error = "typeName parameter required" };

            // Search managed singletons
            try {
                var managed = API.GetManagedSingletons();
                foreach (var desc in managed) {
                    var instance = desc.Instance;
                    if (instance == null) continue;
                    if (instance.GetTypeDefinition().GetFullName() == typeName) {
                        return new {
                            address = "0x" + instance.GetAddress().ToString("X"),
                            kind = "managed",
                            typeName
                        };
                    }
                }
            } catch { }

            // Search native singletons
            try {
                var native = API.GetNativeSingletons();
                foreach (var desc in native) {
                    var instance = desc.Instance;
                    if (instance == null) continue;
                    if (instance.GetTypeDefinition().GetFullName() == typeName) {
                        return new {
                            address = "0x" + instance.GetAddress().ToString("X"),
                            kind = "native",
                            typeName
                        };
                    }
                }
            } catch { }

            return new { error = $"Singleton '{typeName}' not found" };
        } catch (Exception e) {
            return new { error = e.Message };
        }
    }

    static object PostExplorerField(HttpListenerRequest request) {
        try {
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            var body = reader.ReadToEnd();
            var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var addressStr = root.GetProperty("address").GetString();
            var kind = root.GetProperty("kind").GetString();
            var typeName = root.GetProperty("typeName").GetString();
            var fieldName = root.GetProperty("fieldName").GetString();

            var obj = ResolveObjectFromParams(addressStr, kind, typeName);
            if (obj == null) return new { error = "Could not resolve object" };

            // Find field by walking parent chain
            var tdef = obj.GetTypeDefinition();
            Field targetField = null;
            for (var parent = tdef; parent != null; parent = parent.ParentType) {
                foreach (var f in parent.GetFields()) {
                    if (f.GetName() == fieldName) {
                        targetField = f;
                        break;
                    }
                }
                if (targetField != null) break;
            }

            if (targetField == null) return new { error = $"Field '{fieldName}' not found" };

            var ft = targetField.GetType();
            if (ft == null) return new { error = "Field type is null" };
            if (!ft.IsValueType()) return new { error = "Can only write value-type fields" };

            // Determine type name for parsing
            var valueTypeName = root.TryGetProperty("valueType", out var vtProp) ? vtProp.GetString() : null;
            if (string.IsNullOrEmpty(valueTypeName)) {
                valueTypeName = ft.IsEnum() ? ft.GetUnderlyingType().GetFullName() : ft.GetFullName();
            }

            var valueElement = root.GetProperty("value");
            var boxedValue = ParseValueFromJson(valueElement, valueTypeName);
            if (boxedValue == null) return new { error = $"Could not parse value as '{valueTypeName}'" };

            targetField.SetDataBoxed(obj.GetAddress(), boxedValue, false);

            return new { ok = true, field = fieldName, value = boxedValue.ToString() };
        } catch (Exception e) {
            return new { error = e.Message };
        }
    }

    static object PostExplorerMethod(HttpListenerRequest request) {
        try {
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            var body = reader.ReadToEnd();
            var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var addressStr = root.TryGetProperty("address", out var addrProp) ? addrProp.GetString() : null;
            var kind = root.TryGetProperty("kind", out var kindProp) ? kindProp.GetString() : null;
            var typeName = root.GetProperty("typeName").GetString();
            var methodName = root.GetProperty("methodName").GetString();
            var methodSignature = root.TryGetProperty("methodSignature", out var sigProp) ? sigProp.GetString() : null;

            IObject obj;
            TypeDefinition tdef;

            // Static call: no address provided, create a temporary instance
            if (string.IsNullOrEmpty(addressStr)) {
                tdef = TDB.Get().GetType(typeName);
                if (tdef == null) return new { error = $"Type '{typeName}' not found" };
                // Try managed instance first, fall back to native object at address 0
                obj = tdef.CreateInstance(0) ?? (IObject)new NativeObject(0, tdef);
            } else {
                obj = ResolveObjectFromParams(addressStr, kind, typeName);
                if (obj == null) return new { error = "Could not resolve object" };
                tdef = obj.GetTypeDefinition();
            }

            var targetMethod = FindMethod(tdef, methodName, methodSignature);
            if (targetMethod == null) return new { error = "Method not found" };

            // Parse arguments
            object[] args = null;
            if (root.TryGetProperty("args", out var argsProp) && argsProp.ValueKind == JsonValueKind.Array) {
                var ps = targetMethod.GetParameters();
                args = new object[argsProp.GetArrayLength()];
                int i = 0;
                foreach (var argEl in argsProp.EnumerateArray()) {
                    var argValue = argEl.GetProperty("value");
                    // Use explicit type if provided, otherwise infer from method parameter
                    var argType = argEl.TryGetProperty("type", out var atProp) ? atProp.GetString() : null;
                    if (string.IsNullOrEmpty(argType) && i < ps.Count) {
                        argType = ps[i].Type.GetFullName();
                    }
                    args[i] = ParseValueFromJson(argValue, argType ?? "System.Int32");
                    i++;
                }
            }

            object result = null;
            obj.HandleInvokeMember_Internal(targetMethod, args, ref result);

            return FormatMethodResult(result, targetMethod);
        } catch (Exception e) {
            return new { error = e.Message };
        }
    }

    static object PostExplorerBatch(HttpListenerRequest request) {
        try {
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            var body = reader.ReadToEnd();
            var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (!root.TryGetProperty("operations", out var opsProp) || opsProp.ValueKind != JsonValueKind.Array)
                return new { error = "operations array required" };

            var results = new List<object>();

            foreach (var op in opsProp.EnumerateArray()) {
                try {
                    var opType = op.GetProperty("type").GetString();
                    var parms = op.GetProperty("params");

                    object opResult = opType switch {
                        "singleton" => BatchGetSingleton(parms),
                        "object" => BatchGetObject(parms),
                        "field" => BatchGetField(parms),
                        "method" => BatchInvokeMethod(parms),
                        "search" => BatchSearch(parms),
                        "type" => BatchGetType(parms),
                        "setField" => BatchSetField(parms),
                        _ => new { error = $"Unknown operation type: {opType}" }
                    };
                    results.Add(opResult);
                } catch (Exception e) {
                    results.Add(new { error = e.Message });
                }
            }

            return new { results };
        } catch (Exception e) {
            return new { error = e.Message };
        }
    }

    // ── Batch operation dispatchers ───────────────────────────────────

    static object BatchGetSingleton(JsonElement p) {
        var typeName = p.GetProperty("typeName").GetString();

        try {
            var managed = API.GetManagedSingletons();
            foreach (var desc in managed) {
                var instance = desc.Instance;
                if (instance == null) continue;
                if (instance.GetTypeDefinition().GetFullName() == typeName)
                    return new { address = "0x" + instance.GetAddress().ToString("X"), kind = "managed", typeName };
            }
        } catch { }

        try {
            var native = API.GetNativeSingletons();
            foreach (var desc in native) {
                var instance = desc.Instance;
                if (instance == null) continue;
                if (instance.GetTypeDefinition().GetFullName() == typeName)
                    return new { address = "0x" + instance.GetAddress().ToString("X"), kind = "native", typeName };
            }
        } catch { }

        return new { error = $"Singleton '{typeName}' not found" };
    }

    static object BatchGetObject(JsonElement p) {
        var obj = ResolveObjectFromParams(
            p.GetProperty("address").GetString(),
            p.GetProperty("kind").GetString(),
            p.GetProperty("typeName").GetString());
        if (obj == null) return new { error = "Could not resolve object" };

        bool noFields = p.TryGetProperty("noFields", out var nf) && nf.GetBoolean();
        bool noMethods = p.TryGetProperty("noMethods", out var nm) && nm.GetBoolean();
        string filterFields = p.TryGetProperty("fields", out var ff) ? ff.GetString() : null;
        string filterMethods = p.TryGetProperty("methods", out var fm) ? fm.GetString() : null;

        var tdef = obj.GetTypeDefinition();
        int? refCount = obj is ManagedObject m ? m.GetReferenceCount() : (int?)null;

        List<object> fieldList = null;
        if (!noFields) {
            var fields = new List<Field>();
            for (var parent = tdef; parent != null; parent = parent.ParentType)
                fields.AddRange(parent.GetFields());
            fields.Sort((a, b) => a.GetName().CompareTo(b.GetName()));

            HashSet<string> wantFields = null;
            if (filterFields != null)
                wantFields = new HashSet<string>(filterFields.Split(','), StringComparer.OrdinalIgnoreCase);

            fieldList = new List<object>();
            foreach (var field in fields) {
                if (wantFields != null && !wantFields.Contains(field.GetName())) continue;

                var ft = field.GetType();
                var ftName = ft != null ? ft.GetFullName() : "null";
                bool isValueType = ft != null && ft.IsValueType();
                string value = null;
                if (ft != null && (isValueType || ftName == "System.String")) {
                    try { value = ReadFieldValueAsString(obj, field, ft); } catch { }
                }
                fieldList.Add(new {
                    name = field.GetName(), typeName = ftName, isValueType,
                    isStatic = field.IsStatic(),
                    offset = field.IsStatic() ? (string)null : "0x" + field.GetOffsetFromBase().ToString("X"),
                    value
                });
            }
        }

        List<object> methodList = null;
        if (!noMethods) {
            var methods = new List<Method>();
            for (var parent = tdef; parent != null; parent = parent.ParentType)
                methods.AddRange(parent.GetMethods());
            methods.Sort((a, b) => a.GetName().CompareTo(b.GetName()));
            methods.RemoveAll(mt => mt.GetParameters().Exists(pr => pr.Type.Name.Contains("!")));

            HashSet<string> wantMethods = null;
            if (filterMethods != null)
                wantMethods = new HashSet<string>(filterMethods.Split(','), StringComparer.OrdinalIgnoreCase);

            methodList = new List<object>();
            foreach (var method in methods) {
                if (wantMethods != null && !wantMethods.Contains(method.GetName())) continue;

                var returnT = method.GetReturnType();
                var ps = method.GetParameters();
                var paramList = new List<object>();
                foreach (var pr in ps) paramList.Add(new { type = pr.Type.GetFullName(), name = pr.Name });
                bool isGetter = (method.Name.StartsWith("get_") || method.Name.StartsWith("Get") || method.Name == "ToString") && ps.Count == 0;
                methodList.Add(new {
                    name = method.GetName(), returnType = returnT != null ? returnT.GetFullName() : "void",
                    parameters = paramList, isGetter, signature = method.GetMethodSignature()
                });
            }
        }

        bool isArray = tdef.IsDerivedFrom(s_systemArrayT);
        int? arrayLength = null;
        if (isArray) { try { arrayLength = (int)obj.Call("get_Length"); } catch { } }

        return new {
            typeName = tdef.GetFullName(),
            address = "0x" + (obj as UnifiedObject).GetAddress().ToString("X"),
            refCount, fields = fieldList, methods = methodList, isArray, arrayLength
        };
    }

    static object BatchGetField(JsonElement p) {
        var obj = ResolveObjectFromParams(
            p.GetProperty("address").GetString(),
            p.GetProperty("kind").GetString(),
            p.GetProperty("typeName").GetString());
        if (obj == null) return new { error = "Could not resolve object" };

        var fieldName = p.GetProperty("fieldName").GetString();
        var child = obj.GetField(fieldName) as IObject;
        if (child == null) return new { isNull = true };

        var childTdef = child.GetTypeDefinition();
        bool childManaged = child is ManagedObject;
        return new {
            isNull = false,
            childAddress = "0x" + child.GetAddress().ToString("X"),
            childKind = childManaged ? "managed" : "native",
            childTypeName = childTdef.GetFullName()
        };
    }

    static object BatchInvokeMethod(JsonElement p) {
        var addressStr = p.TryGetProperty("address", out var addrProp) ? addrProp.GetString() : null;
        var kind = p.TryGetProperty("kind", out var kindProp) ? kindProp.GetString() : null;
        var typeName = p.GetProperty("typeName").GetString();

        IObject obj;
        TypeDefinition tdef;

        if (string.IsNullOrEmpty(addressStr)) {
            // Static call
            tdef = TDB.Get().GetType(typeName);
            if (tdef == null) return new { error = $"Type '{typeName}' not found" };
            obj = tdef.CreateInstance(0) ?? (IObject)new NativeObject(0, tdef);
        } else {
            obj = ResolveObjectFromParams(addressStr, kind, typeName);
            if (obj == null) return new { error = "Could not resolve object" };
            tdef = obj.GetTypeDefinition();
        }

        var methodName = p.GetProperty("methodName").GetString();
        var methodSignature = p.TryGetProperty("methodSignature", out var sigProp) ? sigProp.GetString() : null;
        var targetMethod = FindMethod(tdef, methodName, methodSignature);
        if (targetMethod == null) return new { error = "Method not found" };

        object[] args = null;
        if (p.TryGetProperty("args", out var argsProp) && argsProp.ValueKind == JsonValueKind.Array) {
            var ps = targetMethod.GetParameters();
            args = new object[argsProp.GetArrayLength()];
            int i = 0;
            foreach (var argEl in argsProp.EnumerateArray()) {
                var argValue = argEl.GetProperty("value");
                var argType = argEl.TryGetProperty("type", out var atProp) ? atProp.GetString() : null;
                if (string.IsNullOrEmpty(argType) && i < ps.Count) argType = ps[i].Type.GetFullName();
                args[i] = ParseValueFromJson(argValue, argType ?? "System.Int32");
                i++;
            }
        }

        object result = null;
        obj.HandleInvokeMember_Internal(targetMethod, args, ref result);
        return FormatMethodResult(result, targetMethod);
    }

    static object BatchSearch(JsonElement p) {
        var query = p.GetProperty("query").GetString();
        int limit = p.TryGetProperty("limit", out var limProp) ? limProp.GetInt32() : 50;

        var tdb = TDB.Get();
        uint numTypes = tdb.GetNumTypes();
        var queryLower = query.ToLower();
        var results = new List<object>();

        for (uint i = 0; i < numTypes && results.Count < limit; i++) {
            try {
                var t = tdb.GetType(i);
                if (t == null) continue;
                var fullName = t.GetFullName();
                if (string.IsNullOrEmpty(fullName) || !fullName.ToLower().Contains(queryLower)) continue;
                var parentT = t.ParentType;
                results.Add(new {
                    fullName, isValueType = t.IsValueType(), isEnum = t.IsEnum(),
                    numFields = (int)t.GetNumFields(), numMethods = (int)t.GetNumMethods(),
                    parentType = parentT?.GetFullName()
                });
            } catch { }
        }

        return new { query, count = results.Count, results };
    }

    static object BatchGetType(JsonElement p) {
        var typeName = p.GetProperty("typeName").GetString();
        var tdef = TDB.Get().GetType(typeName);
        if (tdef == null) return new { error = $"Type '{typeName}' not found" };

        var parentT = tdef.ParentType;
        var declaringT = tdef.DeclaringType;

        var fields = new List<Field>();
        for (var parent = tdef; parent != null; parent = parent.ParentType) fields.AddRange(parent.GetFields());
        fields.Sort((a, b) => a.GetName().CompareTo(b.GetName()));

        var fieldList = new List<object>();
        foreach (var field in fields) {
            var ft = field.GetType();
            fieldList.Add(new {
                name = field.GetName(), typeName = ft != null ? ft.GetFullName() : "null",
                isValueType = ft != null && ft.IsValueType(), isStatic = field.IsStatic(),
                offset = field.IsStatic() ? (string)null : "0x" + field.GetOffsetFromBase().ToString("X")
            });
        }

        var methods = new List<Method>();
        for (var parent = tdef; parent != null; parent = parent.ParentType) methods.AddRange(parent.GetMethods());
        methods.Sort((a, b) => a.GetName().CompareTo(b.GetName()));
        methods.RemoveAll(mt => mt.GetParameters().Exists(pr => pr.Type.Name.Contains("!")));

        var methodList = new List<object>();
        foreach (var method in methods) {
            var returnT = method.GetReturnType();
            var ps = method.GetParameters();
            var paramList = new List<object>();
            foreach (var pr in ps) paramList.Add(new { type = pr.Type.GetFullName(), name = pr.Name });
            bool isGetter = (method.Name.StartsWith("get_") || method.Name.StartsWith("Get") || method.Name == "ToString") && ps.Count == 0;
            methodList.Add(new {
                name = method.GetName(), returnType = returnT != null ? returnT.GetFullName() : "void",
                parameters = paramList, isGetter, signature = method.GetMethodSignature()
            });
        }

        return new {
            fullName = tdef.GetFullName(), @namespace = tdef.GetNamespace(),
            isValueType = tdef.IsValueType(), isEnum = tdef.IsEnum(), size = tdef.GetSize(),
            parentType = parentT?.GetFullName(), declaringType = declaringT?.GetFullName(),
            fields = fieldList, methods = methodList
        };
    }

    static object BatchSetField(JsonElement p) {
        var obj = ResolveObjectFromParams(
            p.GetProperty("address").GetString(),
            p.GetProperty("kind").GetString(),
            p.GetProperty("typeName").GetString());
        if (obj == null) return new { error = "Could not resolve object" };

        var fieldName = p.GetProperty("fieldName").GetString();
        var tdef = obj.GetTypeDefinition();
        Field targetField = null;
        for (var parent = tdef; parent != null; parent = parent.ParentType) {
            foreach (var f in parent.GetFields()) {
                if (f.GetName() == fieldName) { targetField = f; break; }
            }
            if (targetField != null) break;
        }
        if (targetField == null) return new { error = $"Field '{fieldName}' not found" };

        var ft = targetField.GetType();
        if (ft == null) return new { error = "Field type is null" };
        if (!ft.IsValueType()) return new { error = "Can only write value-type fields" };

        var valueTypeName = p.TryGetProperty("valueType", out var vtProp) ? vtProp.GetString() : null;
        if (string.IsNullOrEmpty(valueTypeName))
            valueTypeName = ft.IsEnum() ? ft.GetUnderlyingType().GetFullName() : ft.GetFullName();

        var boxedValue = ParseValueFromJson(p.GetProperty("value"), valueTypeName);
        if (boxedValue == null) return new { error = $"Could not parse value as '{valueTypeName}'" };

        targetField.SetDataBoxed(obj.GetAddress(), boxedValue, false);
        return new { ok = true, field = fieldName, value = boxedValue.ToString() };
    }
}
