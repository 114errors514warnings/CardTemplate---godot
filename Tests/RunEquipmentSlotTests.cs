// RunEquipmentSlotTests.cs
// **部位装备**（P0-18 界面半，2026-10-02 装备界面批）纯逻辑单测：
//   背包 → 部位格（部位校验 / 占用替换 / 饰品满）、部位格 → 背包（卸下与负荷试算）、饰品互换、
//   手位与部位格的互不放行、饰品格数配置、`v5 → v6` 迁移（饰品列表补齐、幂等）、读档清洗（未知 / 部位不符清空）。
using System.Collections.Generic;
using System.Linq;
using Xunit;

[Collection("ItemNameResolverState")]
public class RunEquipmentSlotTests
{
	/// <summary>注册假装备表：武器（手位）+ 部位装备（头 / 身 / 脚 / 饰品），并把饰品格数设为 3。</summary>
	private static void RegisterStubTables()
	{
		ItemNameResolver.Clear();
		ItemNameResolver.RegisterWeapons(new Dictionary<int, WeaponDefinition>
		{
			[10001] = new WeaponDefinition { WeaponId = 10001, DefinitionId = "行军短剑", HandsRequired = 1 },
			[10003] = new WeaponDefinition { WeaponId = 10003, DefinitionId = "长枪", HandsRequired = 2 },
		});
		ItemNameResolver.RegisterArmors(new Dictionary<int, ArmorDefinition>
		{
			[20001] = new ArmorDefinition { ArmorId = 20001, DefinitionId = "布头巾", Slot = EquipmentSlotKind.Head, Load = 1f, DefenseValue = 1 },
			[20002] = new ArmorDefinition { ArmorId = 20002, DefinitionId = "皮甲", Slot = EquipmentSlotKind.Body, Load = 2f, DefenseValue = 2 },
			[20003] = new ArmorDefinition { ArmorId = 20003, DefinitionId = "旅人靴", Slot = EquipmentSlotKind.Feet, Load = 1f, MoveBonus = 1 },
			[20004] = new ArmorDefinition { ArmorId = 20004, DefinitionId = "护身符", Slot = EquipmentSlotKind.Accessory, Load = 0.5f },
			[20005] = new ArmorDefinition { ArmorId = 20005, DefinitionId = "青铜戒指", Slot = EquipmentSlotKind.Accessory, Load = 0.5f },
			[20006] = new ArmorDefinition { ArmorId = 20006, DefinitionId = "猎人护腕", Slot = EquipmentSlotKind.Accessory, Load = 0.5f },
		});
		ItemNameResolver.SetAccessorySlotCount(3);
	}

	/// <summary>一名角色的最小局（走一次迁移，把饰品列表补齐到配置格数）。</summary>
	private static RunSaveData NewRun()
	{
		RunSaveData run = new RunSaveData();
		run.CharacterSlots.Add(new RunCharacterSlotSave { CharacterId = 1002, CurrentHp = 30, MaxHp = 30 });
		run.DeckSlots.Add(new List<RunDeckEntry>());
		RunBagSystem.EnsureCollections(run);
		run.MigrateToCurrentSchema();
		return run;
	}

	private const int Head = (int)EquipmentSlotKind.Head;
	private const int Body = (int)EquipmentSlotKind.Body;
	private const int Feet = (int)EquipmentSlotKind.Feet;
	private const int Accessory = (int)EquipmentSlotKind.Accessory;

	[Fact]
	public void EquipFromBag_ArmorGoesToItsSlot_AndLeavesBag()
	{
		RegisterStubTables();
		RunSaveData run = NewRun();
		RunBagEntrySave helmet = RunBagSystem.Add(run, BagCategory.Equipment, 20001, 1);
		float loadBefore = RunBagSystem.TotalLoad(run);

		Assert.True(RunEquipmentSystem.TryEquipSlotFromBag(run, helmet.InstanceId, 0, Head, 0, out string error), error);
		Assert.Equal("布头巾", RunEquipmentSystem.BodySlotDefinition(run, 0, Head, 0));
		Assert.Equal("布头巾", RunEquipmentSystem.BodySlotText(run, 0, Head, 0));
		// 装到部位格后不在背包里：条目消失、负荷下降（背包系统交互案 §四）。
		Assert.Null(RunBagSystem.Find(run, helmet.InstanceId));
		Assert.Equal(0, RunBagSystem.CountOf(run, BagCategory.Equipment, 20001));
		Assert.Equal(loadBefore - 1f, RunBagSystem.TotalLoad(run), 3);

		// 卸下 → 回背包、负荷回升（局外不落地丢弃）
		Assert.True(RunEquipmentSystem.TryUnequipSlot(run, 0, Head, 0, out error), error);
		Assert.Equal(string.Empty, RunEquipmentSystem.BodySlotDefinition(run, 0, Head, 0));
		Assert.Equal("空", RunEquipmentSystem.BodySlotText(run, 0, Head, 0));
		Assert.Equal(1, RunBagSystem.CountOf(run, BagCategory.Equipment, 20001));
		Assert.Equal(loadBefore, RunBagSystem.TotalLoad(run), 3);
	}

