// RunEquipmentSystem.cs
// 局外**手位**（左右手）的读写与换取规则（装备系统交互案 §三 / §五、六边形战场玩法 §5.2）。
//
// 分工：背包条目（`BagEntries`）归 `RunBagSystem`，手位归这里；「背包 ↔ 手位」的搬运由这里发起
// （装到手位 = 从背包搬出；卸下 = 搬回背包，**局外不落地丢弃**）。
//
// 纯逻辑、无 Godot 依赖：手位字段是装备**定义名串**（`LeftHandDefinitionId` / `RightHandDefinitionId`），
// 手数与单件负荷从 `ItemNameResolver` 的注册表取 —— 因此 xUnit 里喂假注册表即可断言全部规则。
// 合法组合（玩法 §5.2）：单手各占一槽、双手独占两槽；双手装备与已有手位装备冲突时一律**拒绝并给原因**，
// 不替玩家自动卸装。
using System;

public static class RunEquipmentSystem
{
	public const int LeftHand = 0;
	public const int RightHand = 1;
	public const int HandCount = 2;

	/// <summary>空手位文案（装备系统交互案 §九 第 6 条：空位显示 `空`）。</summary>
	public const string EmptyHandText = "空";

	public const string SlotMismatchError = "部位不符：只有武器 / 手位防具能放到手位。";
	public const string BothSlotsRequiredError = "双手装备需两个空手位（先卸下另一手）。";
	public const string TwoHandedSwapError = "双手装备不能与其它手位装备互换（先卸下）。";
	public const string WeaponInItemSlotError = "该装备不可放入道具栏（需装备效果开放许可）。";
	public const string EmptyHandError = "该手位是空的。";
	public const string SameHandError = "同一手位，无需移动。";
	public const string UnknownEquipmentError = "手位上的装备不在装备表里，无法放回背包。";
	public const string EquippedButCarriedError = "该装备在随身格上，先取回背包再装备。";
	public const string InvalidSlotError = "角色槽位或手位非法。";

	public static bool IsValidHand(int hand) => hand is >= LeftHand and <= RightHand;

	/// <summary>取角色槽（越界 / 空槽返回 false）。</summary>
	public static bool TryGetSlot(RunSaveData run, int slotIndex, out RunCharacterSlotSave slot)
	{
		slot = null;
		if (run?.CharacterSlots == null || slotIndex < 0 || slotIndex >= run.CharacterSlots.Count)
		{
			return false;
		}

		slot = run.CharacterSlots[slotIndex];
		return slot != null;
	}

	/// <summary>手位上的装备定义名（空格 / 越界 = 空串）。</summary>
	public static string HandDefinition(RunSaveData run, int slotIndex, int hand)
	{
		if (!TryGetSlot(run, slotIndex, out RunCharacterSlotSave slot) || !IsValidHand(hand))
		{
			return string.Empty;
		}

		return (hand == LeftHand ? slot.LeftHandDefinitionId : slot.RightHandDefinitionId) ?? string.Empty;
	}

	/// <summary>手位显示文案：空串 → `空`，否则装备名。</summary>
	public static string HandText(RunSaveData run, int slotIndex, int hand)
	{
		string definitionId = HandDefinition(run, slotIndex, hand);
		return string.IsNullOrWhiteSpace(definitionId) ? EmptyHandText : definitionId;
	}

	/// <summary>写一个手位 —— **唯一写入口**（写完同步旧字段 `EquippedWeaponDefinitionId` 的左手镜像）。</summary>
	public static void SetHand(RunCharacterSlotSave slot, int hand, string definitionId)
	{
		if (slot == null || !IsValidHand(hand))
		{
			return;
		}

		string value = definitionId ?? string.Empty;
		if (hand == LeftHand)
		{
			slot.LeftHandDefinitionId = value;
		}
		else
		{
			slot.RightHandDefinitionId = value;
		}

		slot.SyncLegacyWeaponField();
	}

	/// <summary>是否双手装备（`HandsRequired == 2`）：未注册的装备名按单手处理。</summary>
	public static bool IsTwoHandedDefinition(string definitionId) =>
		ItemNameResolver.HandsRequiredOfDefinition(definitionId) >= 2;

