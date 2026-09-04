// MapLayoutTests.cs
// 覆盖 P0#9 地图“形式”几何（用户规格）：中心间距 3R、连接线段长=边长、方向平行中心连线。
using System;
using Xunit;

public class MapLayoutTests
{
    private const double Eps = 1e-6;

    [Fact]
    public void CenterOf_UserExample_RightNeighborAt3R()
    {
        (double x, double y) = MapLayout.CenterOf(0, 0, 1.0);
        Assert.Equal(0, x, 6);
        Assert.Equal(0, y, 6);

        // (1,0) 为东侧相邻节点中心，应位于 (3R, 0)
        (double x2, double y2) = MapLayout.CenterOf(1, 0, 1.0);
        Assert.Equal(3.0, x2, 6);
        Assert.Equal(0.0, y2, 6);
    }

    [Fact]
    public void CenterOf_VertexDirections_Are120DegreeNeighbors()
    {
        // (0,1) → 60°，(1,-1) → −60°，与 (1,0)（0°）两两夹角 60°
        (double x1, double y1) = MapLayout.CenterOf(1, 0, 1.0);
        (double x2, double y2) = MapLayout.CenterOf(0, 1, 1.0);
        double dot1 = x1 * x2 + y1 * y2; // 3*1.5 = 4.5 → cos60
        Assert.Equal(Math.Cos(Math.PI / 3) * 9.0, dot1, 6);

        (double x3, double y3) = MapLayout.CenterOf(1, -1, 1.0);
        double dot2 = x1 * x3 + y1 * y3;
        Assert.Equal(Math.Cos(Math.PI / 3) * 9.0, dot2, 6);
    }

    [Fact]
    public void Connector_LengthEqualsSideLength_AndParallelToCenters()
    {
        (double ax, double ay, double bx, double by) =
            MapLayout.ConnectorEndpoints((0, 0), (1, 0), 1.0);

        // 端点应为 (1,0)→(2,0)，线段水平且长度 = 1 = 边长
        Assert.Equal(1.0, ax, 6);
        Assert.Equal(0.0, ay, 6);
        Assert.Equal(2.0, bx, 6);
        Assert.Equal(0.0, by, 6);

        double len = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
        Assert.Equal(1.0, len, 6);
    }

    [Fact]
    public void Hexagons_DoNotOverlap_AllNodeCentersApartAtLeast2R()
    {
        HexBoardData board = MapGeometry.Generate(6, seed: 20260904);
        double radius = 1.0;
        double minDist = double.MaxValue;
        for (int i = 0; i < board.Nodes.Count; i++)
        {
            for (int j = i + 1; j < board.Nodes.Count; j++)
            {
                (double xi, double yi) = MapLayout.CenterOf(board.Nodes[i].Position.Q, board.Nodes[i].Position.R, radius);
                (double xj, double yj) = MapLayout.CenterOf(board.Nodes[j].Position.Q, board.Nodes[j].Position.R, radius);
                double dx = xi - xj;
                double dy = yi - yj;
                minDist = Math.Min(minDist, Math.Sqrt(dx * dx + dy * dy));
            }
        }

        Assert.True(minDist >= 2.5 * radius, $"非相邻格过近/重叠：最小中心距 {minDist}");
    }

    [Fact]
    public void FitRadius_PositiveAndWithinViewport()
    {
        double r = MapLayout.FitRadius(1600, 900, 110, 6.0, 6.0);
        Assert.True(r > 0);

        // 用该半径重算最大外接范围应落在视口安全区内
        double halfW = 3.0 * r * 6.0;
        double halfH = 3.0 * r * 6.0 * Math.Sqrt(3.0) / 2.0;
        Assert.True(2 * halfW + 220 < 1600);
        Assert.True(2 * halfH + 220 < 900);
    }
}
