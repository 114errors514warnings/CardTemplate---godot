// SettlementRewardPresenter.cs
// 结算奖励的份数 / 候选 / 文案 / 放弃日志：战斗结算界面交互案（2026-09-27 定稿）§三 / §四 / §五 / §7.4。
// 纯逻辑（只依赖 RunSaveData 与调用方注入的卡池委托、随机数），便于单测；
// 界面与落档由 SettlementUi / RunSession 负责。
//
// 2026-10-02 用户口径：**领取过的条目不再显示成「已领取」，直接从面板列表里消失**。
// 因此面板渲染走 `BuildVisibleItemTabs` / `BuildVisibleCardPools`（只留未领取的），
// `BuildItemTabs` / `IsCardPoolClaimed` 仍保留「已领取」这个判定本身（发奖防重、浮窗计数、放弃日志都要用）。
using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>结算面板上的一个物品 Tab（非卡牌条目，点击即领取）。</summary>
public sealed class SettlementItemTab
{
	public int RowIndex;
	public DropTableEntry Entry;
	public string ClaimKey = string.Empty;
	public bool Claimed;
	public string Text = string.Empty;
}

/// <summary>一份卡牌奖励的宿主槽位（一份 = 结算面板上一个卡牌 Tab）。</summary>
public sealed class SettlementCardSlot
{
	public int SlotIndex = -1;
	public int CharacterId;
}

public static class SettlementRewardPresenter
{
	/// <summary>卡牌奖励份数上限 = 角色槽位数（每个槽位一份，重复角色按槽位分开）。</summary>
	public const int MaxCardRewardCount = 3;

	/// <summary>三选一候选的**常态值**（不随折损变化；未来由装备影响 → 实现为参数而非常量）。</summary>
	public const int DefaultCandidateCount = 3;

	/// <summary>旧档兼容：没有份槽位的单份奖励，入组槽位在领取时按卡池反查。</summary>
	public const int LegacySlotIndex = -1;

	/// <summary>槽位越界 / 角色缺失时的兜底显示名（此时改用份自带的 `CharacterId` 兜底）。</summary>
	public const string UnknownSlotName = "角色 ?";

	public const string CardTabTextPrefix = "将一张牌添加到你的牌组。· ";
	public const string EmptyPoolText = "该角色暂无可用卡牌";

	// ── 物品 Tab（§四） ────────────────────────────────────────

	/// <summary>
	/// 列出本次结算的物品 Tab（掉落表中属于本次 DropTableId 且不是 Card 的行）。
	/// **行号沿用「该掉落表内的原行号」**（含 Card 行占位）：与旧实现的去重键格式一致，旧档已领取项不会重复发放。
	/// </summary>
	public static List<SettlementItemTab> BuildItemTabs(RunSaveData run, IReadOnlyList<DropTableEntry> allEntries)
	{
		List<SettlementItemTab> tabs = new List<SettlementItemTab>();
		if (run == null || allEntries == null)
		{
			return tabs;
		}

		List<DropTableEntry> rows = BattleRewardPresenter.GetEntriesForTable(allEntries, run.SettlementDropTableId);
		for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
		{
			DropTableEntry entry = rows[rowIndex];
			if (entry == null || entry.Category == DropCategory.Card)
			{
				continue;
			}

			string claimKey = GetClaimKey(rowIndex, entry);
			tabs.Add(new SettlementItemTab
			{
				RowIndex = rowIndex,
				Entry = entry,
				ClaimKey = claimKey,
				Claimed = IsItemClaimed(run, claimKey),
				Text = BattleRewardPresenter.FormatRewardLine(entry),
			});
		}

		return tabs;
	}

	/// <summary>
	/// 结算面板上**实际渲染**的物品 Tab：已领取的条目直接消失（2026-10-02 用户口径，
	/// 原来渲染成灰显 +「已领取」后缀）。面板与浮窗计数都用它；发奖防重仍看 `SettlementClaimedRewardKeys`。
	/// </summary>
	public static List<SettlementItemTab> BuildVisibleItemTabs(RunSaveData run, IReadOnlyList<DropTableEntry> allEntries)
	{
		List<SettlementItemTab> visible = new List<SettlementItemTab>();
		foreach (SettlementItemTab tab in BuildItemTabs(run, allEntries))
		{
			if (!tab.Claimed)
			{
				visible.Add(tab);
			}
		}

		return visible;
	}

	/// <summary>物品 Tab 的去重键：`{行号}:{Category}:{RewardParam}:{Amount}`（§4.2）。</summary>
	public static string GetClaimKey(int rowIndex, DropTableEntry entry)
	{
		return entry == null
			? string.Empty
			: $"{rowIndex}:{entry.Category}:{entry.RewardParam}:{entry.Amount}";
	}

