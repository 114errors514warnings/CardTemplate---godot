using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CardSimulator.Battlefield;

public sealed record BattleLevelObject(string InstanceId, string ObjectType, string DefinitionId, int Q, int R)
{
    /// <summary>关卡 CSV 的 `InitialValue` 列（初始生命 / 初始状态等）；语法与应用见 <see cref="UnitInitialStateConfig"/>。</summary>
    public string InitialValue { get; init; } = string.Empty;
}
public sealed class BattleLevelConfig
{
    public string LevelId = "";
    public string MapId = "";
    public int DropTableId;
    public string LevelType = "";
    public string Difficulty = "";
    public List<BattleLevelObject> Objects = new();
}

public static class BattleLevelCatalog
{
    private const string LevelIndexPath = "res://DataBase/Level/LevelIndex.csv";
    private const string MapIndexPath = "res://DataBase/BattleMap/MapIndex.csv";

    public static BattleLevelConfig Load(string levelId)
    {
        string configPath = FindPath(LevelIndexPath, levelId);
        if (string.IsNullOrWhiteSpace(configPath)) throw new ArgumentException($"关卡不存在：{levelId}");
        return Parse(levelId, LoadCsv.LoadCSVDataLines(configPath));
    }

    /// <summary>解析单关卡 CSV 行（纯函数，便于单测）：每行一个对象；第 11 列 `InitialValue` 记录该对象的初始生命/层数。</summary>
    public static BattleLevelConfig Parse(string levelId, IEnumerable<string> csvLines)
    {
        var config = new BattleLevelConfig { LevelId = levelId };
        foreach (string line in csvLines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            string[] f = LoadCsv.ParseCSVFields(line);
            if (f.Length < 12) throw new ArgumentException($"关卡配置行无效：{line}");
            if (!int.TryParse(f[1], out int drop) || !int.TryParse(f[7], out int q) || !int.TryParse(f[8], out int r))
                throw new ArgumentException($"关卡配置数值无效：{line}");
            if (string.IsNullOrEmpty(config.MapId)) { config.MapId = f[0]; config.DropTableId = drop; config.LevelType = f[2]; config.Difficulty = f[3]; }
            else if (config.MapId != f[0] || config.DropTableId != drop || config.LevelType != f[2] || config.Difficulty != f[3])
                throw new ArgumentException($"关卡配置的地图、掉落、类型或难度不一致：{levelId}");
            config.Objects.Add(new BattleLevelObject(f[4], f[5], f[6], q, r) { InitialValue = f[10].Trim() });
        }
        if (string.IsNullOrEmpty(config.MapId)) throw new ArgumentException($"关卡没有对象配置：{levelId}");
        return config;
    }

    /// <summary>
    /// 把关卡的怪物行写入地图定义：怪物 ID、固定出生点与**逐只初始值**（关卡 CSV 的 `InitialValue`）。
    /// 战斗场景与烟测共用同一入口，避免两处各写一套映射。
    /// </summary>
    public static void ApplyMonstersTo(BattleMapDefinition definition, BattleLevelConfig level)
    {
        if (definition == null || level == null) return;
        definition.MonsterIds = new List<int>();
        definition.FixedEnemySpawnCoords = new List<HexCoordinateData>();
        definition.MonsterInitialValues = new List<string>();
        definition.MonsterInstanceIds = new List<string>();
        int index = 0;
        foreach (BattleLevelObject monster in level.Objects.Where(x => x.ObjectType == "Monster"))
        {
            if (!int.TryParse(monster.DefinitionId, out int monsterId) || monsterId <= 0)
                throw new ArgumentException($"关卡 {level.LevelId} 的怪物 DefinitionId 必须是数字 ID：{monster.DefinitionId}");
            index++;
            definition.MonsterIds.Add(monsterId);
            definition.FixedEnemySpawnCoords.Add(new HexCoordinateData { Q = monster.Q, R = monster.R });
            definition.MonsterInitialValues.Add(monster.InitialValue ?? string.Empty);
            // 实例键优先用关卡 CSV 的 InstanceId（稳定、可读）；缺失时退回 `<关卡Id>#<序号>`。
            definition.MonsterInstanceIds.Add(string.IsNullOrWhiteSpace(monster.InstanceId)
                ? $"{level.LevelId}#{index}"
                : monster.InstanceId.Trim());
        }
    }

    public static string ResolveMapPath(string mapId) => FindPath(MapIndexPath, mapId);

    private static string FindPath(string indexPath, string id)
    {
        foreach (string line in LoadCsv.LoadCSVDataLines(indexPath))
        {
            string[] f = LoadCsv.ParseCSVFields(line);
            if (f.Length >= 2 && string.Equals(f[0], id, StringComparison.OrdinalIgnoreCase)) return f[1];
        }
        return "";
    }
}
