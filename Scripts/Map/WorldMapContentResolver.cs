using System;
using System.Collections.Generic;
using System.Linq;

public sealed record ResolvedMapContent(string Type, string Id);

public static class WorldMapContentResolver
{
    private const string Root = "res://DataBase/WorldMap/";
    public static ResolvedMapContent Resolve(int act, MapBoardNode node, HexBoardData board, RunSaveData run)
    {
        if (node == null || run == null) return null;
        string key = node.NodeId == board.VillageNodeId ? "Village" : node.NodeId == board.EliteMidNodeId ? "EliteMid" : node.NodeId == board.EliteUpNodeId ? "EliteUp" : node.NodeId == board.EliteDownNodeId ? "EliteDown" : node.NodeId == board.BossNodeId ? "Boss" : "";
        if (!string.IsNullOrEmpty(key)) return PickFixed(act, key, node.Type, run.MapState.Seed + node.NodeId);
        if (node.Type == MapNodeType.NormalCombat)
        {
            string difficulty = ResolveDifficulty(act, run.MapState.NormalEncounterIndex + 1);
            return PickLevel(act, node.Type, difficulty, run.MapState.Seed + node.NodeId);
        }
        if (node.Type == MapNodeType.HighRiskCombat)
            return PickLevel(act, node.Type, "High", run.MapState.Seed + node.NodeId);
        return PickEvent(act, node.Type, run.MapState.Seed + node.NodeId);
    }
    private static string ResolveDifficulty(int act, int count)
    {
        foreach (var f in Rows("NormalCombatRule.csv")) if (int.Parse(f[0]) == act && count >= int.Parse(f[1]) && count <= int.Parse(f[2])) return f[3];
        return "";
    }
    private static ResolvedMapContent PickLevel(int act, MapNodeType type, string difficulty, int seed) => Pick(Rows("LevelPool.csv").Where(f => int.Parse(f[1]) == act && f[2] == type.ToString() && f[3] == difficulty).Select(f => ("Level", f[4], int.Parse(f[5]))), seed);
    private static ResolvedMapContent PickEvent(int act, MapNodeType type, int seed) => Pick(Rows("EventPool.csv").Where(f => int.Parse(f[0]) == act && f[1] == type.ToString() && string.IsNullOrEmpty(f[2])).Select(f => ("Event", f[3], int.Parse(f[4]))), seed);
    private static ResolvedMapContent PickFixed(int act, string key, MapNodeType type, int seed) => Pick(Rows("FixedNode.csv").Where(f => int.Parse(f[0]) == act && f[1] == key && f[2] == type.ToString()).Select(f => (f[3], f[4], int.Parse(f[5]))), seed);
    private static ResolvedMapContent Pick(IEnumerable<(string Type, string Id, int Weight)> rows, int seed)
    { var list = rows.ToList(); if (list.Count == 0) return null; int total = list.Sum(x => Math.Max(1, x.Weight)); int roll = new Random(seed).Next(total); foreach (var x in list) { roll -= Math.Max(1, x.Weight); if (roll < 0) return new ResolvedMapContent(x.Type, x.Id); } return new ResolvedMapContent(list[^1].Type, list[^1].Id); }
    private static IEnumerable<string[]> Rows(string file) => LoadCsv.LoadCSVDataLines(Root + file).Where(x => !string.IsNullOrWhiteSpace(x)).Select(LoadCsv.ParseCSVFields).Where(x => x.Length > 1);
}
