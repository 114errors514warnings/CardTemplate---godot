// ItemEffectSpec.cs
// 物品效果 / 效果寿命轴 的文本解析（纯逻辑，可单测）。语法：
//   效果： <EffectType>[:参数1[:参数2…]]   例：Shield:5 / AddState:AddAttack:2 / ClearFirstNormalDebuff / DrawCard:1
//   占位： Pending:<说明>                   例：Pending:每回合多抽 1 张（待 StateType 扩词汇）
//   寿命： <FoodEffectDurationKind>[:数量]  例：BattleCount:1 / TimePoint:2 / DayCount:3；**留空 = BattleCount:1**
using System;
using System.Collections.Generic;
using System.Globalization;
using CardSimulator;

/// <summary>
/// 食物效果的寿命轴（2026-10-02 用户口径：不同食物的效果持续时间可以不同）。
/// `DurationValue` 不填默认为 1（=「下一场战斗」）。
/// tick 点：<see cref="BattleCount"/> 在战斗结算结束时 −1；<see cref="TimePoint"/> 在时间点变动时扣减；
/// <see cref="DayCount"/> 在跨天休息时 −1；三者都落档，读档不刷新。
/// </summary>
public enum FoodEffectDurationKind
{
	/// <summary>立即结算，不进持久列表。</summary>
	None = 0,

	/// <summary>接下来 N 场战斗（默认 1 = 下一场战斗）。</summary>
	BattleCount = 1,

	/// <summary>接下来 N 个时间点预算内的战斗（默认 1）。</summary>
	TimePoint = 2,

	/// <summary>接下来 N 天（默认 1）；期间内每场战斗开场都重新生效。</summary>
	DayCount = 3,
}

/// <summary>一条物品 / 食物效果：效果本体 + 持续时间轴。</summary>
public sealed class ItemEffectSpec
{
	public EffectType Type = EffectType.None;

	/// <summary>效果参数（语义随 <see cref="Type"/>：护盾值 / 攻击层数 / 百分比 / 抽牌数…）。</summary>
	public List<int> Params { get; } = new List<int>();

	public FoodEffectDurationKind DurationKind = FoodEffectDurationKind.BattleCount;

	/// <summary>寿命数量，默认 1（不填时按 1）。</summary>
	public int DurationValue = 1;

	/// <summary>原始文本，报错与调试用。</summary>
	public string Raw = string.Empty;

	public int ParamAt(int index) => index >= 0 && index < Params.Count ? Params[index] : 0;

	/// <summary>是否"只在本场战斗内"的一次性效果（用于战斗开场应用与显示）。</summary>
	public bool IsSingleBattle => DurationKind == FoodEffectDurationKind.BattleCount && DurationValue <= 1;

	public string ToText()
	{
		string body = Params.Count == 0 ? Type.ToString() : $"{Type}:{string.Join(":", Params)}";
		return $"{body}@{DurationKind}:{DurationValue}";
	}
}

public static class ItemEffectSpecParser
{
	/// <summary>
	/// 解析一个效果格。`Pending:&lt;说明&gt;` 记进 <paramref name="pendingNotes"/> 并返回 null（不静默丢弃）。
	/// 语法错误抛 <see cref="FormatException"/>（带 context，便于定位到表与行）。
	/// </summary>
	public static ItemEffectSpec ParseEffect(string raw, string context, IList<string> pendingNotes = null)
	{
		string text = (raw ?? string.Empty).Trim();
		if (text.Length == 0)
		{
			return null;
		}

		if (text.StartsWith("Pending:", StringComparison.OrdinalIgnoreCase))
		{
			pendingNotes?.Add(text.Substring("Pending:".Length).Trim());
			return null;
		}

		string[] segments = text.Split(':');
		if (!Enum.TryParse(segments[0].Trim(), true, out EffectType type) || type == EffectType.None)
		{
			throw new FormatException($"{context}：效果类型不存在或为 None：{text}");
		}

		ItemEffectSpec spec = new ItemEffectSpec { Type = type, Raw = text };
		for (int i = 1; i < segments.Length; i++)
		{
			string value = segments[i].Trim();
			if (value.Length == 0)
			{
				continue;
			}

			// AddState 等复合效果允许写状态名（AddState:AddAttack:2）——非数字的参数按状态名查枚举，落成数字。
			if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
			{
				if (Enum.TryParse(value, true, out StateType state) && state != StateType.None)
				{
					number = (int)state;
				}
				else
				{
					throw new FormatException($"{context}：参数既不是整数也不是已定义状态名：{text}");
				}
			}

			spec.Params.Add(number);
		}

		return spec;
	}

	/// <summary>
	/// 解析寿命格：`&lt;Kind&gt;[:数量]`；留空 = `BattleCount:1`。数量必须先于校验转为正整数。
	/// </summary>
	public static bool TryParseDuration(string raw, out FoodEffectDurationKind kind, out int value, string context)
	{
		kind = FoodEffectDurationKind.BattleCount;
		value = 1;
		string text = (raw ?? string.Empty).Trim();
		if (text.Length == 0)
		{
			return true; // 不填 = 下一场战斗
		}

		string[] segments = text.Split(':');
		if (!Enum.TryParse(segments[0].Trim(), true, out kind) || kind == FoodEffectDurationKind.None)
		{
			throw new FormatException($"{context}：寿命类型不存在或为 None：{text}（可用：" +
				string.Join(" / ", Enum.GetNames(typeof(FoodEffectDurationKind))) + "）");
		}

		if (segments.Length > 1 && segments[1].Trim().Length > 0)
		{
			if (!int.TryParse(segments[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || value <= 0)
			{
				throw new FormatException($"{context}：寿命数量必须是正整数：{text}");
			}
		}

		if (segments.Length > 2)
		{
			throw new FormatException($"{context}：寿命格最多 `<Kind>:<数量>` 两段：{text}");
		}

		return true;
	}

	/// <summary>把寿命写回效果（解析顺序：先效果、后寿命，便于表里分成两列）。</summary>
	public static void ApplyDuration(ItemEffectSpec spec, string durationRaw, string context)
	{
		if (spec == null)
		{
			return;
		}

		TryParseDuration(durationRaw, out FoodEffectDurationKind kind, out int value, context);
		spec.DurationKind = kind;
		spec.DurationValue = value;
	}
}
