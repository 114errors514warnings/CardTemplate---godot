// BattleRangeResolver.cs
// 战场卡牌的空间形状解析（纯逻辑）：Single / Burst / Line 的候选目标与受影响格。
// SelfMove 走移动路线；Trap 只落点，不参与“受影响单位”集合。
using System;
using System.Collections.Generic;

namespace CardSimulator.Battlefield;

public static class BattleRangeResolver
{
	public static readonly AxialHex[] SixNeighborOffsets =
	{
		new AxialHex(1, 0),
		new AxialHex(1, -1),
		new AxialHex(0, -1),
		new AxialHex(-1, 0),
		new AxialHex(-1, 1),
		new AxialHex(0, 1),
	};

	public static IEnumerable<AxialHex> Neighbors(AxialHex cell)
	{
		foreach (AxialHex offset in SixNeighborOffsets)
		{
			yield return new AxialHex(cell.Q + offset.Q, cell.R + offset.R);
		}
	}

	public static int Distance(AxialHex a, AxialHex b)
	{
		int dq = Math.Abs(a.Q - b.Q);
		int dr = Math.Abs(a.R - b.R);
		int ds = Math.Abs((a.Q + a.R) - (b.Q + b.R));
		return Math.Max(dq, Math.Max(dr, ds));
	}

	/// <summary>以 origin 为中心、范围内（图距离 ≤ maxRange）的全部格（不校验地图边界）。</summary>
	public static HashSet<AxialHex> CellsWithinRange(AxialHex origin, int maxRange)
	{
		HashSet<AxialHex> result = new HashSet<AxialHex> { origin };
		if (maxRange <= 0)
		{
			return result;
		}

		for (int dq = -maxRange; dq <= maxRange; dq++)
		{
			for (int dr = -maxRange; dr <= maxRange; dr++)
			{
				AxialHex hex = new AxialHex(origin.Q + dq, origin.R + dr);
				if (Distance(origin, hex) <= maxRange)
				{
					result.Add(hex);
				}
			}
		}

		return result;
	}

	/// <summary>
	/// 给定已选落点 target，求“会受到影响的格集合”。
	/// Burst/Line 的 Length/Radius 从规格读取；Single 即落点本身。
	/// </summary>
	public static HashSet<AxialHex> ResolveAffectedCells(AxialHex origin, AxialHex target, CardSpatialSpec spec)
	{
		HashSet<AxialHex> affected = new HashSet<AxialHex> { target };
		if (spec == null || spec.Shape == CardSpatialShape.Single || spec.Shape == CardSpatialShape.None)
		{
			return affected;
		}

		switch (spec.Shape)
		{
			case CardSpatialShape.Burst:
				foreach (AxialHex cell in CellsWithinRange(target, spec.Radius))
				{
					affected.Add(cell);
				}

				break;

			case CardSpatialShape.Line:
			{
				AxialHex dir = PickLineDirection(origin, target);
				for (int i = 1; i <= spec.Length; i++)
				{
					affected.Add(new AxialHex(origin.Q + dir.Q * i, origin.R + dir.R * i));
				}

				break;
			}

			case CardSpatialShape.Fan:
			{
				AxialHex dir = PickLineDirection(origin, target);
				foreach (AxialHex cell in ResolveFanCells(origin, dir, Math.Max(1, spec.MaxRange))) affected.Add(cell);
				break;
			}
		}

		return affected;
	}

	/// <summary>Two 120-degree boundary fan: O + a*left + b*right, excluding O.</summary>
	public static HashSet<AxialHex> ResolveFanCells(AxialHex origin, AxialHex direction, int range)
	{
		var result = new HashSet<AxialHex>();
		int index = IndexOfDirection(direction);
		if (index < 0 || range <= 0) return result;
		AxialHex left = SixNeighborOffsets[(index + 1) % SixNeighborOffsets.Length];
		AxialHex right = SixNeighborOffsets[(index + SixNeighborOffsets.Length - 1) % SixNeighborOffsets.Length];
		for (int a = 0; a <= range; a++)
		for (int b = 0; b <= range; b++)
		{
			if (a == 0 && b == 0) continue;
			result.Add(new AxialHex(origin.Q + a * left.Q + b * right.Q, origin.R + a * left.R + b * right.R));
		}
		return result;
	}

