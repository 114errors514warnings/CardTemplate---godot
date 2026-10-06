using System;
using System.Collections.Generic;
using System.Globalization;

public sealed class GameVariables
{
    public int DefaultEnergyPerTurn { get; private set; } = 3;
    public int DefaultDrawCardsPerTurn { get; private set; } = 5;

    /// <summary>
    /// 世界地图「移动到相邻节点」的时间点进程（`DataBase/GameVariables.csv` 的 `MoveTimePointCost` 列；
    /// 2026-10-05 用户口径：数值放全局数据表，便于调整）。表里留空 = `null`，消费方回落到
    /// `RunTimePoints.MoveCost`（默认 0.3）。消费点：`MapScene.EnterNode` 的移动闸门与支付。
    /// </summary>
    public float? MoveTimePointCost { get; private set; }

    /// <summary>
    /// 村庄设施「**每次操作**」的时间点代价（`DataBase/GameVariables.csv` 的 `VillageOperationTimePointCost` 列；
    /// 2026-10-05 用户口径 0.1：每次操作固定消耗少量时间点）。表里留空 = `null`，消费方
    /// （`RunFacilityCosts.Apply`）回落到 `RunFacilityCosts.DefaultOperationCost`。
    /// </summary>
    public float? VillageOperationTimePointCost { get; private set; }

    /// <summary>
    /// 树林「**单次搜寻**」的时间点代价（`ForestForageTimePointCost` 列；与村庄操作**分开配** ——
    /// 用户口径「树林里搜索的耗时与村庄其他操作不同」）。留空 = `null` → 回落
    /// `RunFacilityCosts.DefaultForestForageCost`。
    /// </summary>
    public float? ForestForageTimePointCost { get; private set; }

    private readonly Dictionary<int, int> movesPerTurn = new();

    public int GetMovesPerTurn(int characterId) => movesPerTurn.TryGetValue(characterId, out int value) ? value : 0;

    public static GameVariables Load()
    {
        string[] lines = LoadCsv.LoadCSVDataLines(LoadingSystem.GetFilePathByKey("Data.Game.Variables"));
        var result = new GameVariables();
        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            string[] fields = LoadCsv.ParseCSVFields(line);
            if (fields.Length == 0 || string.Equals(fields[0], "Scope", StringComparison.OrdinalIgnoreCase)) continue;
            string scope = fields[0].Trim();
            if (string.Equals(scope, "Global", StringComparison.OrdinalIgnoreCase))
            {
                if (fields.Length < 4 || !int.TryParse(fields[2], out int energy) || !int.TryParse(fields[3], out int draw) || energy < 0 || draw < 0)
                    throw new FormatException($"游戏变量全局行无效：{line}");
                result.DefaultEnergyPerTurn = energy; result.DefaultDrawCardsPerTurn = draw;
                result.MoveTimePointCost = ParseCost(fields, 5, "MoveTimePointCost", line);
                result.VillageOperationTimePointCost = ParseCost(fields, 6, "VillageOperationTimePointCost", line);
                result.ForestForageTimePointCost = ParseCost(fields, 7, "ForestForageTimePointCost", line);
            }
            else if (string.Equals(scope, "Character", StringComparison.OrdinalIgnoreCase))
            {
                if (fields.Length < 5 || !int.TryParse(fields[1], out int id) || !int.TryParse(fields[4], out int moves) || moves < 0)
                    throw new FormatException($"游戏变量角色行无效：{line}");
                result.movesPerTurn[id] = moves;
            }
            else throw new FormatException($"游戏变量 Scope 无效：{line}");
        }
        return result;
    }

    /// <summary>
    /// 把表里的**地点设施操作代价**灌进纯逻辑层 `RunFacilityCosts`（第 7 / 8 列）——
    /// 地点场景开启时调一次（当前调用点：`VillageScene._Ready`；村庄 / 商人共用的 `SmithyUi` 也从这里取值，
    /// 商人场景落地时需同样调用一次）。留空 = 保持兜底默认值（与移动代价「留空 = 回落默认」同口径）。
    /// </summary>
    public void ApplyFacilityCosts() =>
        RunFacilityCosts.Apply(VillageOperationTimePointCost, ForestForageTimePointCost);

    /// <summary>
    /// 解析全局行的第 N 列时间点代价（`index` 从 0 起）：
    /// 列不存在 / 留空 → 返回 null（消费方回落各自的默认值；移动 = `RunTimePoints.MoveCost` 0.3、
    /// 村庄操作 = `RunFacilityCosts.DefaultOperationCost`、树林搜寻 = `RunFacilityCosts.DefaultForestForageCost`）。
    /// 填了就必须是 &gt; 0 的有限数（口径 0.1 步长，见配表规范 §全局变量），否则整表判为坏表（不静默降级）。
    /// </summary>
    private static float? ParseCost(string[] fields, int index, string name, string line)
    {
        if (fields.Length <= index) return null;
        string text = fields[index].Trim();
        if (text.Length == 0) return null;
        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
            || float.IsNaN(value) || float.IsInfinity(value) || value <= 0f)
            throw new FormatException($"游戏变量 {name} 无效（需要 > 0 的数）：{line}");
        return value;
    }
}