	[Fact]
	public void EquipFromBag_WrongSlotOrWeapon_RejectedWithMismatchReason()
	{
		RegisterStubTables();
		RunSaveData run = NewRun();
		RunBagEntrySave helmet = RunBagSystem.Add(run, BagCategory.Equipment, 20001, 1); // 头部
		RunBagEntrySave sword = RunBagSystem.Add(run, BagCategory.Equipment, 10001, 1);  // 手位武器

		// 头部装备拖到身体格：部位不符
		Assert.False(RunEquipmentSystem.TryEquipSlotFromBag(run, helmet.InstanceId, 0, Body, 0, out string error));
		Assert.Equal(RunEquipmentSystem.BodySlotMismatchError, error);
		Assert.Equal(string.Empty, RunEquipmentSystem.BodySlotDefinition(run, 0, Body, 0));

		// 武器拖到部位格：部位不符（武器不是 Armor.csv 的行）
		Assert.False(RunEquipmentSystem.TryEquipSlotFromBag(run, sword.InstanceId, 0, Head, 0, out error));
		Assert.Equal(RunEquipmentSystem.BodySlotMismatchError, error);

		// 两件都还在背包里（拒绝不动状态）
		Assert.Equal(2, run.BagEntries.Count(x => x.CategoryEnum == BagCategory.Equipment));
	}

	[Fact]
	public void EquipFromBag_ArmorToHand_Rejected_WhileWeaponStillWorks()
	{
		RegisterStubTables();
		RunSaveData run = NewRun();
		RunBagEntrySave helmet = RunBagSystem.Add(run, BagCategory.Equipment, 20001, 1);
		RunBagEntrySave sword = RunBagSystem.Add(run, BagCategory.Equipment, 10001, 1);

		// 部位装备不许进手位（案 §三「部位格 → 手位（或反向）不允许」）
		Assert.False(RunEquipmentSystem.TryEquipFromBag(run, helmet.InstanceId, 0, RunEquipmentSystem.LeftHand, out string error));
		Assert.Equal(RunEquipmentSystem.HandAcceptsOnlyWeaponError, error);

		// 手位装配的既有规则不受影响
		Assert.True(RunEquipmentSystem.TryEquipFromBag(run, sword.InstanceId, 0, RunEquipmentSystem.LeftHand, out error), error);
		Assert.Equal("行军短剑", RunEquipmentSystem.HandDefinition(run, 0, RunEquipmentSystem.LeftHand));
	}

	[Fact]
	public void EquipFromBag_OccupiedHeadSlot_ReplacesAndReturnsOldToBag()
	{
		RegisterStubTables();
		RunSaveData run = NewRun();
		RunBagEntrySave first = RunBagSystem.Add(run, BagCategory.Equipment, 20001, 1); // 布头巾
		Assert.True(RunEquipmentSystem.TryEquipSlotFromBag(run, first.InstanceId, 0, Head, 0, out string error), error);

		RunBagEntrySave second = RunBagSystem.Add(run, BagCategory.Equipment, 20001, 1);
		Assert.True(RunEquipmentSystem.TryEquipSlotFromBag(run, second.InstanceId, 0, Head, 0, out error), error);

		// 替换：新装备在位、旧装备回背包（局外不落地丢弃）
		Assert.Equal("布头巾", RunEquipmentSystem.BodySlotDefinition(run, 0, Head, 0));
		Assert.Equal(1, RunBagSystem.CountOf(run, BagCategory.Equipment, 20001));
	}

	[Fact]
	public void AccessoryRowFull_RejectsNewAccessory_UntilOneIsRemoved()
	{
		RegisterStubTables();
		RunSaveData run = NewRun();
		int[] accessoryKeys = { 20004, 20005, 20006 };
		string error;

		for (int i = 0; i < accessoryKeys.Length; i++)
		{
			RunBagEntrySave entry = RunBagSystem.Add(run, BagCategory.Equipment, accessoryKeys[i], 1);
			Assert.True(RunEquipmentSystem.TryEquipSlotFromBag(run, entry.InstanceId, 0, Accessory, i, out error), error);
		}

		Assert.True(RunEquipmentSystem.AccessoryRowFull(run, 0));

		// 饰品格满：新的饰品被拒绝，原因 = 案 §三 的第 3 类文案
		RunBagEntrySave extra = RunBagSystem.Add(run, BagCategory.Equipment, 20004, 1);
		Assert.False(RunEquipmentSystem.TryEquipSlotFromBag(run, extra.InstanceId, 0, Accessory, 0, out error));
		Assert.Equal(RunEquipmentSystem.AccessoryRowFullError, error);

		// 卸下一件后同一拖动合法（回到「不满」态）
		Assert.True(RunEquipmentSystem.TryUnequipSlot(run, 0, Accessory, 2, out error), error);
		Assert.False(RunEquipmentSystem.AccessoryRowFull(run, 0));
		Assert.True(RunEquipmentSystem.TryEquipSlotFromBag(run, extra.InstanceId, 0, Accessory, 0, out error), error);
		Assert.Equal("护身符", RunEquipmentSystem.BodySlotDefinition(run, 0, Accessory, 0));
	}

