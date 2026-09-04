// MapGeometryTests.cs
// 覆盖六边形版图生成的几何约束（127 格 / 每边 7 / 固定特殊点 / Boss 可达）。
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

public class MapGeometryTests
{
    [Fact]
    public void Generate_Radius6_Has127Nodes_AndPerSide7()
    {
        HexBoardData board = MapGeometry.Generate(6, seed: 20260904);
        Assert.Equal(127, board.Nodes.Count);
        Assert.Equal(HexBoardData.DefaultRadius, board.Radius);

        // 每条外边（沿 q 轴的两条对边之一）含 7 格（含两角）
        int qMaxCount = board.Nodes.Count(n => n.Position.Q == 6);
        Assert.Equal(7, qMaxCount);
        int qMinCount = board.Nodes.Count(n => n.Position.Q == -6);
        Assert.Equal(7, qMinCount);
    }

    [Fact]
    public void Generate_FixedSpecialNodes_RespectNumericConstraints()
    {
        HexBoardData board = MapGeometry.Generate(6, seed: 1);

        MapBoardNode start = board.GetNode(board.StartNodeId);
        MapBoardNode boss = board.GetNode(board.BossNodeId);
        MapBoardNode village = board.GetNode(board.VillageNodeId);
        MapBoardNode eliteMid = board.GetNode(board.EliteMidNodeId);
        MapBoardNode eliteUp = board.GetNode(board.EliteUpNodeId);
        MapBoardNode eliteDown = board.GetNode(board.EliteDownNodeId);

        Assert.NotNull(start); Assert.NotNull(boss); Assert.NotNull(village);
        Assert.NotNull(eliteMid); Assert.NotNull(eliteUp); Assert.NotNull(eliteDown);

        // 起点/村庄/Boss 共线且各距村庄 6 步
        Assert.Equal(0, village.Position.Q);
        Assert.Equal(0, village.Position.R);
        Assert.Equal(6, AxialHex.Distance(village.Position, boss.Position));
        Assert.Equal(6, AxialHex.Distance(village.Position, start.Position));

        // 精英① 为村庄-Boss 连线中点（距村 3 步）
        Assert.Equal(3, AxialHex.Distance(village.Position, eliteMid.Position));

        // 精英②③ 距村 6 步
        Assert.Equal(6, AxialHex.Distance(village.Position, eliteUp.Position));
        Assert.Equal(6, AxialHex.Distance(village.Position, eliteDown.Position));

        // 六个固定格坐标互不重复
        HashSet<AxialHex> positions = new HashSet<AxialHex>
        {
            start.Position, boss.Position, village.Position,
            eliteMid.Position, eliteUp.Position, eliteDown.Position,
        };
        Assert.Equal(6, positions.Count);
    }

    [Fact]
    public void Generate_Adjacency_IsSymmetricHexNeighbors_AndAllSpecialNodesReachable()
    {
        HexBoardData board = MapGeometry.Generate(6, seed: 1234);

        // 邻接关系：互为相邻、且几何上确为六边形相邻（距离 = 1）
        foreach (MapBoardNode node in board.Nodes)
        {
            foreach (int nextId in node.NextIds)
            {
                MapBoardNode next = board.GetNode(nextId);
                Assert.NotNull(next);
                Assert.Equal(1, AxialHex.Distance(node.Position, next.Position));
                Assert.Contains(node.NodeId, next.NextIds);
            }
        }

        // 全相邻连接 → 起点可达全部特殊节点（含右上/右下精英）
        HashSet<int> reachable = MapGeometry.CollectReachableNodeIds(board, board.StartNodeId);
        Assert.Equal(board.Nodes.Count, reachable.Count);
        Assert.Contains(board.VillageNodeId, reachable);
        Assert.Contains(board.EliteMidNodeId, reachable);
        Assert.Contains(board.EliteUpNodeId, reachable);
        Assert.Contains(board.EliteDownNodeId, reachable);
        Assert.Contains(board.BossNodeId, reachable);
    }

    [Fact]
    public void Generate_DeterministicSeed_ProducesSameBoard()
    {
        HexBoardData a = MapGeometry.Generate(6, seed: 77);
        HexBoardData b = MapGeometry.Generate(6, seed: 77);
        Assert.Equal(a.Nodes.Count, b.Nodes.Count);
        for (int i = 0; i < a.Nodes.Count; i++)
        {
            Assert.Equal(a.Nodes[i].Type, b.Nodes[i].Type);
            Assert.Equal(a.Nodes[i].Position, b.Nodes[i].Position);
            Assert.Equal(a.Nodes[i].NextIds, b.Nodes[i].NextIds);
        }
    }
}
