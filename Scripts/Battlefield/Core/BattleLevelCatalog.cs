using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CardSimulator.Battlefield;

public sealed record BattleLevelObject(string InstanceId, string ObjectType, string DefinitionId, int Q, int R);
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
        string[] lines = LoadCsv.LoadCSVDataLines(configPath);
        var config = new BattleLevelConfig { LevelId = levelId };
        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            string[] f = LoadCsv.ParseCSVFields(line);
            if (f.Length < 12) throw new ArgumentException($"关卡配置行无效：{line}");
            if (!int.TryParse(f[1], out int drop) || !int.TryParse(f[7], out int q) || !int.TryParse(f[8], out int r))
                throw new ArgumentException($"关卡配置数值无效：{line}");
            if (string.IsNullOrEmpty(config.MapId)) { config.MapId = f[0]; config.DropTableId = drop; config.LevelType = f[2]; config.Difficulty = f[3]; }
            else if (config.MapId != f[0] || config.DropTableId != drop || config.LevelType != f[2] || config.Difficulty != f[3])
                throw new ArgumentException($"关卡配置的地图、掉落、类型或难度不一致：{levelId}");
            config.Objects.Add(new BattleLevelObject(f[4], f[5], f[6], q, r));
        }
        if (string.IsNullOrEmpty(config.MapId)) throw new ArgumentException($"关卡没有对象配置：{levelId}");
        return config;
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