	[Fact]
	public void BodySlots_SwapOnlyWithinSameMultiSlotKind()
	{
		RegisterStubTables();
		RunSaveData run = NewRun();
		RunBagEntrySave ring = RunBagSystem.Add(run, BagCategory.Equipment, 20005, 1);
		RunBagEntrySave bracer = RunBagSystem.Add(run, BagCategory.Equipment, 20006, 1);
		Assert.True(RunEquipmentSystem.TryEquipSlotFromBag(run, ring.InstanceId, 0, Accessory, 0, out string error), error);
		Assert.True(RunEquipmentSystem.TryEquipSlotFromBag(run, bracer.InstanceId, 0, Accessory, 2, out error), error);

		// 饰品 ↔ 饰品：互换
		Assert.True(RunEquipmentSystem.TrySwapBodySlots(run, 0, Accessory, 0, Accessory, 2, out error), error);
		Assert.Equal("猎人护腕", RunEquipmentSystem.BodySlotDefinition(run, 0, Accessory, 0));
		Assert.Equal("青铜戒指", RunEquipmentSystem.BodySlotDefinition(run, 0, Accessory, 2));

		// 同一格：无操作
		Assert.False(RunEquipmentSystem.TrySwapBodySlots(run, 0, Accessory, 0, Accessory, 0, out error));
		Assert.Equal(RunEquipmentSystem.SameBodySlotError, error);

		// 跨部位（饰品 → 头部）：部位不符
		Assert.False(RunEquipmentSystem.TrySwapBodySlots(run, 0, Accessory, 0, Head, 0, out error));
		Assert.Equal(RunEquipmentSystem.BodySlotMismatchError, error);

		// 头 / 身 / 脚各一格：没有「另一格」可换（越界格按非法格拒绝）
		Assert.False(RunEquipmentSystem.TrySwapBodySlots(run, 0, Head, 0, Head, 1, out error));
		Assert.Equal(RunEquipmentSystem.InvalidSlotError, error);
	}

	[Fact]
	public void SlotCountsAndLabels_FollowAccessoryConfig()
	{
		RegisterStubTables();
		Assert.Equal(3, ItemNameResolver.AccessorySlotCount);
		Assert.Equal(3 + 3, RunEquipmentSystem.BodySlotTotalCount);
		Assert.Equal("头部", RunEquipmentSystem.SlotLabel(Head, 0));
		Assert.Equal("身体", RunEquipmentSystem.SlotLabel(Body, 0));
		Assert.Equal("脚部", RunEquipmentSystem.SlotLabel(Feet, 0));
		Assert.Equal("饰品1", RunEquipmentSystem.SlotLabel(Accessory, 0));
		Assert.Equal("饰品3", RunEquipmentSystem.SlotLabel(Accessory, 2));
		Assert.Equal(1, RunEquipmentSystem.SlotCountOf(Head));
		Assert.Equal(3, RunEquipmentSystem.SlotCountOf(Accessory));
		Assert.False(RunEquipmentSystem.IsValidSlotIndex(Accessory, 3)); // 越界格不合法

		// 配置改成 2 → 格数随之减少（界面自动加减格位，案 §二 / §九 第 5 条）
		ItemNameResolver.SetAccessorySlotCount(2);
		Assert.Equal(2, RunEquipmentSystem.SlotCountOf(Accessory));
		Assert.Equal(5, RunEquipmentSystem.BodySlotTotalCount);

		int[] accessoryKeys = { 20004, 20005 };
		RunSaveData run = NewRun();
		string error;
		for (int i = 0; i < accessoryKeys.Length; i++)
		{
			RunBagEntrySave entry = RunBagSystem.Add(run, BagCategory.Equipment, accessoryKeys[i], 1);
			Assert.True(RunEquipmentSystem.TryEquipSlotFromBag(run, entry.InstanceId, 0, Accessory, i, out error), error);
		}

		Assert.True(RunEquipmentSystem.AccessoryRowFull(run, 0)); // 配置成 2 = 两格都占满即满

		// 非正数一律回落到默认 3（坏表不锁死界面）
		ItemNameResolver.SetAccessorySlotCount(0);
		Assert.Equal(EquipmentConfigDefinition.DefaultAccessorySlotCount, ItemNameResolver.AccessorySlotCount);
	}

