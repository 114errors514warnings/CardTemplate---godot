// MonsterValuePoints.cs
// 奖励折损口径（代码需求清单 P2-10.3）：综合价值点数 = 面板 HP + K × 意图加权价值（K = 1.0，2026-09-27 定稿）。
// 口径出处：单位数值平衡标准 §2.4 / 六边形战场玩法 §7.4。
// 纯逻辑：数值来源由调用方以委托注入，便于单测；运行时走 LoadingSystem（Monster.csv 的 MAX_HP + MonsterValue.csv 的意图价值）。
// 折损分流：生存关**通关总是全额**（`IsSurvivalLevelType`）；**爪牙不计入分子分母**（`EnemyValueSample.IsMinion`）。
using Godot;
using System;
using System.Collections.Generic;

public static class MonsterValuePoints
{
	/// <summary>意图折算系数 K：**1.0 已定稿**（单位数值平衡标准 §2.4）。</summary>
	public const double IntentWeightK = 1.0;

	/// <summary>击败比例 &gt; 该值 = 全额（3 份卡牌奖励）。</summary>
	public const double FullRewardThreshold = 0.70;

	/// <summary>击败比例 ≥ 该值 = 降 1 份（2 份）；&lt; 该值 = 降 2 份（1 份）。</summary>
	public const double ReducedRewardThreshold = 0.50;

	public const int FullCardRewardCount = 3;

	/// <summary>意图加权价值 = Σ(意图价值) ÷ 意图条数（权重默认 1）；无意图返回 0。**含无效果意图**（逃跑 = 0）。</summary>
	public static double GetWeightedIntentValue(IReadOnlyList<int> intentValues)
	{
		if (intentValues == null || intentValues.Count == 0)
		{
			return 0;
		}

		double sum = 0;
		foreach (int value in intentValues)
		{
			sum += value;
		}

		return sum / intentValues.Count;
	}

	/// <summary>综合价值点数 = 面板 HP + K × 意图加权价值。</summary>
	public static double GetValuePoints(int maxHp, IReadOnlyList<int> intentValues)
	{
		int hp = maxHp < 0 ? 0 : maxHp;
		return hp + IntentWeightK * GetWeightedIntentValue(intentValues);
	}

	/// <summary>运行时取某怪的综合价值点数（面板 HP 取 Monster.csv，意图价值取 MonsterValue.csv）。</summary>
	public static double GetValuePointsForMonster(int monsterId)
	{
		int maxHp = 0;
		if (LoadingSystem.MonsterDictionary.TryGetValue(monsterId, out Monster monster) && monster != null)
		{
			maxHp = monster.MAX_HP;
		}

		MonsterValueEntry entry = LoadingSystem.FindMonsterValueEntry(monsterId);
		if (entry == null)
		{
			GD.PrintErr($"[折损] MonsterValue.csv 缺少怪物 {monsterId} 的意图价值行，本次按「面板 HP」单口径计算。");
			return maxHp;
		}

		return GetValuePoints(maxHp, entry.IntentValues);
	}

	/// <summary>生存关的关卡类型值（`BattleLevelConfig.LevelType`）：生存关按回合数结算，**通关总是全额**（玩法 §7.4）。</summary>
	public const string SurvivalLevelType = "Survival";

	/// <summary>是否生存关（大小写不敏感、忽略首尾空白）：生存关豁免折损，直接按全额落档。</summary>
	public static bool IsSurvivalLevelType(string levelType)
	{
		return !string.IsNullOrWhiteSpace(levelType)
			&& string.Equals(levelType.Trim(), SurvivalLevelType, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>一个敌方单位的价值样本：`IsMinion`（爪牙）**不计入分子与分母**（玩法 §7.4 / §7.5）。</summary>
	public readonly record struct EnemyValueSample(int MonsterId, bool Defeated, bool IsMinion = false);

	/// <summary>
	/// 击败比例（含爪牙分流）：爪牙不计入分子分母；没有任何可计奖单位时返回 1.0（视为不折损）。
	/// 运行时路径用它；`GetDefeatRatio(部署, 击败, 取值)` 是同口径的简化版（只有怪物 Id 列表时用）。
	/// </summary>
	public static double GetDefeatRatio(IReadOnlyList<EnemyValueSample> samples, Func<int, double> valueProvider)
	{
		if (samples == null || samples.Count == 0)
		{
			return 1.0;
		}

		Func<int, double> provider = valueProvider ?? (_ => 0.0);
		double deployed = 0;
		double defeated = 0;
		foreach (EnemyValueSample sample in samples)
		{
			if (sample.IsMinion)
			{
				continue;
			}

			double value = provider(sample.MonsterId);
			deployed += value;
			if (sample.Defeated)
			{
				defeated += value;
			}
		}

		return GetLossRatio(defeated, deployed);
	}

	/// <summary>击败比例 = 被击败者价值 ÷ 本场应击败者价值（**分母含逃跑 / 存活者**，分子分母同口径）。</summary>
	public static double GetDefeatRatio(IReadOnlyList<int> deployedMonsterIds, IReadOnlyList<int> defeatedMonsterIds, Func<int, double> valueProvider)
	{
		if (deployedMonsterIds == null || deployedMonsterIds.Count == 0)
		{
			return 1.0; // 没有部署者（非战斗来源 / 空场）：视为无折损
		}

		Func<int, double> provider = valueProvider ?? (_ => 0.0);
		double deployed = 0;
		foreach (int monsterId in deployedMonsterIds)
		{
			deployed += provider(monsterId);
		}

		double defeated = 0;
		if (defeatedMonsterIds != null)
		{
			foreach (int monsterId in defeatedMonsterIds)
			{
				defeated += provider(monsterId);
			}
		}

		return GetLossRatio(defeated, deployed);
	}

	/// <summary>比例 = 分子 ÷ 分母；分母 ≤ 0 视为无折损（1.0），结果夹在 [0, 1]。</summary>
	public static double GetLossRatio(double defeatedValue, double deployedValue)
	{
		if (deployedValue <= 0)
		{
			return 1.0;
		}

		double ratio = defeatedValue / deployedValue;
		if (ratio < 0)
		{
			return 0;
		}

		return ratio > 1 ? 1 : ratio;
	}

	/// <summary>折损档位 = **被取消的卡牌份数**（0 = 全额 3 份、1 = 2 份、2 = 1 份）。</summary>
	public static int GetLossTier(double ratio)
	{
		if (ratio > FullRewardThreshold)
		{
			return 0;
		}

		return ratio >= ReducedRewardThreshold ? 1 : 2;
	}

	/// <summary>本次卡牌奖励份数（3 / 2 / 1，至少保留 1 份）。</summary>
	public static int GetCardRewardCount(double ratio)
	{
		return FullCardRewardCount - GetLossTier(ratio);
	}

	/// <summary>由档位反推份数（落档字段 `SettlementLossTier` 的读回口）。</summary>
	public static int GetCardRewardCountFromTier(int lossTier)
	{
		int tier = lossTier < 0 ? 0 : (lossTier > FullCardRewardCount - 1 ? FullCardRewardCount - 1 : lossTier);
		return FullCardRewardCount - tier;
	}
}