	/// <summary>
	/// 「武器 / 护具进道具栏」的**预留许可通道**（玩法 §5.2 末尾、背包系统交互案 §三）：默认全部为 false，
	/// 许可由装备效果开放；通道先接通，开放时不需要再改交互与落点判定。
	/// </summary>
	public static bool AllowsWeaponInItemSlots(string definitionId) => false;

	/// <summary>该手位的装备单件负荷（手位本身不占背包负荷，这里只用于「卸下会不会超载」的试算）。</summary>
	public static float HandLoad(string definitionId) =>
		string.IsNullOrWhiteSpace(definitionId) || !ItemNameResolver.TryGetEquipmentKey(definitionId, out int key)
			? 0f
			: ItemNameResolver.LoadOf(BagCategory.Equipment, key);

	/// <summary>
	/// 背包 → 手位（背包系统交互案 §三「背包 → 左手位 / 右手位」）：
	/// 只接装备类目；双手装备要求**两槽同时让位**；被换下的单手装备回背包。
	/// 装备搬出手位后不再占背包负荷（§四），因此这里不做负荷试算。
	/// </summary>
	public static bool TryEquipFromBag(RunSaveData run, string instanceId, int slotIndex, int hand, out string error)
	{
		error = string.Empty;
		if (run == null)
		{
			error = "没有进行中的本局。";
			return false;
		}

		if (!TryGetSlot(run, slotIndex, out RunCharacterSlotSave slot) || !IsValidHand(hand))
		{
			error = InvalidSlotError;
			return false;
		}

		RunBagEntrySave entry = RunBagSystem.Find(run, instanceId);
		if (entry == null)
		{
			error = "背包里没有该物品。";
			return false;
		}

		if (entry.CategoryEnum != BagCategory.Equipment)
		{
			error = SlotMismatchError;
			return false;
		}

		if (!entry.IsInBag)
		{
			error = EquippedButCarriedError;
			return false;
		}

		string definitionId = string.IsNullOrWhiteSpace(entry.DefinitionId)
			? ItemNameResolver.NameOf(BagCategory.Equipment, entry.DefinitionKey)
			: entry.DefinitionId;
		if (string.IsNullOrWhiteSpace(definitionId))
		{
			error = $"未定义装备({entry.DefinitionKey})，不能装备。";
			return false;
		}

		int otherHand = hand == LeftHand ? RightHand : LeftHand;
		string targetOld = HandDefinition(run, slotIndex, hand);
		string otherOld = HandDefinition(run, slotIndex, otherHand);
		bool twoHanded = IsTwoHandedDefinition(definitionId);

		if (twoHanded)
		{
			// 双手装备独占两槽：任一槽有装备都要先卸下（交互案 §三「双手装备需两槽同时让位」）。
			if (!string.IsNullOrWhiteSpace(targetOld) || !string.IsNullOrWhiteSpace(otherOld))
			{
				error = BothSlotsRequiredError;
				return false;
			}
		}
		else if (IsTwoHandedDefinition(otherOld))
		{
			error = TwoHandedSwapError;
			return false;
		}

		// 被换下的装备要能回背包（名字必须能反查回表内 ID）：先校验，再改动状态。
		if (!CanReturnHandToBag(targetOld) || !CanReturnHandToBag(otherOld))
		{
			error = UnknownEquipmentError;
			return false;
		}

		if (!RunBagSystem.TakeOneByDefinition(run, BagCategory.Equipment, entry.DefinitionKey, out _))
		{
			error = "背包里没有该装备。";
			return false;
		}

		ReturnHandToBag(run, targetOld);
		if (twoHanded)
		{
			SetHand(slot, LeftHand, definitionId);
			SetHand(slot, RightHand, definitionId);
		}
		else
		{
			SetHand(slot, hand, definitionId);
		}

		return true;
	}