	[Fact]
	public void MigrateToSchema6_FillsAccessoryList_AndIsIdempotent()
	{
		RegisterStubTables();
		Assert.Equal(6, RunSaveData.CurrentSchemaVersion);

		RunSaveData run = new RunSaveData { SchemaVersion = 5 };
		RunCharacterSlotSave slot = new RunCharacterSlotSave
		{
			CharacterId = 1002,
			CurrentHp = 30,
			MaxHp = 30,
			EquippedAccessoryDefinitionIds = new List<string> { "护身符" },
		};
		run.CharacterSlots.Add(slot);
		run.DeckSlots.Add(new List<RunDeckEntry>());
		run.BagEntries = new List<RunBagEntrySave>();

		run.MigrateToCurrentSchema();
		Assert.Equal(6, run.SchemaVersion);
		Assert.Equal(3, slot.EquippedAccessoryDefinitionIds.Count);
		Assert.Equal("护身符", slot.EquippedAccessoryDefinitionIds[0]); // 已有值不动
		Assert.Equal(string.Empty, slot.EquippedAccessoryDefinitionIds[2]);

		run.MigrateToCurrentSchema(); // 幂等
		Assert.Equal(3, slot.EquippedAccessoryDefinitionIds.Count);
		Assert.Equal("护身符", RunEquipmentSystem.BodySlotDefinition(run, 0, Accessory, 0));
	}

	[Fact]
	public void SanitizeEquipment_ClearsUnknownAndMismatchedCells_KeepsLegalOnes()
	{
		RegisterStubTables();
		RunSaveData run = NewRun();
		RunCharacterSlotSave slot = run.CharacterSlots[0];

		RunEquipmentSystem.SetHand(slot, RunEquipmentSystem.LeftHand, "行军短剑"); // 合法
		RunEquipmentSystem.SetHand(slot, RunEquipmentSystem.RightHand, "布头巾");   // 部位装备占手位 → 清空
		RunEquipmentSystem.SetBodySlot(slot, Head, 0, "皮甲");                      // 头部格放身体装备 → 清空
		RunEquipmentSystem.SetBodySlot(slot, Body, 0, "布头巾");                    // 身体格放头部装备 → 清空
		RunEquipmentSystem.SetBodySlot(slot, Accessory, 0, "不存在饰品");            // 未注册 → 清空
		RunEquipmentSystem.SetBodySlot(slot, Feet, 0, "旅人靴");                    // 合法

		List<string> cleared = RunEquipmentSystem.SanitizeEquipment(run);
		Assert.Equal(4, cleared.Count);
		Assert.Equal("行军短剑", RunEquipmentSystem.HandDefinition(run, 0, RunEquipmentSystem.LeftHand));
		Assert.Equal(string.Empty, RunEquipmentSystem.HandDefinition(run, 0, RunEquipmentSystem.RightHand));
		Assert.Equal(string.Empty, RunEquipmentSystem.BodySlotDefinition(run, 0, Head, 0));
		Assert.Equal(string.Empty, RunEquipmentSystem.BodySlotDefinition(run, 0, Body, 0));
		Assert.Equal(string.Empty, RunEquipmentSystem.BodySlotDefinition(run, 0, Accessory, 0));
		Assert.Equal("旅人靴", RunEquipmentSystem.BodySlotDefinition(run, 0, Feet, 0));

		// 幂等：再洗一次没有新的清空
		Assert.Empty(RunEquipmentSystem.SanitizeEquipment(run));
	}

	[Fact]
	public void UnequipSlot_RejectedWhenBagWouldOverload()
	{
		RegisterStubTables();
		ItemNameResolver.RegisterItems(new Dictionary<int, ItemDefinition>
		{
			[999] = new ItemDefinition { ItemId = 999, DefinitionId = "重石", Load = 31f },
		});
		RunSaveData run = NewRun();
		RunBagEntrySave helmet = RunBagSystem.Add(run, BagCategory.Equipment, 20001, 1);
		Assert.True(RunEquipmentSystem.TryEquipSlotFromBag(run, helmet.InstanceId, 0, Head, 0, out string error), error);

		// 背包已超载：卸下会被整笔拒绝并给出具体数字（背包交互 §四）
		RunBagSystem.Add(run, BagCategory.Item, 999, 1);
		Assert.True(RunBagSystem.IsOverloaded(run));
		Assert.False(RunEquipmentSystem.TryUnequipSlot(run, 0, Head, 0, out error));
		Assert.Contains("负荷", error);
		Assert.Equal("布头巾", RunEquipmentSystem.BodySlotDefinition(run, 0, Head, 0)); // 状态未变
	}
}
