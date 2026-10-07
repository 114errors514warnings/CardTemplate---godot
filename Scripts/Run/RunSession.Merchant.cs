// RunSession.Merchant.cs
// 商人（地点关 `LevelType = Merchant`）的会话入口：货架 / 卡包 / 卡牌操作的**唯一结算出口**（界面只调这里）。
// 口径出处：README/施工文档/2026/2026.10/交互/商人交互案.md §四–§七（购买流程）、§5.1 / §5.2 / §5.3（价格与次数）。
// 分工：规则与文案住 `MerchantStock`（货架抽样）/ `MerchantCardPacks`（卡包抽样）/ `DeckOps`（卡牌操作校验与计价）；
//   本文件只做「校验 → 扣金币 → 改存档 → Save()」。
// **商人节点只能进入一次**（商人案 §一）→ 快照（货架 / 卡包）与卡牌操作计数都是**本局一次**，
//   不因开关界面重置（`MerchantState.StockGenerated` / `CardPacksGenerated` / `RemoveUsed` / `TransferUsed`）。
// 商人不消耗时间点（§一 / §五）：买货、买卡、删 / 变 / 移 / 升级都只花金币；锻造炉那一次打造另算（§4.9）。
using Godot;
using System;
using System.Collections.Generic;
using CardSimulator;

/// <summary>商人「卡牌相关操作」四项（商人案 §4.8 / §6.3.1）。</summary>
public enum MerchantDeckOp
{
	Remove = 0,
	Change = 1,
	Transfer = 2,
	Upgrade = 3,
}

public partial class RunSession
{
	// ── 快照（本局首次进入本商人时生成一次）──────────────────────────────

	/// <summary>货架 / 卡包快照是否都已生成（界面「余 N / M」与货架格的恢复都读它）。</summary>
	public bool MerchantSnapshotReady =>
		Current?.MerchantState != null && Current.MerchantState.StockGenerated && Current.MerchantState.CardPacksGenerated;

	/// <summary>
	/// 首次进入本商人时生成**货架 + 5 个卡包**快照并落档（§七：本局固定、不刷新、不补位）。
	/// 已生成过则原样返回（不重抽）。生成种子绑「本局种子 × 用途盐」，同局读档重进得到同一份快照。
	/// </summary>
	public void EnsureMerchantSnapshot()
	{
		if (Current == null)
		{
			return;
		}

		RunMerchantStateSave state = Current.MerchantState ??= new RunMerchantStateSave();
		Random random = VillagePlaceData.MerchantRandom(Current, 0x4D45);
		if (!state.StockGenerated)
		{
			state.Stock = MerchantStock.Generate(random, VillagePlaceData.MerchantStockCandidates(),
				VillagePlaceData.MerchantPrices());
			state.StockGenerated = true;
			GD.Print($"[商人] 货架快照生成：{state.Stock.Count} 格（材料 / 食物 / 装备 / 道具各 3、钥匙 1）。");
		}

		if (!state.CardPacksGenerated)
		{
			state.CardPacks = MerchantCardPacks.Generate(
				random,
				VillagePlaceData.MerchantPackRows(),
				VillagePlaceData.MerchantSlotCardPools(Current),
				VillagePlaceData.MerchantGenericCardPool(),
				VillagePlaceData.MerchantCardPrices());
			state.CardPacksGenerated = true;
			int total = 0;
			foreach (RunMerchantCardPackSave pack in state.CardPacks)
			{
				total += pack.Cards?.Count ?? 0;
			}

			GD.Print($"[商人] 卡包快照生成：{state.CardPacks.Count} 包 / {total} 张。");
		}

		Save();
	}

	// ── 货架购买（商人案 §6.1）────────────────────────────────────────────

