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
using System.Collections.Generic;

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

		// 部位装备（Armor.csv 的头 / 身 / 脚 / 饰品）不占手位 —— 落点给「部位不符」，
		// 与「部位格只接部位装备」的反向判定对称（案 §三 的表格两行）。
		if (ItemNameResolver.IsBodySlotEquipment(definitionId))
		{
			error = HandAcceptsOnlyWeaponError;
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

	// ── 部位格（装备系统交互案 §二 / §三；2026-10-02 装备界面批）─────────────────────────
	// 头 / 身 / 脚各 1 格；饰品 N 格（`EquipmentConfig.csv` 的 `Global` 行，当前 3；改配置，界面随之加减格位）。
	// 与手位的分工：手位住 Weapon.csv 的武器 / 手位防具，部位格住 Armor.csv 的部位装备 ——
	// 两侧互不放行（落点一律给「部位不符」），因此界面不必各写一份白名单。

	/// <summary>部位种类数（头部 / 身体 / 脚部 / 饰品 = `EquipmentSlotKind` 的取值个数）。</summary>
	public const int BodySlotKindCount = 4;

	/// <summary>固定部位格数（头 + 身 + 脚，各 1 格）。</summary>
	public const int FixedBodySlotCount = 3;

	/// <summary>饰品格数（配置读数口）。</summary>
	public static int AccessorySlotCount => ItemNameResolver.AccessorySlotCount;

	/// <summary>部位格总数 = 3 + 饰品格数（界面按它铺格）。</summary>
	public static int BodySlotTotalCount => FixedBodySlotCount + AccessorySlotCount;

	public const string BodySlotMismatchError = "部位不符：这件装备不能放在这个部位。";
	public const string HandAcceptsOnlyWeaponError = "部位不符：手位只放武器 / 手位防具（头 / 身 / 脚 / 饰品戴在部位格）。";
	public const string AccessoryRowFullError = "饰品栏已满（数量按配置），先卸下一件再装。";
	public const string EmptyBodySlotError = "该部位格是空的。";
	public const string SameBodySlotError = "同一格，无需移动。";
	public const string UnknownBodyEquipmentError = "部位格上的装备不在装备表里，无法放回背包。";

	/// <summary>部位是否合法（0..3）。</summary>
	public static bool IsValidSlotKind(int slotKind) => slotKind is >= 0 and < BodySlotKindCount;

	/// <summary>该部位有几格（饰品按配置；头 / 身 / 脚恒 1）。</summary>
	public static int SlotCountOf(int slotKind) => slotKind == (int)EquipmentSlotKind.Accessory
		? AccessorySlotCount
		: IsValidSlotKind(slotKind) ? 1 : 0;

	/// <summary>格位是否合法（部位 + 格序）。</summary>
	public static bool IsValidSlotIndex(int slotKind, int indexInKind) =>
		indexInKind >= 0 && indexInKind < SlotCountOf(slotKind);

	/// <summary>部位文案（界面口径：头部 / 身体 / 脚部 / 饰品；与玩法文档的头盔 / 护甲 / 鞋是同一部位）。</summary>
	public static string SlotKindLabel(int slotKind) => slotKind switch
	{
		(int)EquipmentSlotKind.Head => "头部",
		(int)EquipmentSlotKind.Body => "身体",
		(int)EquipmentSlotKind.Feet => "脚部",
		(int)EquipmentSlotKind.Accessory => "饰品",
		_ => "部位",
	};

	/// <summary>格标题（案 §二）：单格部位 = `头部` / `身体` / `脚部`；饰品带编号 = `饰品1` …</summary>
	public static string SlotLabel(int slotKind, int indexInKind) =>
		slotKind == (int)EquipmentSlotKind.Accessory
			? $"{SlotKindLabel(slotKind)}{indexInKind + 1}"
			: SlotKindLabel(slotKind);

	/// <summary>手位文案（读档日志 / 提示行用）。</summary>
	public static string HandLabel(int hand) => hand == LeftHand ? "左手" : "右手";

	/// <summary>部位格上的装备定义名（按角色槽对象取；非法格 / 空位 = 空串）。</summary>
	public static string BodySlotDefinitionOf(RunCharacterSlotSave slot, int slotKind, int indexInKind)
	{
		if (slot == null || !IsValidSlotIndex(slotKind, indexInKind))
		{
			return string.Empty;
		}

		if (slotKind == (int)EquipmentSlotKind.Head)
		{
			return slot.EquippedHelmetDefinitionId ?? string.Empty;
		}

		if (slotKind == (int)EquipmentSlotKind.Body)
		{
			return slot.EquippedArmorDefinitionId ?? string.Empty;
		}

		if (slotKind == (int)EquipmentSlotKind.Feet)
		{
			return slot.EquippedBootsDefinitionId ?? string.Empty;
		}

		List<string> accessories = slot.EquippedAccessoryDefinitionIds;
		return accessories != null && indexInKind < accessories.Count ? accessories[indexInKind] ?? string.Empty : string.Empty;
	}

	/// <summary>部位格上的装备定义名（非法格 / 空位 = 空串）。</summary>
	public static string BodySlotDefinition(RunSaveData run, int slotIndex, int slotKind, int indexInKind)
	{
		if (!TryGetSlot(run, slotIndex, out RunCharacterSlotSave slot))
		{
			return string.Empty;
		}

		return BodySlotDefinitionOf(slot, slotKind, indexInKind);
	}

	/// <summary>部位格显示文案：空串 → `空`，否则装备名（案 §九 第 6 条）。</summary>
	public static string BodySlotText(RunSaveData run, int slotIndex, int slotKind, int indexInKind)
	{
		string definitionId = BodySlotDefinition(run, slotIndex, slotKind, indexInKind);
		return string.IsNullOrWhiteSpace(definitionId) ? EmptyHandText : definitionId;
	}

	/// <summary>写一个部位格 —— **唯一写入口**（饰品列表按需补齐到配置格数）。</summary>
	public static void SetBodySlot(RunCharacterSlotSave slot, int slotKind, int indexInKind, string definitionId)
	{
		if (slot == null || !IsValidSlotIndex(slotKind, indexInKind))
		{
			return;
		}

		string value = definitionId ?? string.Empty;
		if (slotKind == (int)EquipmentSlotKind.Head)
		{
			slot.EquippedHelmetDefinitionId = value;
			return;
		}

		if (slotKind == (int)EquipmentSlotKind.Body)
		{
			slot.EquippedArmorDefinitionId = value;
			return;
		}

		if (slotKind == (int)EquipmentSlotKind.Feet)
		{
			slot.EquippedBootsDefinitionId = value;
			return;
		}

		slot.EquippedAccessoryDefinitionIds ??= new List<string>();
		while (slot.EquippedAccessoryDefinitionIds.Count <= indexInKind)
		{
			slot.EquippedAccessoryDefinitionIds.Add(string.Empty);
		}

		slot.EquippedAccessoryDefinitionIds[indexInKind] = value;
	}

	/// <summary>部位格装备的单件负荷（卸下试算用）；未注册 = 0。</summary>
	public static float BodySlotLoad(string definitionId) =>
		string.IsNullOrWhiteSpace(definitionId) || !ItemNameResolver.TryGetEquipmentKey(definitionId, out int key)
			? 0f
			: ItemNameResolver.LoadOf(BagCategory.Equipment, key);

	/// <summary>饰品行是否已满（所有饰品格都被占）。</summary>
	public static bool AccessoryRowFull(RunSaveData run, int slotIndex)
	{
		for (int index = 0; index < AccessorySlotCount; index++)
		{
			if (string.IsNullOrWhiteSpace(BodySlotDefinition(run, slotIndex, (int)EquipmentSlotKind.Accessory, index)))
			{
				return false;
			}
		}

		return true;
	}

	/// <summary>该装备名能不能反查回表内 ID（空串 = 无需反查，直接算通过）。</summary>
	private static bool CanReturnBodyToBag(string definitionId) =>
		string.IsNullOrWhiteSpace(definitionId) || ItemNameResolver.TryGetEquipmentKey(definitionId, out _);

	/// <summary>把部位格上的装备放回背包（进背包装备分类）。</summary>
	private static void ReturnBodyToBag(RunSaveData run, string definitionId)
	{
		if (!string.IsNullOrWhiteSpace(definitionId) && ItemNameResolver.TryGetEquipmentKey(definitionId, out int key))
		{
			RunBagSystem.Add(run, BagCategory.Equipment, key, 1);
		}
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

	/// <summary>
	/// 补齐角色槽的部位格集合（饰品列表到配置格数）——**建档与读档迁移共用同一口径**，
	/// 否则「落档 → 读档迁移 → 再序列化」会因迁移补齐而不再逐字一致（`RunSessionReconstructionSmoke` 断死这条纪律）。
	/// </summary>
	public static void EnsureBodySlots(RunCharacterSlotSave slot)
	{
		if (slot == null)
		{
			return;
		}

		slot.EquippedAccessoryDefinitionIds ??= new List<string>();
		while (slot.EquippedAccessoryDefinitionIds.Count < AccessorySlotCount)
		{
			slot.EquippedAccessoryDefinitionIds.Add(string.Empty);
		}
	}

	/// <summary>
	/// 背包 → 部位格（案 §三 第一行）：只接 `Armor.csv` 的部位装备，且部位必须与格子一致（否则「部位不符」）。
	/// 目标格被占时**替换**（原装备回背包）；饰品行**全满**时拒绝并给「饰品栏已满」（案 §三 的四类原因之一）。
	/// 装备搬出背包后不再占背包负荷（背包交互 §四），因此这里不做负荷试算。
	/// </summary>
	public static bool TryEquipSlotFromBag(RunSaveData run, string instanceId, int slotIndex, int slotKind, int indexInKind, out string error)
	{
		error = string.Empty;
		if (run == null)
		{
			error = "没有进行中的本局。";
			return false;
		}

		if (!TryGetSlot(run, slotIndex, out RunCharacterSlotSave slot) || !IsValidSlotIndex(slotKind, indexInKind))
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
			error = BodySlotMismatchError;
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

		if (!ItemNameResolver.TryGetBodySlotOfDefinition(definitionId, out EquipmentSlotKind actualSlot) || (int)actualSlot != slotKind)
		{
			error = BodySlotMismatchError;
			return false;
		}

		if (slotKind == (int)EquipmentSlotKind.Accessory && AccessoryRowFull(run, slotIndex))
		{
			error = AccessoryRowFullError;
			return false;
		}

		string replaced = BodySlotDefinition(run, slotIndex, slotKind, indexInKind);
		if (!CanReturnBodyToBag(replaced))
		{
			error = UnknownBodyEquipmentError;
			return false;
		}

		if (!RunBagSystem.TakeOneByDefinition(run, BagCategory.Equipment, entry.DefinitionKey, out _))
		{
			error = "背包里没有该装备。";
			return false;
		}

		ReturnBodyToBag(run, replaced);
		SetBodySlot(slot, slotKind, indexInKind, definitionId);
		return true;
	}

	/// <summary>
	/// 部位格 → 背包（案 §三 第二行）：卸下并进背包装备分类，**局外不落地丢弃**。
	/// 进背包后负荷上升，超限时整笔拒绝并给出具体数字（背包交互 §四）。
	/// </summary>
	public static bool TryUnequipSlot(RunSaveData run, int slotIndex, int slotKind, int indexInKind, out string error)
	{
		error = string.Empty;
		if (!TryGetSlot(run, slotIndex, out RunCharacterSlotSave slot) || !IsValidSlotIndex(slotKind, indexInKind))
		{
			error = InvalidSlotError;
			return false;
		}

		string definitionId = BodySlotDefinition(run, slotIndex, slotKind, indexInKind);
		if (string.IsNullOrWhiteSpace(definitionId))
		{
			error = EmptyBodySlotError;
			return false;
		}

		if (!ItemNameResolver.TryGetEquipmentKey(definitionId, out int key))
		{
			error = UnknownBodyEquipmentError;
			return false;
		}

		float load = ItemNameResolver.LoadOf(BagCategory.Equipment, key);
		if (RunBagSystem.WouldExceedLoad(run, load))
		{
			error = RunBagSystem.DescribeLoadReject(run, load);
			return false;
		}

		RunBagSystem.Add(run, BagCategory.Equipment, key, 1);
		SetBodySlot(slot, slotKind, indexInKind, string.Empty);
		return true;
	}

	/// <summary>
	/// 部位格 ↔ 部位格（案 §三 第三行）：**只有饰品之间可互换**；同一部位只有一格的（头 / 身 / 脚）拖到自身格无操作。
	/// 部位不同 = 「部位不符」（案 §三 第四行「部位格 → 手位或反向」的反向口径同样落在这里）。
	/// </summary>
	public static bool TrySwapBodySlots(RunSaveData run, int slotIndex, int fromKind, int fromIndex, int toKind, int toIndex, out string error)
	{
		error = string.Empty;
		if (!TryGetSlot(run, slotIndex, out RunCharacterSlotSave slot)
			|| !IsValidSlotIndex(fromKind, fromIndex) || !IsValidSlotIndex(toKind, toIndex))
		{
			error = InvalidSlotError;
			return false;
		}

		if (fromKind != toKind)
		{
			error = BodySlotMismatchError;
			return false;
		}

		if (fromIndex == toIndex)
		{
			error = SameBodySlotError;
			return false;
		}

		if (fromKind != (int)EquipmentSlotKind.Accessory)
		{
			error = BodySlotMismatchError; // 头 / 身 / 脚各一格：没有「另一格」可换
			return false;
		}

		string from = BodySlotDefinition(run, slotIndex, fromKind, fromIndex);
		string to = BodySlotDefinition(run, slotIndex, toKind, toIndex);
		if (string.IsNullOrWhiteSpace(from) && string.IsNullOrWhiteSpace(to))
		{
			error = EmptyBodySlotError;
			return false;
		}

		SetBodySlot(slot, fromKind, fromIndex, to);
		SetBodySlot(slot, toKind, toIndex, from);
		return true;
	}

	/// <summary>
	/// 读档清洗（案 §九 第 4 条的默认值「警告 + 清空该格 + 打印日志」，取代原先未知装备直接抛错、建场失败的形态）：
	/// 装备表里不存在的手位 / 部位装备、以及部位与格子对不上的（手改档 / 配表改名后的残留）一律清空该格，
	/// 返回逐条说明供调用方打日志。**合法字段一律不动**，幂等。
	/// </summary>
	public static List<string> SanitizeEquipment(RunSaveData run)
	{
		List<string> cleared = new List<string>();
		if (run?.CharacterSlots == null)
		{
			return cleared;
		}

		for (int slotIndex = 0; slotIndex < run.CharacterSlots.Count; slotIndex++)
		{
			if (!TryGetSlot(run, slotIndex, out RunCharacterSlotSave slot))
			{
				continue;
			}

			for (int hand = LeftHand; hand <= RightHand; hand++)
			{
				string definitionId = HandDefinition(run, slotIndex, hand);
				if (string.IsNullOrWhiteSpace(definitionId))
				{
					continue;
				}

				if (ItemNameResolver.TryGetEquipmentKey(definitionId, out _) && !ItemNameResolver.IsBodySlotEquipment(definitionId))
				{
					continue;
				}

				cleared.Add($"角色槽 {slotIndex + 1} 的{HandLabel(hand)}装备 `{definitionId}` 不在武器表里，已清空该格。");
				SetHand(slot, hand, string.Empty);
			}

			for (int kind = 0; kind < BodySlotKindCount; kind++)
			{
				for (int index = 0; index < SlotCountOf(kind); index++)
				{
					string definitionId = BodySlotDefinition(run, slotIndex, kind, index);
					if (string.IsNullOrWhiteSpace(definitionId))
					{
						continue;
					}

					bool known = ItemNameResolver.TryGetBodySlotOfDefinition(definitionId, out EquipmentSlotKind actual)
						&& (int)actual == kind;
					if (known)
					{
						continue;
					}

					cleared.Add($"角色槽 {slotIndex + 1} 的{SlotLabel(kind, index)}装备 `{definitionId}` 不在部位装备表里 / 与部位不符，已清空该格。");
					SetBodySlot(slot, kind, index, string.Empty);
				}
			}
		}

		return cleared;
	}
}
