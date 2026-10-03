using Godot;
using System;
using System.Collections.Generic;
using CardSimulator;

/// <summary>
/// 卡牌归属与掉落规则（[总体卡牌设计](../../../../README/玩法说明文档/系统规则/卡牌系统/总体卡牌设计.md)「卡牌归属与掉落」；
/// 2026-10-03 用户口径）。
///
/// 归属只由**卡牌所在表**决定：
/// - `DataBase/Card/&lt;角色名&gt;Card.csv` = 该角色的**专有牌**（如 `精灵Card.csv`）；
/// - `DataBase/Card/通用/通用Card.csv` = **通用牌**，不属于任何角色。
///
/// 掉落规则：`LoadingSystem.GetCharacterRewardCardIds` 只取该角色的专有来源；
/// <see cref="IncludeGenericSources"/> 是用户口径里「除非特殊装备效果」的开口 ——
/// 该效果落地时把它打开（按角色 / 按装备实例判定即可，其它调用面不必改）。
/// </summary>
public static class CardRewardOwnership
{
	/// <summary>通用卡表在 `DataBase/Card/` 下的相对路径（写入 `CharacterRewardPool.csv` 时的原文）。</summary>
	public const string GenericCardSource = "通用/通用Card.csv";

	/// <summary>是否把通用卡表也当作掉落候选。默认 <c>false</c>（掉落的牌必须是该角色的专有牌）。</summary>
	public static bool IncludeGenericSources { get; set; }

	/// <summary>该来源是否指向通用卡表（忽略大小写与首尾空白，兼容 `/`、`\` 与 `/` 前缀写法）。</summary>
	public static bool IsGenericSource(string cardSource)
	{
		if (string.IsNullOrWhiteSpace(cardSource))
		{
			return false;
		}

		string normalized = cardSource.Trim().Replace('\\', '/').TrimStart('/');
		return string.Equals(normalized, GenericCardSource, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// 等级是否允许进掉落候选（2026-10-04 用户口径 / 施工清单 T9）：**只含该角色 B–S 级专属牌**。
	/// `None` = 该行没有等级数据 → **不拦截**（缺数据不能静默把牌从池里删掉，加载侧会打一次告警）；
	/// `C` / `D`（角色初始牌档）= 拦截。次序见 <see cref="CardTier"/>。
	/// </summary>
	public static bool IsTierEligibleForReward(CardTier tier) => tier == CardTier.None || tier >= CardTier.B;

	/// <summary>卡牌是否可进掉落候选（当前只看等级；专有 / 通用来源判定见 <see cref="IsGenericSource"/>）。</summary>
	public static bool IsCardEligibleForReward(Card card) => card == null || IsTierEligibleForReward(card.Tier);
}

/// <summary>
/// 解析 DataBase/Card/CharacterRewardPool.csv。
/// 列：CharacterId,CardSource（CardSource 为 DataBase/Card/ 下相对路径）
/// </summary>
public static class LoadCharacterRewardPoolCsv
{
	public static List<CharacterRewardSource> LoadSourcesFromCSV(string filePath)
	{
		List<CharacterRewardSource> result = new List<CharacterRewardSource>();
		string[] dataLines = LoadCsv.LoadCSVDataLines(filePath);
		if (dataLines.Length == 0)
		{
			return result;
		}

		foreach (string line in dataLines)
		{
			if (string.IsNullOrWhiteSpace(line))
			{
				continue;
			}

			CharacterRewardSource source = ParseLine(line);
			if (source != null)
			{
				result.Add(source);
			}
		}

		return result;
	}

	public static CharacterRewardSource ParseLine(string line)
	{
		try
		{
			string[] fields = LoadCsv.ParseCSVFields(line);
			if (fields.Length < 2)
			{
				GD.PrintErr($"[CharacterRewardPool] 行格式错误：期望至少 2 列，实际 {fields.Length}：{line}");
				return null;
			}

			return new CharacterRewardSource
			{
				CharacterId = int.Parse(fields[0]),
				CardSource = fields[1].Trim(),
			};
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[CharacterRewardPool] 行解析异常：{line}\nException: {ex.Message}");
			return null;
		}
	}
}