	/// <summary>
	/// 买货架上的一格：**未售出 → 金币够 → 背包可入账** 逐项校验，通过后扣金币 → 入背包（钥匙走 `Keys`）→
	/// 该格转「已售出」→ `Save()`。任一项不过 → 不改任何状态并返回原因（§6.1 失败原因表）。
	/// </summary>
	public bool TryBuyMerchantStock(MerchantCategory category, int slotIndex, out string error)
	{
		error = string.Empty;
		if (Current?.MerchantState == null)
		{
			error = "没有进行中的本局。";
			return false;
		}

		RunMerchantStockEntrySave entry = MerchantStock.FindEntry(Current.MerchantState.Stock, category, slotIndex);
		if (entry == null)
		{
			error = MerchantStock.SoldOutText;
			return false;
		}

		if (entry.Sold)
		{
			error = MerchantStock.SoldOutText;
			return false;
		}

		if (Current.Gold < entry.Price)
		{
			error = MerchantStock.GoldShortText(entry.Price, Current.Gold);
			return false;
		}

		// 钥匙不走背包（它没有重量也没有实例），其余四类按各自的类目入包。
		if (category == MerchantCategory.Key)
		{
			if (!TryAddKeys(1, out error))
			{
				return false;
			}

			Current.Gold -= entry.Price;
			entry.Sold = true;
			Save();
			return true;
		}

		BagCategory bag = category switch
		{
			MerchantCategory.Material => BagCategory.Material,
			MerchantCategory.Food => BagCategory.Food,
			MerchantCategory.Equipment => BagCategory.Equipment,
			_ => BagCategory.Item,
		};

		if (RunBagSystem.WouldExceedLoad(Current, ItemNameResolver.LoadOf(bag, entry.DefinitionKey)))
		{
			error = MerchantStock.BagOverloadText;
			return false;
		}

		if (entry.DefinitionKey <= 0)
		{
			error = $"{entry.DefinitionId} 没有可入包的定义键（配表缺 ID）。";
			return false;
		}

		Current.Gold -= entry.Price;
		if (!TryAddBagItem(bag, entry.DefinitionKey, 1, out string addError))
		{
			Current.Gold += entry.Price; // 入包失败：金币回滚，商品保持可买。
			error = addError;
			return false;
		}

		entry.Sold = true;
		Save();
		return true;
	}

	// ── 卡包购买（商人案 §6.2）──────────────────────────────────────────

	/// <summary>目标槽位不可写入的统一文案（§6.2 失败原因表）。</summary>
	public const string DeckSlotRejectText = "该角色的卡组当前不可写入。";

	/// <summary>包 1–3 固定写回的槽位（= `PackIndex - 1`）；包 4 / 5 返回 -1（买时由玩家点选，§4.3）。</summary>
	public int MerchantPackOwnerSlot(int packIndex) =>
		MerchantCardPacks.FindPack(Current?.MerchantState?.CardPacks, packIndex)?.OwnerSlot ?? -1;

	/// <summary>
	/// 在卡包详细里买一张卡：**该卡未售出 → 金币够 → 目标槽位可写入** 逐项校验，
	/// 通过后扣金币 → `AddCardToSlotDeck(槽位, 卡 Id, 0)`（新卡永久升级级数 0）→ 该卡位转「已售出」→ `Save()`。
	/// `targetSlot`：包 1–3 传包绑定槽位；包 4 / 5 传玩家点选的槽位（§4.3 归属选择）。
	/// </summary>
	public bool TryBuyMerchantCard(int packIndex, int cardIndex, int targetSlot, out string error)
	{
		error = string.Empty;
		if (Current?.MerchantState == null)
		{
			error = "没有进行中的本局。";
			return false;
		}

		RunMerchantCardPackSave pack = MerchantCardPacks.FindPack(Current.MerchantState.CardPacks, packIndex);
		if (pack?.Cards == null || cardIndex < 0 || cardIndex >= pack.Cards.Count)
		{
			error = "该卡包没有这一张卡。";
			return false;
		}

		RunMerchantCardEntrySave card = pack.Cards[cardIndex];
		if (card.Sold)
		{
			error = MerchantStock.SoldOutText;
			return false;
		}

		if (Current.Gold < card.Price)
		{
			error = MerchantStock.GoldShortText(card.Price, Current.Gold);
			return false;
		}

		if (Current.CharacterSlots == null || targetSlot < 0 || targetSlot >= Current.CharacterSlots.Count)
		{
			error = DeckSlotRejectText;
			return false;
		}

		Current.Gold -= card.Price;
		AddCardToSlotDeck(targetSlot, card.CardId, 0);
		card.Sold = true;
		card.GrantedSlot = targetSlot;
		Save();
		return true;
	}

