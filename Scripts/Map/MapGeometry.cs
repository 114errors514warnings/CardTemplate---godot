// MapGeometry.cs
// 大六边形版图生成（纯逻辑，可单测）。
//
// 几何口径（对齐《地图玩法数值设计.md》与 2026.08/P0#9 施工文档 §4.4）：
//  - 轴向坐标 (q,r)，半径 R=6 的环形版图：max(|q|,|r|,|q+r|) <= R
//  - 总格数 = 3*R*(R+1)+1 = 127；六条外边每边含 R+1=7 个格点（含两角）
//  - 起点(-R,0) → Boss(R,0) 与村庄(0,0) 共线，各距村庄 R=6 步；
//    精英① 取村庄与 Boss 连线中点 (R/2,0)（距村 3 步）；
//    精英②③ 取可到达的右上方/右下方角区节点（距村 6 步）。
//  - 前向边：每格向其 3 个"更接近 Boss(屏面右端)"的相邻方向连边
//    （pointy-top 东向三元组 (1,0)/(0,1)/(1,-1)），保证每步 x 增加且禁止回头。
//  - 边：与版图内全部相邻格点连边（≤6）；移动 = 前往任一相邻格（已访问格可再次经过，不重复结算）。
using System;
using System.Collections.Generic;

public static class MapGeometry
{
	private static readonly AxialHex[] AllNeighborOffsets =
	{
		new AxialHex(1, 0),
		new AxialHex(-1, 0),
		new AxialHex(0, 1),
		new AxialHex(0, -1),
		new AxialHex(1, -1),
		new AxialHex(-1, 1),
	};

	private const int WeightEmpty = 10;        // 空白
	private const int WeightNormalCombat = 40; // 普通敌袭
	private const int WeightHighRiskCombat = 12; // 高危敌袭
	private const int WeightNormalEvent = 25;  // 普通事件
	private const int WeightDangerousEvent = 13; // 危险事件

	public static bool IsInsideBoard(AxialHex hex, int radius)
	{
		return System.Math.Abs(hex.Q) <= radius
			&& System.Math.Abs(hex.R) <= radius
			&& System.Math.Abs(hex.Q + hex.R) <= radius;
	}

	/// <summary>生成一次版图：radius 默认 6（127 格）。seed 为空时使用随机种子。</summary>
	public static HexBoardData Generate(int radius = HexBoardData.DefaultRadius, int? seed = null)
	{
		if (radius < 1)
		{
			radius = HexBoardData.DefaultRadius;
		}

		Random random = seed.HasValue ? new Random(seed.Value) : new Random();

		HexBoardData board = new HexBoardData { Radius = radius };

		// 1) 摆放固定特殊格坐标
		Dictionary<AxialHex, MapNodeType> fixedTypes = new Dictionary<AxialHex, MapNodeType>
		{
			{ new AxialHex(-radius, 0), MapNodeType.Start },
			{ new AxialHex(radius, 0), MapNodeType.Boss },
			{ new AxialHex(0, 0), MapNodeType.Village },
			{ new AxialHex(radius / 2, 0), MapNodeType.Elite },
			{ new AxialHex(0, radius), MapNodeType.Elite },
			{ new AxialHex(radius, -radius), MapNodeType.Elite },
		};

		// 2) 生成全部格点坐标并排序（q 升序、r 升序），得到稳定 NodeId
		List<AxialHex> coords = new List<AxialHex>();
		for (int q = -radius; q <= radius; q++)
		{
			int rMin = Math.Max(-radius, -radius - q);
			int rMax = Math.Min(radius, radius - q);
			for (int r = rMin; r <= rMax; r++)
			{
				coords.Add(new AxialHex(q, r));
			}
		}

		coords.Sort((a, b) =>
		{
			int byQ = a.Q.CompareTo(b.Q);
			return byQ != 0 ? byQ : a.R.CompareTo(b.R);
		});

		// 3) 建节点：固定特殊点就位；其余按占比分配类型（先保证一个商人格）
		List<MapBoardNode> merchantCandidates = new List<MapBoardNode>();
		foreach (AxialHex hex in coords)
		{
			MapBoardNode node = new MapBoardNode
			{
				NodeId = board.Nodes.Count,
				Position = hex,
			};

			if (fixedTypes.TryGetValue(hex, out MapNodeType specialType))
			{
				node.Type = specialType;
				node.IsSpecial = true;
			}
			else
			{
				node.Type = RollRandomType(random);
				merchantCandidates.Add(node);
			}

			board.RegisterIndex(hex, node.NodeId);
			board.Nodes.Add(node);
		}

		// 至少保留一个商人格（占普通格一个名额）
		if (merchantCandidates.Count > 0)
		{
			merchantCandidates[random.Next(merchantCandidates.Count)].Type = MapNodeType.Merchant;
		}

		// 4) 记录特殊节点 id
		RecordFixedNodeIds(board);

		// 5) 邻接：每格与版图内全部相邻格连边（最多 6 个）
		foreach (MapBoardNode node in board.Nodes)
		{
			foreach (AxialHex offset in AllNeighborOffsets)
			{
				AxialHex target = new AxialHex(node.Position.Q + offset.Q, node.Position.R + offset.R);
				if (!IsInsideBoard(target, radius))
				{
					continue;
				}

				if (board.TryGetNodeId(target, out int targetId))
				{
					node.NextIds.Add(targetId);
				}
			}
		}

		return board;
	}

