// CardSpatialSpec.cs
// 战场卡牌的空间规格（纯逻辑）：从 勇士Card.csv 扩展列 SpatialShape/SpatialArgs/TrapId 解析。
using System;
using System.Collections.Generic;

namespace CardSimulator.Battlefield;

public enum CardSpatialShape
{
	None = 0,    // 沿用旧单位目标语义（无空间形状）
	Single = 1,  // 单格目标（默认相邻/射程内）
	Burst = 2,   // 以目标格为中心半径内的爆发
	Line = 3,    // 从施法方向延伸的直线
	SelfMove = 4, // 卡牌移动：沿所选合法路线移动
	Trap = 5,    // 在目标格放置陷阱（格上触发物件）
}

public sealed class CardSpatialSpec
{
	public int CardId;
	public CardSpatialShape Shape = CardSpatialShape.None;
	public int MaxRange = 1;
	public int Radius = 1;
	public int Length = 1;
	public string TrapId = string.Empty;
	public bool Penetrates;
	public bool Explodes;

	public bool HasSpatial => Shape != CardSpatialShape.None;

	public static CardSpatialSpec ParseFields(string[] fields)
	{
		CardSpatialSpec spec = new CardSpatialSpec();
		if (fields == null || fields.Length == 0)
		{
			return spec;
		}

		int.TryParse(Field(fields, 0), out int cardId);
		spec.CardId = cardId;
		spec.Shape = ParseShape(Field(fields, 9));
		spec.TrapId = Field(fields, 11);

		string args = Field(fields, 10);
		if (!string.IsNullOrWhiteSpace(args))
		{
			foreach (string token in args.Split(new char[] { ';', '|', '，', ',' }, StringSplitOptions.RemoveEmptyEntries))
			{
				string t = token.Trim();
				if (TryArg(t, "Range", out int range)) spec.MaxRange = Math.Max(1, range);
				else if (TryArg(t, "Radius", out int radius)) spec.Radius = Math.Max(1, radius);
				else if (TryArg(t, "Length", out int length)) spec.Length = Math.Max(1, length);
				else if (t.Equals("Pierce", StringComparison.OrdinalIgnoreCase) || t.Equals("Penetrates", StringComparison.OrdinalIgnoreCase)) spec.Penetrates = true;
				else if (t.Equals("Explode", StringComparison.OrdinalIgnoreCase) || t.Equals("Explodes", StringComparison.OrdinalIgnoreCase)) spec.Explodes = true;
			}
		}

		return spec;
	}

	private static string Field(string[] fields, int index) => index < fields.Length ? (fields[index] ?? string.Empty).Trim() : string.Empty;

	private static bool TryArg(string token, string key, out int value)
	{
		value = 0;
		if (token.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase)
			|| token.StartsWith(key + "：", StringComparison.OrdinalIgnoreCase))
		{
			int eq = token.IndexOf('=');
			if (eq < 0) eq = token.IndexOf('：');
			return int.TryParse(token.Substring(eq + 1).Trim(), out value);
		}

		return false;
	}

	private static CardSpatialShape ParseShape(string raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			return CardSpatialShape.None;
		}

		return Enum.TryParse(raw.Trim(), true, out CardSpatialShape shape) ? shape : CardSpatialShape.None;
	}
}
