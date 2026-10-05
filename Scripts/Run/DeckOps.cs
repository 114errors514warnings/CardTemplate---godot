// DeckOps.cs
// 卡组操作（商人「卡牌相关操作」四项：删除 / 变化 / 转移 / 升级）的**纯逻辑校验与计价**（无 Godot 依赖，可 xUnit 直测）。
// 口径出处：README/施工文档/2026/2026.10/交互/商人交互案.md §4.8 / §5.2 / §6.3（含失败原因文案）。
// 分工：规则与文案只住这里；`RunSession` 只做「校验通过 → 改档 → Save()」，界面只读这里的返回值。
using System;

public static class DeckOps
{
	/// <summary>永久升级级数上限（商人交互案 §十 第 16 条：每张卡永久升级 ≤ +3）。</summary>
	public const int MaxPermanentUpgradeLevel = 3;

	/// <summary>卡组至少保留的张数（删除 / 转移的下限，§6.3 失败原因表）。</summary>
	public const int MinDeckSize = 1;

	/// <summary>删除卡牌价（§5.2）。</summary>
	public const int RemoveGold = 75;
	/// <summary>变化卡牌价（§5.2）。</summary>
	public const int ChangeGold = 100;
	/// <summary>转移卡牌价（§5.2）。</summary>
	public const int TransferGold = 60;
	/// <summary>升级卡牌的**每级**价（实际 = 该常量 ×（当前永久级数 + 1），§5.2）。</summary>
	public const int UpgradeGoldPerLevel = 80;

	/// <summary>升级一次的费用：80 / 160 / 240（§5.2）。</summary>
	public static int UpgradeGold(int currentPermanentUpgradeLevel) =>
		UpgradeGoldPerLevel * (Math.Max(0, currentPermanentUpgradeLevel) + 1);

	/// <summary>金币不足的统一文案（§6.3 失败原因表）；界面与烟测共用同一处。</summary>
	public static string GoldShortText(int need, int have) => $"金币不足：需要 {need}，当前 {have}。";

	/// <summary>删除 / 转移的源卡校验：卡组至少保留 1 张 + 下标必须在卡组内。</summary>
	public static bool CanRemoveCard(int deckCount, int deckIndex, out string error)
	{
		error = string.Empty;
		if (deckCount <= MinDeckSize)
		{
			error = "卡组至少保留 1 张卡。";
			return false;
		}

		if (deckIndex < 0 || deckIndex >= deckCount)
		{
			error = "该卡不在卡组中（下标越界）。";
			return false;
		}

		return true;
	}

	/// <summary>升级校验：未到永久升级上限。</summary>
	public static bool CanUpgradeCard(int permanentUpgradeLevel, out string error)
	{
		error = string.Empty;
		if (permanentUpgradeLevel >= MaxPermanentUpgradeLevel)
		{
			error = $"该卡已达永久升级上限（+{MaxPermanentUpgradeLevel}）。";
			return false;
		}

		return true;
	}

	/// <summary>
	/// 转移校验：源卡可移除（卡组 ≥ 2 + 下标合法）+ 目标槽位合法 + 目标卡组非空。
	/// 口径（§十 第 17 条默认值）：**允许**角色专属牌进别人的卡组 —— 卡组是实例列表，不做卡池校验。
	/// </summary>
	public static bool CanTransferCard(int sourceDeckCount, int deckIndex, int slotCount, int targetSlot, int targetDeckCount, out string error)
	{
		if (!CanRemoveCard(sourceDeckCount, deckIndex, out error))
		{
			return false;
		}

		if (targetSlot < 0 || targetSlot >= slotCount)
		{
			error = "该角色的卡组当前不可写入。";
			return false;
		}

		if (targetDeckCount <= 0)
		{
			error = "目标卡组为空，无法接收。";
			return false;
		}

		return true;
	}
}