	/// <summary>
	/// 手位 → 背包（交互案 §三「手位 → 背包」）：卸下并进背包装备分类，**局外不落地丢弃**。
	/// 双手装备一次卸下两槽；进背包后负荷上升，超限时整笔拒绝并给出具体数字。
	/// </summary>
	public static bool TryUnequipHand(RunSaveData run, int slotIndex, int hand, out string error)
	{
		error = string.Empty;
		if (!TryGetSlot(run, slotIndex, out RunCharacterSlotSave slot) || !IsValidHand(hand))
		{
			error = InvalidSlotError;
			return false;
		}

		string definitionId = HandDefinition(run, slotIndex, hand);
		if (string.IsNullOrWhiteSpace(definitionId))
		{
			error = EmptyHandError;
			return false;
		}

		if (!ItemNameResolver.TryGetEquipmentKey(definitionId, out int key))
		{
			error = UnknownEquipmentError;
			return false;
		}

		float load = ItemNameResolver.LoadOf(BagCategory.Equipment, key);
		if (RunBagSystem.WouldExceedLoad(run, load))
		{
			error = RunBagSystem.DescribeLoadReject(run, load);
			return false;
		}

		RunBagSystem.Add(run, BagCategory.Equipment, key, 1);
		if (IsTwoHandedDefinition(definitionId))
		{
			SetHand(slot, LeftHand, string.Empty);
			SetHand(slot, RightHand, string.Empty);
		}
		else
		{
			SetHand(slot, hand, string.Empty);
		}

		return true;
	}

	/// <summary>手位 → 另一手位（交互案 §三「手位 → 另一手位」）：交换；双手装备不参与互换（先卸下）。</summary>
	public static bool TryMoveHandToHand(RunSaveData run, int slotIndex, int fromHand, int toHand, out string error)
	{
		error = string.Empty;
		if (!TryGetSlot(run, slotIndex, out RunCharacterSlotSave slot) || !IsValidHand(fromHand) || !IsValidHand(toHand))
		{
			error = InvalidSlotError;
			return false;
		}

		if (fromHand == toHand)
		{
			error = SameHandError;
			return false;
		}

		string from = HandDefinition(run, slotIndex, fromHand);
		string to = HandDefinition(run, slotIndex, toHand);
		if (string.IsNullOrWhiteSpace(from) && string.IsNullOrWhiteSpace(to))
		{
			error = EmptyHandError;
			return false;
		}

		if (IsTwoHandedDefinition(from) || IsTwoHandedDefinition(to))
		{
			error = TwoHandedSwapError;
			return false;
		}

		SetHand(slot, fromHand, to);
		SetHand(slot, toHand, from);
		return true;
	}

	/// <summary>
	/// 手位 → 随身道具格（交互案 §三 的**保留通道**）：默认拒绝并给出许可原因；
	/// `AllowsWeaponInItemSlots` 为真时合法，落点行为与「背包 → 随身格」一致（先进背包再占格）。
	/// </summary>
	public static bool TryMoveHandToCarrySlot(RunSaveData run, int slotIndex, int hand, int carrySlot, out string error)
	{
		error = string.Empty;
		if (!TryGetSlot(run, slotIndex, out RunCharacterSlotSave slot) || !IsValidHand(hand))
		{
			error = InvalidSlotError;
			return false;
		}

		string definitionId = HandDefinition(run, slotIndex, hand);
		if (string.IsNullOrWhiteSpace(definitionId))
		{
			error = EmptyHandError;
			return false;
		}

		if (!AllowsWeaponInItemSlots(definitionId))
		{
			error = WeaponInItemSlotError;
			return false;
		}

		if (!ItemNameResolver.TryGetEquipmentKey(definitionId, out int key))
		{
			error = UnknownEquipmentError;
			return false;
		}

		RunBagEntrySave added = RunBagSystem.Add(run, BagCategory.Equipment, key, 1);
		if (!RunBagSystem.TrySetCarrySlot(run, carrySlot, added?.InstanceId, out error))
		{
			return false;
		}

		if (IsTwoHandedDefinition(definitionId))
		{
			SetHand(slot, LeftHand, string.Empty);
			SetHand(slot, RightHand, string.Empty);
		}
		else
		{
			SetHand(slot, hand, string.Empty);
		}

		return true;
	}

	/// <summary>该手位上的装备名能不能反查回表内 ID（空串 = 无需反查，直接算通过）。</summary>
	private static bool CanReturnHandToBag(string definitionId) =>
		string.IsNullOrWhiteSpace(definitionId) || ItemNameResolver.TryGetEquipmentKey(definitionId, out _);

	/// <summary>把手位上的装备放回背包（进背包装备分类）。</summary>
	private static void ReturnHandToBag(RunSaveData run, string definitionId)
	{
		if (!string.IsNullOrWhiteSpace(definitionId) && ItemNameResolver.TryGetEquipmentKey(definitionId, out int key))
		{
			RunBagSystem.Add(run, BagCategory.Equipment, key, 1);
		}
	}
}