	// ── 卡牌操作（商人案 §4.8 / §5.2 / §6.3）────────────────────────────

	/// <summary>
	/// 卡牌操作的**全部前置校验**（次数 → 卡组下限 / 下标 → 目标槽位 → 升级上限 → 变化需有同等级卡）：
	/// 不通过时 `error` 给原因并返回 false；通过时 `cost` 出金币价。
	/// 界面用它在按钮上显示价与灰态；结算 `TryMerchantDeckOp` 用它做「先校验、后扣账」（失败不改任何状态）。
	/// </summary>
	public bool ValidateMerchantDeckOp(
		MerchantDeckOp op, int slotIndex, int deckIndex, int targetSlot, out int cost, out string error)
	{
		cost = -1;
		error = string.Empty;
		if (Current?.MerchantState == null)
		{
			error = "没有进行中的本局。";
			return false;
		}

		if (Current.CharacterSlots == null || slotIndex < 0 || slotIndex >= Current.CharacterSlots.Count)
		{
			error = DeckSlotRejectText;
			return false;
		}

		List<RunDeckEntry> deck = GetSlotDeck(slotIndex) ?? new List<RunDeckEntry>();
		int slotCount = Current.CharacterSlots.Count;
		switch (op)
		{
			case MerchantDeckOp.Remove:
				if (Current.MerchantState.RemoveUsed >= DeckOps.RemovePerMerchant)
				{
					error = DeckOps.UsedText;
					return false;
				}

				if (!DeckOps.CanRemoveCard(deck.Count, deckIndex, out error))
				{
					return false;
				}

				cost = DeckOps.RemoveGold;
				return true;

			case MerchantDeckOp.Change:
				if (!DeckOps.CanRemoveCard(deck.Count, deckIndex, out error))
				{
					return false;
				}

				if (!HasSameTierReplacement(slotIndex, deckIndex))
				{
					error = DeckOps.NoSameTierText;
					return false;
				}

				cost = DeckOps.ChangeGold;
				return true;

			case MerchantDeckOp.Transfer:
				if (Current.MerchantState.TransferUsed >= DeckOps.TransferPerMerchant)
				{
					error = DeckOps.UsedText;
					return false;
				}

				int targetCount = targetSlot >= 0 && targetSlot < slotCount
					? GetSlotDeck(targetSlot)?.Count ?? 0
					: 0;
				if (!DeckOps.CanTransferCard(deck.Count, deckIndex, slotCount, targetSlot, targetCount, out error))
				{
					return false;
				}

				cost = DeckOps.TransferGold;
				return true;

			default:
				if (deckIndex < 0 || deckIndex >= deck.Count)
				{
					error = DeckOps.IndexOutOfRangeText;
					return false;
				}

				int level = deck[deckIndex]?.PermanentUpgradeLevel ?? 0;
				if (!DeckOps.CanUpgradeCard(level, out error))
				{
					return false;
				}

				cost = DeckOps.UpgradeGold(level);
				return true;
		}
	}

