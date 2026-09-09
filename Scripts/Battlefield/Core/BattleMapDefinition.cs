using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CardSimulator.Battlefield;

public sealed class HexCoordinateData
{
    public int Q { get; set; }
    public int R { get; set; }
    public AxialHex ToHex() => new(Q, R);
}

public sealed class BattleCellData
{
    public int Q { get; set; }
    public int R { get; set; }
    public BattleCellKind Kind { get; set; }
    public BattleSurface Surface { get; set; }
    public bool BlocksSight { get; set; }
}

public sealed class BattleObjectData
{
    public int Q { get; set; }
    public int R { get; set; }
    public string DefinitionId { get; set; } = "";
    public GroundObjectKind Kind { get; set; }
    public EntryTriggerMode TriggerMode { get; set; }
    public int HandsRequired { get; set; } = 1;
    public int AttackRange { get; set; } = 1;
    public int MoveBonus { get; set; }
    public int HealAmount { get; set; }
    // ── 投掷型道具（需选目标）的空间规格 ──
    public ItemSpatialShape SpatialShape { get; set; } = ItemSpatialShape.None;
    public int ItemMaxRange { get; set; } = 1;
    public int ItemRadius { get; set; } = 1;
    public int ItemLength { get; set; } = 1;
    public string ItemTrapId { get; set; } = "";
    public int DamageAmount { get; set; }
}

public sealed class BattleMapDefinition
{
    public string MapId { get; set; } = "";
    public int Version { get; set; } = 1;
    public int Seed { get; set; } = 60908;
    public double CellRadius { get; set; } = 42;
    public string Shape { get; set; } = "Hexagon";
    public int Radius { get; set; } = 8;
    public List<BattleCellData> Cells { get; set; } = new();
    public List<BattleCellData> CellOverrides { get; set; } = new();
    public List<HexCoordinateData> PlayerSpawnCoords { get; set; } = new();
    public List<HexCoordinateData> ExitAnchors { get; set; } = new();
    public List<HexCoordinateData> GenerationExcludedCoords { get; set; } = new();
    public List<BattleObjectData> ObjectPlacements { get; set; } = new();
    public int RandomObstacleCount { get; set; }
    public int RandomItemCount { get; set; }
    public List<string> RandomItemDefinitions { get; set; } = new();
    public int MinEnemyDistance { get; set; }
    public int MaxGenerationAttempts { get; set; } = 32;
    public List<int> PlayerCharacterIds { get; set; } = new();
    public List<int> MonsterIds { get; set; } = new();

    public static BattleMapDefinition Parse(string json)
    {
        var options = new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        var data = JsonSerializer.Deserialize<BattleMapDefinition>(json, options)
            ?? throw new ArgumentException("地图 JSON 为空。");
        data.Validate(); return data;
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(MapId) || Version < 1) throw new ArgumentException("地图 ID/版本无效。");
        if (Shape != "Hexagon" && Shape != "Explicit") throw new ArgumentException("Shape 必须是 Hexagon 或 Explicit。");
        if (!double.IsFinite(CellRadius) || CellRadius < 16 || CellRadius > 120) throw new ArgumentException("格半径应在 16–120 范围内。");
        if (Radius < 1 || Radius > 64) throw new ArgumentException("地图半径应在 1–64 范围内。");
        if (Cells == null || CellOverrides == null || PlayerSpawnCoords == null || ExitAnchors == null ||
            GenerationExcludedCoords == null || ObjectPlacements == null || RandomItemDefinitions == null ||
            PlayerCharacterIds == null || MonsterIds == null) throw new ArgumentException("地图集合字段不能为 null。");
        if (PlayerSpawnCoords.Count != 3 || PlayerCharacterIds.Count != 3) throw new ArgumentException("必须配置三个玩家槽与出生格。");
        if (Cells.Count > 20000 || CellOverrides.Count > 20000 || ObjectPlacements.Count > 20000 || MonsterIds.Count > 1000)
            throw new ArgumentException("地图配置超过支持容量。");
        if (RandomObstacleCount < 0 || RandomItemCount < 0 || RandomItemCount > 20000 || MinEnemyDistance < 0 ||
            MaxGenerationAttempts < 1 || MaxGenerationAttempts > 100) throw new ArgumentException("随机生成数量或约束无效。");
        if (Shape == "Explicit" && (Cells.Count == 0 || ExitAnchors.Count == 0)) throw new ArgumentException("Explicit 地图必须配置 Cells 和 ExitAnchors。");
        if (RandomItemCount > 0 && (RandomItemDefinitions.Count == 0 || RandomItemDefinitions.Exists(string.IsNullOrWhiteSpace)))
            throw new ArgumentException("随机道具池为空或包含空 ID。");
        for (int i = 0; i < 3; i++)
        {
            if (PlayerSpawnCoords[i] == null || PlayerCharacterIds[i] <= 0) throw new ArgumentException("玩家出生信息无效。");
            for (int j = 0; j < i; j++)
                if (AxialHex.Distance(PlayerSpawnCoords[i].ToHex(), PlayerSpawnCoords[j].ToHex()) != 1)
                    throw new ArgumentException("三个出生格必须两两相邻。");
        }
    }
}