	private static void RecordFixedNodeIds(HexBoardData board)
	{
		int radius = board.Radius;
		board.StartNodeId = FindNode(board, new AxialHex(-radius, 0), MapNodeType.Start);
		board.BossNodeId = FindNode(board, new AxialHex(radius, 0), MapNodeType.Boss);
		board.VillageNodeId = FindNode(board, new AxialHex(0, 0), MapNodeType.Village);
		board.EliteMidNodeId = FindNode(board, new AxialHex(radius / 2, 0), MapNodeType.Elite);
		board.EliteUpNodeId = FindNode(board, new AxialHex(0, radius), MapNodeType.Elite);
		board.EliteDownNodeId = FindNode(board, new AxialHex(radius, -radius), MapNodeType.Elite);
	}

	private static int FindNode(HexBoardData board, AxialHex position, MapNodeType expectedType)
	{
		if (board.TryGetNodeId(position, out int nodeId))
		{
			MapBoardNode node = board.GetNode(nodeId);
			if (node != null)
			{
				node.Type = expectedType;
				node.IsSpecial = true;
				return nodeId;
			}
		}

		return -1;
	}

	private static MapNodeType RollRandomType(Random random)
	{
		int total = WeightEmpty + WeightNormalCombat + WeightHighRiskCombat + WeightNormalEvent + WeightDangerousEvent;
		int roll = random.Next(total);
		if (roll < WeightEmpty)
		{
			return MapNodeType.Empty;
		}
		roll -= WeightEmpty;
		if (roll < WeightNormalCombat)
		{
			return MapNodeType.NormalCombat;
		}
		roll -= WeightNormalCombat;
		if (roll < WeightHighRiskCombat)
		{
			return MapNodeType.HighRiskCombat;
		}
		roll -= WeightHighRiskCombat;
		return roll < WeightNormalEvent ? MapNodeType.NormalEvent : MapNodeType.DangerousEvent;
	}

	/// <summary>从指定节点出发做 BFS，收集可达格 NodeId 集合。</summary>
	public static HashSet<int> CollectReachableNodeIds(HexBoardData board, int fromNodeId)
	{
		HashSet<int> visited = new HashSet<int>();
		if (fromNodeId < 0)
		{
			return visited;
		}

		Queue<int> queue = new Queue<int>();
		queue.Enqueue(fromNodeId);
		visited.Add(fromNodeId);
		while (queue.Count > 0)
		{
			MapBoardNode node = board.GetNode(queue.Dequeue());
			if (node == null)
			{
				continue;
			}

			foreach (int next in node.NextIds)
			{
				if (visited.Add(next))
				{
					queue.Enqueue(next);
				}
			}
		}

		return visited;
	}
}