	/// <summary>从 6 个邻向中选与 (target-origin) 最接近的方向。</summary>
	public static AxialHex PickLineDirection(AxialHex origin, AxialHex target)
	{
		int dq = target.Q - origin.Q;
		int dr = target.R - origin.R;
		if (dq == 0 && dr == 0)
		{
			return SixNeighborOffsets[0];
		}

		// 轴向 q/r 不是正交二维坐标。必须在实际 pointy-top 六边形中心坐标中比较夹角，
		// 否则鼠标指向屏幕纵向两组邻格时会被错误归到斜向格。
		double targetX = Math.Sqrt(3d) * (dq + dr * 0.5d);
		double targetY = 1.5d * dr;
		double targetLength = Math.Sqrt(targetX * targetX + targetY * targetY);
		AxialHex best = SixNeighborOffsets[0];
		double bestDot = double.MinValue;
		foreach (AxialHex dir in SixNeighborOffsets)
		{
			double directionX = Math.Sqrt(3d) * (dir.Q + dir.R * 0.5d);
			double directionY = 1.5d * dir.R;
			double directionLength = Math.Sqrt(directionX * directionX + directionY * directionY);
			double dot = (directionX * targetX + directionY * targetY) / (directionLength * targetLength);
			if (dot > bestDot)
			{
				bestDot = dot;
				best = dir;
			}
		}

		return best;
	}

	/// <summary>Returns a direction only when target lies on one of the six centre-to-centre hex rays.</summary>
	public static bool TryGetExactLineDirection(AxialHex origin, AxialHex target, out AxialHex direction)
	{
		int dq = target.Q - origin.Q, dr = target.R - origin.R;
		foreach (AxialHex candidate in SixNeighborOffsets)
		{
			int steps;
			if (candidate.Q != 0)
			{
				if (dq % candidate.Q != 0) continue;
				steps = dq / candidate.Q;
				if (candidate.R * steps != dr) continue;
			}
			else
			{
				if (dq != 0 || dr % candidate.R != 0) continue;
				steps = dr / candidate.R;
			}
			if (steps > 0) { direction = candidate; return true; }
		}
		direction = default;
		return false;
	}

	/// <summary>
	/// 投掷型道具：给定已选落点 target，求会受到影响的格集合。
	/// 全部以 target 为落点中心；方向由 origin→target 决定（直线/扇形/环形）。
	/// </summary>
	public static HashSet<AxialHex> ResolveItemAffectedCells(AxialHex origin, AxialHex target, GroundObject item)
	{
		HashSet<AxialHex> affected = new HashSet<AxialHex> { target };
		if (item == null || item.SpatialShape == ItemSpatialShape.None || item.SpatialShape == ItemSpatialShape.Single)
		{
			return affected;
		}

		int radius = Math.Max(1, item.ItemRadius);
		int length = Math.Max(1, item.ItemLength);
		AxialHex dir = PickLineDirection(origin, target);
		switch (item.SpatialShape)
		{
			case ItemSpatialShape.Line:
				// 直线：从施法者 origin 沿投掷方向向前 length 格（投掷型“固定距离直线”）。
				for (int i = 1; i <= length; i++)
				{
					affected.Add(new AxialHex(origin.Q + dir.Q * i, origin.R + dir.R * i));
				}

				break;

			case ItemSpatialShape.Fan:
				// 扇形：以 origin 为顶点、朝投掷方向 ±60° 的楔形，半径 radius。
				foreach (AxialHex cell in CellsWithinRange(origin, radius))
				{
					if (cell == origin)
					{
						continue;
					}

					if (IsSameOrAdjacentDirection(dir, PickLineDirection(origin, cell)))
					{
						affected.Add(cell);
					}
				}

				break;

			case ItemSpatialShape.Ring:
				// 环形：以落点 target 为圆心、半径为 radius 的一圈格。
				foreach (AxialHex cell in CellsWithinRange(target, radius))
				{
					if (Distance(target, cell) == radius)
					{
						affected.Add(cell);
					}
				}

				break;

			case ItemSpatialShape.Diamond:
				// 菱形（较短中线与使用方向一致）：以落点为中心、沿投掷方向为“短对角线”。
				foreach (AxialHex perp in SixNeighborOffsets)
				{
					if (perp == dir || IsSameOrAdjacentDirection(dir, perp)) continue;
					for (int i = -1; i <= radius; i++)
					{
						affected.Add(new AxialHex(target.Q + dir.Q * i + perp.Q, target.R + dir.R * i + perp.R));
					}
				}

				for (int i = -1; i <= radius; i++)
				{
					affected.Add(new AxialHex(target.Q + dir.Q * i, target.R + dir.R * i));
				}

				break;
		}

		return affected;
	}

	/// <summary>两个方向是否相同或相差 60°（相邻邻向）。</summary>
	public static bool IsSameOrAdjacentDirection(AxialHex a, AxialHex b)
	{
		if (a == b)
		{
			return true;
		}

		int ia = IndexOfDirection(a);
		int ib = IndexOfDirection(b);
		if (ia < 0 || ib < 0)
		{
			return false;
		}

		int diff = Math.Abs(ia - ib);
		return diff == 1 || diff == 5;
	}

	private static int IndexOfDirection(AxialHex d)
	{
		for (int i = 0; i < SixNeighborOffsets.Length; i++)
		{
			if (SixNeighborOffsets[i] == d)
			{
				return i;
			}
		}

		return -1;
	}
}