	/// <summary>
	/// 结算一次卡牌操作：校验（`ValidateMerchantDeckOp`）→ 金币 → 改卡组 → 次数 +1（删 / 移）→ `Save()`。
	/// 任一项不过 → **不改任何状态**（§6.3 第 5 条）。
	/// </summary>
	public bool TryMerchantDeckOp(MerchantDeckOp op, int slotIndex, int deckIndex, int targetSlot, out string error)
	{
		if (!ValidateMerchantDeckOp(op, slotIndex, deckIndex, targetSlot, out int cost, out error))
		{
			return false;
		}

		if (Current.Gold < cost)
		{
			error = DeckOps.GoldShortText(cost, Current.Gold);
			return false;
		}

		switch (op)
		{
			case MerchantDeckOp.Remove:
				if (!TryRemoveCardFromSlotDeck(slotIndex, deckIndex, out error))
				{
					return false;
				}

				Current.MerchantState.RemoveUsed++;
				break;

			case MerchantDeckOp.Change:
				if (!ChangeSlotCard(slotIndex, deckIndex, out error))
				{
					return false;
				}

				break;

			case MerchantDeckOp.Transfer:
				if (!TryTransferDeckCard(slotIndex, deckIndex, targetSlot, out error))
				{
					return false;
				}

				Current.MerchantState.TransferUsed++;
				break;

			default:
				if (!TryUpgradeSlotDeckCard(slotIndex, deckIndex, out error))
				{
					return false;
				}

				break;
		}

		Current.Gold -= cost;
		Save();
		return true;
	}

	/// <summary>变化卡牌（§6.3.1）：删 1 张 + 从该槽位角色卡池里**同等级**取 1 张补回（新卡永久升级级数 0）。</summary>
	private bool ChangeSlotCard(int slotIndex, int deckIndex, out string error)
	{
		error = string.Empty;
		List<RunDeckEntry> deck = GetSlotDeck(slotIndex);
		RunDeckEntry source = deck != null && deckIndex >= 0 && deckIndex < deck.Count ? deck[deckIndex] : null;
		if (source == null)
		{
			error = DeckOps.IndexOutOfRangeText;
			return false;
		}

		CardTier tier = TierOfPoolCard(slotIndex, source.CardId);
		List<MerchantCardCandidate> matches = SameTierReplacements(slotIndex, tier);
		if (matches.Count == 0)
		{
			error = DeckOps.NoSameTierText;
			return false;
		}

		// 随机源绑「本局种子 × 天数 × 槽位 × 卡组张数」：同一次操作可复现，卡组变了就会换一张。
		Random random = VillagePlaceData.MerchantRandom(Current,
			unchecked(0x4348 ^ (slotIndex * 17) ^ (deck.Count * 7) ^ (deckIndex * 3)));
		MerchantCardCandidate picked = matches[random.Next(matches.Count)];
		if (!TryRemoveCardFromSlotDeck(slotIndex, deckIndex, out error))
		{
			return false;
		}

		AddCardToSlotDeck(slotIndex, picked.CardId, 0);
		return true;
	}

	/// <summary>该槽位能否做「变化」：卡池里存在同等级的卡（源卡没有等级数据时按池中其余卡判）。</summary>
	private bool HasSameTierReplacement(int slotIndex, int deckIndex)
	{
		List<RunDeckEntry> deck = GetSlotDeck(slotIndex);
		RunDeckEntry source = deck != null && deckIndex >= 0 && deckIndex < deck.Count ? deck[deckIndex] : null;
		return source != null && SameTierReplacements(slotIndex, TierOfPoolCard(slotIndex, source.CardId)).Count > 0;
	}

	/// <summary>同等级候选：卡池里的非状态牌；源卡没有等级数据（`None`）时按「不过滤等级」取池中全部非状态牌。</summary>
	private List<MerchantCardCandidate> SameTierReplacements(int slotIndex, CardTier tier)
	{
		List<MerchantCardCandidate> matches = new List<MerchantCardCandidate>();
		foreach (MerchantCardCandidate candidate in VillagePlaceData.MerchantSlotCardPool(Current, slotIndex))
		{
			if (candidate == null || candidate.IsState)
			{
				continue;
			}

			if (tier == CardTier.None || candidate.Tier == tier)
			{
				matches.Add(candidate);
			}
		}

		return matches;
	}

	/// <summary>某张卡在该槽位角色卡池里的等级（池里查不到 = `None`，按「不过滤等级」处理）。</summary>
	private CardTier TierOfPoolCard(int slotIndex, int cardId)
	{
		foreach (MerchantCardCandidate candidate in VillagePlaceData.MerchantSlotCardPool(Current, slotIndex))
		{
			if (candidate != null && candidate.CardId == cardId)
			{
				return candidate.Tier;
			}
		}

		return CardTier.None;
	}
}
