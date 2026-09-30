// EventCardReward.cs
// 非战斗来源（事件 / 商人等）发卡的统一入口参数（战斗结算界面交互案 §5.7）。
// 一份 = 一个角色槽位 = 结算界面上的一个卡牌 Tab；来源指定具体卡时该份候选恒为这 1 张，
// 未指定时按该份角色自己的卡池抽 3 张；非战斗来源**不做折损**（折损只属战斗结算）。
// 纯逻辑：只依赖传入的存档槽位、卡池委托与随机源，便于单测；落档与界面一律走 RunSession.EnterSettlement。
using System;
using System.Collections.Generic;

/// <summary>事件发卡的一份：`SlotIndex` = 归属槽位（0 起，与 `SettlementCardPoolSave` 同序）；`CardId > 0` = 来源指定的具体卡，0 = 该角色卡池任选。</summary>
public readonly record struct EventCardRewardSpec(int SlotIndex, int CardId);

public static class EventCardReward
{
	/// <summary>事件 JSON 的效果类型名（`剧情事件.md` / `事件系统 §6.5` 登记为 `CardAdd`）：进统一结算界面由玩家领取，不自动发放。</summary>
	public const string EffectTypeCardAdd = "CardAdd";

	/// <summary>事件 JSON 发卡效果的 `target` 值：目标 = 队伍槽位。</summary>
	public const string TargetSlot = "Slot";

	/// <summary>
	/// 解析一条发卡效果：`target` 必须是 `Slot`、`value` = 槽位序（0 起）、`referenceId` 留空 = 该角色卡池任选，
	/// 或写具体卡牌 Id。槽位是否在队伍内由调用方（执行器）校验（这里拿不到队伍长度）。
	/// </summary>
	public static bool TryParseEffect(string type, string target, int value, string referenceId, out EventCardRewardSpec spec, out string error)
	{
		spec = default;
		error = string.Empty;
		if (!string.Equals(type, EffectTypeCardAdd, StringComparison.Ordinal))
		{
			error = $"不是发卡效果：{type}";
			return false;
		}

		if (!string.Equals(target, TargetSlot, StringComparison.Ordinal))
		{
			error = $"发卡的 target 只能是 {TargetSlot}（实际 {target}）";
			return false;
		}

		if (value < 0)
		{
			error = $"槽位必须 ≥ 0（实际 {value}）";
			return false;
		}

		int cardId = 0;
		if (!string.IsNullOrWhiteSpace(referenceId) && (!int.TryParse(referenceId.Trim(), out cardId) || cardId <= 0))
		{
			error = $"referenceId 必须是卡牌 Id 或留空（实际 {referenceId}）";
			return false;
		}

		spec = new EventCardRewardSpec(value, cardId);
		return true;
	}

	/// <summary>
	/// 按份生成候选（落档即定稿，重进不重抽）：具体卡份的候选恒为该 1 张；任选份从**该份角色自己的卡池**抽
	/// `candidateCount` 张（§5.3）。槽位越界或同一槽位重复出现时只取第一条（份的身份 = 槽位）。
	/// </summary>
	public static List<SettlementCardPoolSave> BuildPools(
		IReadOnlyList<EventCardRewardSpec> specs,
		RunSaveData run,
		Func<int, IReadOnlyList<int>> poolProvider,
		int candidateCount,
		Random rng)
	{
		List<SettlementCardPoolSave> pools = new List<SettlementCardPoolSave>();
		if (specs == null || specs.Count == 0 || run == null || run.CharacterSlots == null)
		{
			return pools;
		}

		int candidates = candidateCount <= 0 ? SettlementRewardPresenter.DefaultCandidateCount : candidateCount;
		Random random = rng ?? new Random();
		HashSet<int> usedSlots = new HashSet<int>();
		foreach (EventCardRewardSpec spec in specs)
		{
			if (spec.SlotIndex < 0 || spec.SlotIndex >= run.CharacterSlots.Count || !usedSlots.Add(spec.SlotIndex))
			{
				continue;
			}

			int characterId = run.CharacterSlots[spec.SlotIndex].CharacterId;
			IReadOnlyList<int> candidateIds = spec.CardId > 0
				? new[] { spec.CardId }
				: BattleRewardPresenter.SampleFromPool(poolProvider == null ? Array.Empty<int>() : poolProvider(characterId), candidates, random);
			pools.Add(new SettlementCardPoolSave
			{
				SlotIndex = spec.SlotIndex,
				CharacterId = characterId,
				CandidateCardIds = new List<int>(candidateIds),
			});
		}

		return pools;
	}

	/// <summary>非战斗来源的结算入参：不绑战斗掉落表（没有物品 Tab）、档位恒 0、比例恒 1.0（不做折损，§5.7）。</summary>
	public static SettlementStartRequest BuildRequest(string sourceName, MapNodeType sourceNodeType, List<SettlementCardPoolSave> pools)
	{
		return new SettlementStartRequest
		{
			SourceName = sourceName ?? string.Empty,
			SourceNodeType = sourceNodeType,
			DropTableId = 0,
			CardPools = pools ?? new List<SettlementCardPoolSave>(),
			LossTier = 0,
			ValueRatio = 1.0,
		};
	}
}
