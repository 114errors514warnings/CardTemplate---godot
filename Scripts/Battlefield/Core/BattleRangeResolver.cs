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
		}

		return affected;
	}

	/// <summary>从 6 个邻向中选与 (target-origin) 最接近的方向。</summary>
	public static AxialHex PickLineDirection(AxialHex origin, AxialHex target)
	{
		int dq = target.Q - origin.Q;
		int dr = target.R - origin.R;
		AxialHex best = SixNeighborOffsets[0];
		double bestDot = double.MinValue;
		foreach (AxialHex dir in SixNeighborOffsets)
		{
			double dot = dir.Q * dq + dir.R * dr;
			if (dot > bestDot)
			{
				bestDot = dot;
				best = dir;
			}
		}

		return best;
	}
}
