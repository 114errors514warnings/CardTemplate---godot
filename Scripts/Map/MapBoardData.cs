// MapBoardData.cs
// 地图运行期/静态数据模型（纯逻辑，不依赖 Godot，便于 xUnit 单测）。
using System.Collections.Generic;

/// <summary>轴向坐标 (q, r)；s = -(q + r)。</summary>
public readonly struct AxialHex
{
	public readonly int Q;
	public readonly int R;

	public AxialHex(int q, int r)
	{
		Q = q;
		R = r;
	}

	public int S => -Q - R;

	public static int Distance(AxialHex a, AxialHex b)
	{
		int dq = a.Q - b.Q;
		int dr = a.R - b.R;
		int dqr = dq + dr;
		int max = System.Math.Max(System.Math.Abs(dq), System.Math.Abs(dr));
		return System.Math.Max(max, System.Math.Abs(dqr));
	}

	public override bool Equals(object obj) => obj is AxialHex other && other.Q == Q && other.R == R;
	public override int GetHashCode() => (Q * 397) ^ R;

	public static bool operator ==(AxialHex a, AxialHex b) => a.Q == b.Q && a.R == b.R;
	public static bool operator !=(AxialHex a, AxialHex b) => !(a == b);
}

/// <summary>单格点。</summary>
public sealed class MapBoardNode
{
	public int NodeId;
	public AxialHex Position;
	public MapNodeType Type = MapNodeType.Empty;
	public bool IsSpecial;
	public bool Visited;

	/// <summary>相邻格点（版图内全部六边形相邻，最多 6 个）。</summary>
	public readonly List<int> NextIds = new List<int>();
}

/// <summary>一次生成出的完整地图版图数据。</summary>
public sealed class HexBoardData
{
	public const int DefaultRadius = 6;

	public int Radius;
	public int StartNodeId = -1;
	public int BossNodeId = -1;
	public int VillageNodeId = -1;
	public int EliteMidNodeId = -1;
	public int EliteUpNodeId = -1;
	public int EliteDownNodeId = -1;

	/// <summary>按 NodeId 顺序的节点表。</summary>
	public List<MapBoardNode> Nodes { get; } = new List<MapBoardNode>();

	private readonly Dictionary<AxialHex, int> indexByPosition = new Dictionary<AxialHex, int>();

	public void RegisterIndex(AxialHex position, int nodeId)
	{
		indexByPosition[position] = nodeId;
	}

	public bool TryGetNodeId(AxialHex position, out int nodeId) => indexByPosition.TryGetValue(position, out nodeId);

	public MapBoardNode GetNode(int nodeId) => nodeId >= 0 && nodeId < Nodes.Count ? Nodes[nodeId] : null;

	public IReadOnlyDictionary<AxialHex, int> PositionIndex => indexByPosition;
}