	public static bool IsItemClaimed(RunSaveData run, string claimKey)
	{
		return run?.SettlementClaimedRewardKeys != null
			&& !string.IsNullOrEmpty(claimKey)
			&& run.SettlementClaimedRewardKeys.Contains(claimKey);
	}

	public static int CountUnclaimedItems(RunSaveData run, IReadOnlyList<DropTableEntry> allEntries)
	{
		// 面板上「还看得见的物品 Tab 数」= 未领取项数（已领取的直接消失，2026-10-02）。
		return BuildVisibleItemTabs(run, allEntries).Count;
	}

	// ── 卡牌份（§五） ──────────────────────────────────────────

	public static SettlementCardClaimSave FindCardClaim(RunSaveData run, int slotIndex)
	{
		if (run?.SettlementCardClaims == null)
		{
			return null;
		}

		foreach (SettlementCardClaimSave claim in run.SettlementCardClaims)
		{
			if (claim != null && claim.SlotIndex == slotIndex)
			{
				return claim;
			}
		}

		return null;
	}

	public static bool IsCardPoolClaimed(RunSaveData run, int slotIndex)
	{
		return FindCardClaim(run, slotIndex) != null;
	}

	/// <summary>
	/// 结算面板上**实际渲染**的卡牌份 Tab：已领取的份直接消失（2026-10-02 用户口径，
	/// 原来渲染成灰显 +「（已领取：&lt;卡名&gt;）」）。发奖防重仍看 `SettlementCardClaims`。
	/// </summary>
	public static List<SettlementCardPoolSave> BuildVisibleCardPools(RunSaveData run)
	{
		List<SettlementCardPoolSave> visible = new List<SettlementCardPoolSave>();
		if (run?.SettlementCardPools == null)
		{
			return visible;
		}

		foreach (SettlementCardPoolSave pool in run.SettlementCardPools)
		{
			if (pool != null && !IsCardPoolClaimed(run, pool.SlotIndex))
			{
				visible.Add(pool);
			}
		}

		return visible;
	}

	public static int CountUnclaimedCards(RunSaveData run)
	{
		return BuildVisibleCardPools(run).Count;
	}

	/// <summary>未领取项总数 = 未领取物品 Tab 数 + 未领取卡牌份数（浮窗计数用，§6.3）。</summary>
	public static int CountUnclaimed(RunSaveData run, IReadOnlyList<DropTableEntry> allEntries)
	{
		return CountUnclaimedItems(run, allEntries) + CountUnclaimedCards(run);
	}

	/// <summary>卡牌 Tab 文案：「将一张牌添加到你的牌组。· &lt;角色显示名&gt;」（§三）。</summary>
	public static string GetCardTabText(string displayName)
	{
		return CardTabTextPrefix + (string.IsNullOrWhiteSpace(displayName) ? "角色 ?" : displayName);
	}

	/// <summary>
	/// 按槽位生成卡牌奖励份：全额 = 每个槽位一份；折损 = **随机取消不同槽位的份**，保留的份按槽位序显示。
	/// 每份的候选**只取该份角色自己的卡池**（§5.3，不与队伍其它角色混合），不足则按实际张数。
	/// </summary>
	public static List<SettlementCardPoolSave> BuildCardPools(
		IReadOnlyList<SettlementCardSlot> slots,
		int rewardCount,
		Func<int, IReadOnlyList<int>> poolProvider,
		int candidateCount,
		Random rng)
	{
		List<SettlementCardPoolSave> pools = new List<SettlementCardPoolSave>();
		if (slots == null || slots.Count == 0 || rewardCount <= 0 || poolProvider == null)
		{
			return pools;
		}

		Random random = rng ?? new Random();
		List<SettlementCardSlot> kept = new List<SettlementCardSlot>(slots);
		if (rewardCount < kept.Count)
		{
			List<SettlementCardSlot> drawable = new List<SettlementCardSlot>(slots);
			for (int i = drawable.Count - 1; i > 0; i--)
			{
				int j = random.Next(i + 1);
				SettlementCardSlot swap = drawable[i];
				drawable[i] = drawable[j];
				drawable[j] = swap;
			}

			kept = drawable.GetRange(0, rewardCount);
			kept.Sort((SettlementCardSlot left, SettlementCardSlot right) => left.SlotIndex.CompareTo(right.SlotIndex));
		}

		int candidates = candidateCount <= 0 ? DefaultCandidateCount : candidateCount;
		foreach (SettlementCardSlot slot in kept)
		{
			IReadOnlyList<int> pool = poolProvider(slot.CharacterId) ?? Array.Empty<int>();
			pools.Add(new SettlementCardPoolSave
			{
				SlotIndex = slot.SlotIndex,
				CharacterId = slot.CharacterId,
				CandidateCardIds = BattleRewardPresenter.SampleFromPool(pool, candidates, random),
			});
		}

		return pools;
	}

