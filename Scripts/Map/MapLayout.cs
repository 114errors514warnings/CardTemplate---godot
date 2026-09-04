// MapLayout.cs
// 地图“形式”纯逻辑布局助手（不依赖 Godot，便于单测）。
//
// 依据《地图玩法.md》与用户规格：
//  - 六边形边长 = 外接圆半径 R（flat-top，东向存在顶点）；
//  - 相邻两节点中心间距 = 3R：中心(0,0) → 东顶点(1,0) → 邻格西顶点(2,0) → 邻格中心(3,0)；
//  - 相邻节点相对顶点间空隙宽度 = R，连接线段长度 = 边长 = R，方向与中心连线平行。
//  - 轴向到屏幕：P(q,r) = 3R * (q*u0 + r*u60)，u0=(1,0)，u60=(0.5,√3/2)。
using System;

public static class MapLayout
{
	public const double SpacingFactor = 3.0;

	public static (double X, double Y) CenterOf(int q, int r, double radius)
	{
		double x = SpacingFactor * radius * (q + r * 0.5);
		double y = SpacingFactor * radius * r * (Math.Sqrt(3.0) / 2.0);
		return (x, y);
	}

	/// <summary>六边形第 k（0..5）个顶点的局部偏移（0° 起算，60° 步进，东向为顶点）。</summary>
	public static (double X, double Y) VertexLocal(int k, double radius)
	{
		double angle = Math.PI / 180.0 * (60 * k);
		return (Math.Cos(angle) * radius, Math.Sin(angle) * radius);
	}

	/// <summary>相邻两格中心间距。</summary>
	public static double CenterDistance(double radius) => SpacingFactor * radius;

	/// <summary>连接线段长度（= 边长 = R）。</summary>
	public static double ConnectorLength(double radius) => radius;

	/// <summary>
	/// 一条无向边 (a→b) 的连接线段两端点（填在两格相对顶点之间，长度 = 边长）。
	/// 返回 (aR.x,aR.y,bL.x,bL.y)。公式：a + R*e → b − R*e，e = (P_b − P_a)/(3R)。
	/// </summary>
	public static (double Ax, double Ay, double Bx, double By) ConnectorEndpoints(
		(int Q, int R) a, (int Q, int R) b, double radius)
	{
		(double ax, double ay) = CenterOf(a.Q, a.R, radius);
		(double bx, double by) = CenterOf(b.Q, b.R, radius);

		double dx = bx - ax;
		double dy = by - ay;
		double dist = Math.Sqrt(dx * dx + dy * dy);
		if (dist < 1e-9)
		{
			return (ax, ay, bx, by);
		}

		double ex = dx / dist;
		double ey = dy / dist;
		return (
			ax + ex * radius, ay + ey * radius,
			bx - ex * radius, by - ey * radius);
	}

	/// <summary>
	/// 根据视口与版图范围反推合适的外接圆半径（留出 margin 与外圈余量）。
	/// xNorm = |q + r/2| 上限；yNorm = |r| 上限（y = 3R·r·√3/2）。
	/// </summary>
	public static double FitRadius(int viewWidth, int viewHeight, double margin, double xNormMax, double yNormMax)
	{
		double safeX = viewWidth - 2 * margin;
		double safeY = viewHeight - 2 * margin;
		if (safeX <= 0 || safeY <= 0)
		{
			return 8.0;
		}

		double rx = xNormMax > 0 ? (0.98 * safeX) / (2 * SpacingFactor * xNormMax) : safeX;
		double ry = yNormMax > 0 ? (0.98 * safeY) / (2 * SpacingFactor * yNormMax * (Math.Sqrt(3.0) / 2.0)) : safeY;
		return Math.Max(6.0, Math.Min(rx, ry));
	}
}