	// ── 折损展示（P2-10.3 / §5.6） ────────────────────────────

	/// <summary>折损行：全额返回空串（不显示该行）；例 `卡牌奖励减少：击败 57.0%（≤ 70%）→ 卡牌奖励 2 份`。</summary>
	public static string FormatLossLine(double ratio, int rewardCount)
	{
		if (rewardCount >= MaxCardRewardCount)
		{
			return string.Empty;
		}

		string percent = (ratio * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%";
		string rule = rewardCount <= 1
			? "< " + (MonsterValuePoints.ReducedRewardThreshold * 100).ToString("0", CultureInfo.InvariantCulture) + "%"
			: "≤ " + (MonsterValuePoints.FullRewardThreshold * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
		return $"卡牌奖励减少：击败 {percent}（{rule}）→ 卡牌奖励 {rewardCount} 份";
	}

	// ── 来源与放弃日志（§7.4） ────────────────────────────────

	public const string AbandonModeDialog = "弹窗确认";
	public const string AbandonModeSuppressed = "已勾选不再提示";

	/// <summary>本次结算来源的节点类型文案（战斗 = 普通敌袭 / 高危敌袭 / 精英 / Boss；非战斗 = 普通事件 / 商人）。</summary>
	public static string GetSourceNodeTypeText(MapNodeType nodeType)
	{
		if (nodeType == MapNodeType.Empty)
		{
			return "未知";
		}

		return MapNodeTypeUtil.HasStageConfigFile(nodeType)
			? MapNodeTypeUtil.GetStageConfigFileName(nodeType)
			: nodeType.ToString();
	}

	/// <summary>放弃日志的逐项明细：未领物品 Tab（复用 FormatRewardLine）+ 未领卡牌份（`卡牌（角色显示名）未领`）。</summary>
	public static List<string> BuildUnclaimedDetails(RunSaveData run, IReadOnlyList<DropTableEntry> allEntries, Func<int, string> slotNameProvider)
	{
		List<string> details = new List<string>();
		if (run == null)
		{
			return details;
		}

		foreach (SettlementItemTab tab in BuildItemTabs(run, allEntries))
		{
			if (!tab.Claimed)
			{
				details.Add(tab.Text);
			}
		}

		if (run.SettlementCardPools != null)
		{
			foreach (SettlementCardPoolSave pool in run.SettlementCardPools)
			{
				if (pool == null || IsCardPoolClaimed(run, pool.SlotIndex))
				{
					continue;
				}

				details.Add($"卡牌（{GetSlotDisplayName(pool, slotNameProvider)}）未领");
			}
		}

		return details;
	}

	/// <summary>取某份卡牌的归属角色显示名（份自带槽位；旧档没有槽位时退回角色 Id 兜底）。</summary>
	public static string GetSlotDisplayName(SettlementCardPoolSave pool, Func<int, string> slotNameProvider)
	{
		if (pool == null)
		{
			return UnknownSlotName;
		}

		string name = slotNameProvider == null ? null : slotNameProvider(pool.SlotIndex);
		if (!string.IsNullOrWhiteSpace(name) && !string.Equals(name, UnknownSlotName, StringComparison.Ordinal))
		{
			return name;
		}

		return pool.CharacterId > 0 ? $"角色 {pool.CharacterId}" : UnknownSlotName;
	}

	/// <summary>放弃日志：一次实际作废只写一条（`[结算] SETTLEMENT_ABANDONED: …`）。</summary>
	public static string FormatAbandonLog(string sourceName, string sourceNodeTypeText, IReadOnlyList<string> details, string abandonMode)
	{
		int count = details?.Count ?? 0;
		string source = string.IsNullOrWhiteSpace(sourceName) ? "未知来源" : sourceName;
		string nodeType = string.IsNullOrWhiteSpace(sourceNodeTypeText) ? "未知" : sourceNodeTypeText;
		string detailText = count > 0 ? string.Join("；", details) : "无";
		return $"[结算] SETTLEMENT_ABANDONED: {source}（{nodeType}）| 未领取 {count} 项 | {detailText} | {abandonMode}";
	}
}
