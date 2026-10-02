// RunBagArrangeTests.cs
// 背包整理（P0-17 界面半 / P0-18 装备链，2026-10-02 批 E）纯逻辑单测：
//   装备 → 左右手位（单手 / 双手 / 部位不符 / 被换下回背包）、手位互换、卸下与负荷试算、
//   随身 3 格（条目的 CarrySlot 归属 + 兼容镜像 + 不占背包负荷 + CountOf 输入口径）、
//   手位 → 道具栏的许可通道、整理闸门文案、以及 v4 旧档 → v5 的两条迁移（手位字段 / 随身格归属）。
using System.Collections.Generic;
using System.Linq;
using Xunit;

[Collection("ItemNameResolverState")]
public class RunBagArrangeTests
{
	/// <summary>注册假物品与装备表：模拟 LoadingSystem 配表加载后的注册状态（名字 / 手数 / 负荷）。</summary>
	private static void RegisterStubTables()
	{
		ItemNameResolver.Clear();
		ItemNameResolver.RegisterMaterials(new Dictionary<int, MaterialDefinition>
		{
			[101] = new MaterialDefinition { MaterialId = 101, DefinitionId = "药草", Load = 0.2f, Rarity = ItemRarity.Common },
		});
		ItemNameResolver.RegisterItems(new Dictionary<int, ItemDefinition>
		{
			[301] = new ItemDefinition { ItemId = 301, DefinitionId = "治疗药水", Load = 0.3f, Rarity = ItemRarity.Common },
		});
		ItemNameResolver.RegisterWeapons(new Dictionary<int, WeaponDefinition>
		{
			[10001] = new WeaponDefinition { WeaponId = 10001, DefinitionId = "行军短剑", HandsRequired = 1 },
			[10002] = new WeaponDefinition { WeaponId = 10002, DefinitionId = "长刀", HandsRequired = 1 },
			[10003] = new WeaponDefinition { WeaponId = 10003, DefinitionId = "长枪", HandsRequired = 2 },
		});
	}

	/// <summary>一名角色的最小局（手位与随身格都补齐）。</summary>
	private static RunSaveData NewRun()
	{
		RunSaveData run = new RunSaveData();
		run.CharacterSlots.Add(new RunCharacterSlotSave { CharacterId = 1002, CurrentHp = 30, MaxHp = 30 });
		run.DeckSlots.Add(new List<RunDeckEntry>());
		RunBagSystem.EnsureCollections(run);
		return run;
	}

	[Fact]
	public void EquipFromBag_SingleHanded_MovesOutOfBagAndMirrorsLegacyField()
	{
		RegisterStubTables();
		RunSaveData run = NewRun();
		RunBagEntrySave sword = RunBagSystem.Add(run, BagCategory.Equipment, 10001, 1);
		float loadBefore = RunBagSystem.TotalLoad(run);

		Assert.True(RunEquipmentSystem.TryEquipFromBag(run, sword.InstanceId, 0, RunEquipmentSystem.LeftHand, out string error), error);
		Assert.Equal("行军短剑", RunEquipmentSystem.HandDefinition(run, 0, RunEquipmentSystem.LeftHand));
		Assert.Equal("行军短剑", RunEquipmentSystem.HandText(run, 0, RunEquipmentSystem.LeftHand));
		Assert.Equal(string.Empty, RunEquipmentSystem.HandDefinition(run, 0, RunEquipmentSystem.RightHand));
		// 手位的真相是左右手两字段，旧字段保持左手镜像（既有读写点照旧可用）。
		Assert.Equal("行军短剑", run.CharacterSlots[0].EquippedWeaponDefinitionId);
		// 装备搬出手位后不在背包里：条目消失、负荷下降（背包系统交互案 §四）。
		Assert.Null(RunBagSystem.Find(run, sword.InstanceId));
		Assert.Equal(0, RunBagSystem.CountOf(run, BagCategory.Equipment, 10001));
		Assert.Equal(loadBefore - 2f, RunBagSystem.TotalLoad(run), 3);

		// 卸下 → 回背包、负荷回升（局外不落地丢弃）
		Assert.True(RunEquipmentSystem.TryUnequipHand(run, 0, RunEquipmentSystem.LeftHand, out error), error);
		Assert.Equal(string.Empty, RunEquipmentSystem.HandDefinition(run, 0, RunEquipmentSystem.LeftHand));
		Assert.Equal(string.Empty, run.CharacterSlots[0].EquippedWeaponDefinitionId);
		Assert.Equal(1, RunBagSystem.CountOf(run, BagCategory.Equipment, 10001));
		Assert.Equal(loadBefore, RunBagSystem.TotalLoad(run), 3);
	}

	[Fact]
	public void EquipFromBag_TwoHanded_NeedsBothHandsFree_ThenOccupiesBoth()
	{
		RegisterStubTables();
		RunSaveData run = NewRun();
		RunBagEntrySave lance = RunBagSystem.Add(run, BagCategory.Equipment, 10003, 1);

		Assert.True(RunEquipmentSystem.TryEquipFromBag(run, lance.InstanceId, 0, RunEquipmentSystem.LeftHand, out string error), error);
		Assert.Equal("长枪", RunEquipmentSystem.HandDefinition(run, 0, RunEquipmentSystem.LeftHand));
		Assert.Equal("长枪", RunEquipmentSystem.HandDefinition(run, 0, RunEquipmentSystem.RightHand));

		// 另一手是双手装备：任何新装备都要先卸下（单手 → 双手冲突用 TwoHandedSwapError）
		RunBagEntrySave sword = RunBagSystem.Add(run, BagCategory.Equipment, 10001, 1);
		Assert.False(RunEquipmentSystem.TryEquipFromBag(run, sword.InstanceId, 0, RunEquipmentSystem.RightHand, out error));
		Assert.Equal(RunEquipmentSystem.TwoHandedSwapError, error);
		Assert.Equal(1, RunBagSystem.CountOf(run, BagCategory.Equipment, 10001)); // 失败不得动背包

		// 双手装备本身要求两槽同时空（右槽空、左槽被自己占住 → 仍要拒绝）
		RunEquipmentSystem.SetHand(run.CharacterSlots[0], RunEquipmentSystem.RightHand, string.Empty);
		RunBagEntrySave lance2 = RunBagSystem.Add(run, BagCategory.Equipment, 10003, 1);
		Assert.False(RunEquipmentSystem.TryEquipFromBag(run, lance2.InstanceId, 0, RunEquipmentSystem.RightHand, out error));
		Assert.Equal(RunEquipmentSystem.BothSlotsRequiredError, error);

		// 卸下双手装备 → 两槽一起空、装备回背包
		Assert.True(RunEquipmentSystem.TryUnequipHand(run, 0, RunEquipmentSystem.LeftHand, out error), error);
		Assert.Equal(string.Empty, RunEquipmentSystem.HandDefinition(run, 0, RunEquipmentSystem.LeftHand));
		Assert.Equal(string.Empty, RunEquipmentSystem.HandDefinition(run, 0, RunEquipmentSystem.RightHand));
		Assert.Equal(2, RunBagSystem.CountOf(run, BagCategory.Equipment, 10003));
	}

	[Fact]
	public void EquipFromBag_SingleHanded_RejectedWhenOtherHandHoldsTwoHanded()
	{
		RegisterStubTables();
		RunSaveData run = NewRun();
		RunBagEntrySave lance = RunBagSystem.Add(run, BagCategory.Equipment, 10003, 1);
		Assert.True(RunEquipmentSystem.TryEquipFromBag(run, lance.InstanceId, 0, RunEquipmentSystem.LeftHand, out _));

		// 只留左手（模拟「另一手仍是双手武器」的写坏档）：单手装备不得挤进来
		RunEquipmentSystem.SetHand(run.CharacterSlots[0], RunEquipmentSystem.RightHand, string.Empty);
		RunBagEntrySave sword = RunBagSystem.Add(run, BagCategory.Equipment, 10001, 1);

		Assert.False(RunEquipmentSystem.TryEquipFromBag(run, sword.InstanceId, 0, RunEquipmentSystem.RightHand, out string error));
		Assert.Equal(RunEquipmentSystem.TwoHandedSwapError, error);
	}

	[Fact]
	public void EquipFromBag_ReplacesSingleHanded_AndReturnsOldItemToBag()
	{
		RegisterStubTables();
		RunSaveData run = NewRun();
		RunBagEntrySave sword = RunBagSystem.Add(run, BagCategory.Equipment, 10001, 1);
		RunBagEntrySave saber = RunBagSystem.Add(run, BagCategory.Equipment, 10002, 1);

		Assert.True(RunEquipmentSystem.TryEquipFromBag(run, sword.InstanceId, 0, RunEquipmentSystem.LeftHand, out _));
		Assert.True(RunEquipmentSystem.TryEquipFromBag(run, saber.InstanceId, 0, RunEquipmentSystem.LeftHand, out string error), error);
		Assert.Equal("长刀", RunEquipmentSystem.HandDefinition(run, 0, RunEquipmentSystem.LeftHand));
		Assert.Equal(1, RunBagSystem.CountOf(run, BagCategory.Equipment, 10001)); // 被换下的单手武器回背包
	}

	[Fact]
	public void EquipFromBag_RejectsNonEquipment_AndEquipOfCarriedItem()
	{
		RegisterStubTables();
		RunSaveData run = NewRun();
		RunBagEntrySave potion = RunBagSystem.Add(run, BagCategory.Item, 301, 1);

		Assert.False(RunEquipmentSystem.TryEquipFromBag(run, potion.InstanceId, 0, RunEquipmentSystem.LeftHand, out string error));
		Assert.Equal(RunEquipmentSystem.SlotMismatchError, error);

		// 随身格上的装备不能直接装备（先取回背包）
		RunBagEntrySave sword = RunBagSystem.Add(run, BagCategory.Equipment, 10001, 1);
		Assert.True(RunBagSystem.TrySetCarrySlot(run, 1, sword.InstanceId, out _));
		Assert.False(RunEquipmentSystem.TryEquipFromBag(run, sword.InstanceId, 0, RunEquipmentSystem.LeftHand, out error));
		Assert.Equal(RunEquipmentSystem.EquippedButCarriedError, error);
	}

	[Fact]
	public void MoveHandToHand_SwapsSingleHanded_AndRejectsTwoHanded()
	{
		RegisterStubTables();
		RunSaveData run = NewRun();
		RunCharacterSlotSave slot = run.CharacterSlots[0];

		RunEquipmentSystem.SetHand(slot, RunEquipmentSystem.LeftHand, "行军短剑");
		RunEquipmentSystem.SetHand(slot, RunEquipmentSystem.RightHand, "长刀");
		Assert.True(RunEquipmentSystem.TryMoveHandToHand(run, 0, RunEquipmentSystem.LeftHand, RunEquipmentSystem.RightHand, out string error), error);
		Assert.Equal("长刀", RunEquipmentSystem.HandDefinition(run, 0, RunEquipmentSystem.LeftHand));
		Assert.Equal("行军短剑", RunEquipmentSystem.HandDefinition(run, 0, RunEquipmentSystem.RightHand));

		Assert.False(RunEquipmentSystem.TryMoveHandToHand(run, 0, RunEquipmentSystem.LeftHand, RunEquipmentSystem.LeftHand, out error));
		Assert.Equal(RunEquipmentSystem.SameHandError, error);

		RunEquipmentSystem.SetHand(slot, RunEquipmentSystem.LeftHand, "长枪"); // 双手：不参与互换
		Assert.False(RunEquipmentSystem.TryMoveHandToHand(run, 0, RunEquipmentSystem.LeftHand, RunEquipmentSystem.RightHand, out error));
		Assert.Equal(RunEquipmentSystem.TwoHandedSwapError, error);
	}

	[Fact]
	public void UnequipHand_RejectedWhenBagWouldOverload()
	{
		RegisterStubTables();
		ItemNameResolver.SetInventoryCapacity(3f); // 队伍上限压到 3.0，制造「卸下会超载」
		RunSaveData run = NewRun();
		RunEquipmentSystem.SetHand(run.CharacterSlots[0], RunEquipmentSystem.LeftHand, "行军短剑"); // 2.0
		RunBagSystem.Add(run, BagCategory.Material, 101, 6);                                    // 0.2 × 6 = 1.2 → 1.2 + 2.0 > 3.0

		Assert.False(RunEquipmentSystem.TryUnequipHand(run, 0, RunEquipmentSystem.LeftHand, out string error));
		Assert.Contains("负荷不足", error);
		Assert.Equal("行军短剑", RunEquipmentSystem.HandDefinition(run, 0, RunEquipmentSystem.LeftHand)); // 失败不得动状态

		ItemNameResolver.SetInventoryCapacity(ItemNameResolver.DefaultInventoryCapacity);
		Assert.True(RunEquipmentSystem.TryUnequipHand(run, 0, RunEquipmentSystem.LeftHand, out error), error);
	}

	[Fact]
	public void CarrySlot_MoveExchangeReturn_KeepsEntriesOutOfBagLoad()
	{
		RegisterStubTables();
		RunSaveData run = NewRun();
		RunBagEntrySave potion = RunBagSystem.Add(run, BagCategory.Item, 301, 1);   // 0.3
		RunBagEntrySave herb = RunBagSystem.Add(run, BagCategory.Material, 101, 1); // 0.2

		Assert.True(RunBagSystem.TrySetCarrySlot(run, 0, potion.InstanceId, out string error), error);
		Assert.Equal(0, potion.CarrySlot);
		Assert.Same(potion, RunBagSystem.CarrySlotEntry(run, 0));
		Assert.Equal(potion.InstanceId, run.CarryItemSlots[0]);           // 兼容镜像同步
		Assert.Equal("治疗药水", RunBagSystem.CarrySlotText(run, 0));
		Assert.Empty(RunBagSystem.EntriesOf(run, BagCategory.Item));       // 不在背包列表里
		Assert.Equal(0.2f, RunBagSystem.TotalLoad(run), 3);                // 随身格不占背包负荷

		// 交换：原物回背包
		Assert.True(RunBagSystem.TrySetCarrySlot(run, 0, herb.InstanceId, out error), error);
		Assert.Equal(-1, potion.CarrySlot);
		Assert.Equal(0, herb.CarrySlot);
		Assert.Equal(0.3f, RunBagSystem.TotalLoad(run), 3);

		// 取回：回背包、镜像清空
		Assert.True(RunBagSystem.TryClearCarrySlot(run, 0, out error), error);
		Assert.Equal(-1, herb.CarrySlot);
		Assert.Equal(string.Empty, run.CarryItemSlots[0]);
		Assert.Equal(0.5f, RunBagSystem.TotalLoad(run), 3);

		// 输入口径：随身格上的材料不算「背包内可用件数」（烹饪 / 消耗读的是背包）
		Assert.True(RunBagSystem.TrySetCarrySlot(run, 2, herb.InstanceId, out error), error);
		Assert.Equal(0, RunBagSystem.CountOf(run, BagCategory.Material, 101));
		Assert.Single(RunBagSystem.AllEntriesOf(run, BagCategory.Material));
	}

	[Fact]
	public void HandToCarrySlot_BlockedByDefaultPermissionChannel()
	{
		RegisterStubTables();
		RunSaveData run = NewRun();
		RunEquipmentSystem.SetHand(run.CharacterSlots[0], RunEquipmentSystem.LeftHand, "行军短剑");

		Assert.False(RunEquipmentSystem.AllowsWeaponInItemSlots("行军短剑"));
		Assert.False(RunEquipmentSystem.TryMoveHandToCarrySlot(run, 0, RunEquipmentSystem.LeftHand, 0, out string error));
		Assert.Equal(RunEquipmentSystem.WeaponInItemSlotError, error);
		Assert.Equal("行军短剑", RunEquipmentSystem.HandDefinition(run, 0, RunEquipmentSystem.LeftHand));
	}

	[Fact]
	public void ArrangeGate_DescribesEveryBlockingState()
	{
		Assert.Equal(string.Empty, BagArrangeGate.Describe(false, false, false, false));
		Assert.Equal(BagArrangeGate.Rest, BagArrangeGate.Describe(true, true, true, true));
		Assert.Equal(BagArrangeGate.Settlement, BagArrangeGate.Describe(false, true, true, true));
		Assert.Equal(BagArrangeGate.Battle, BagArrangeGate.Describe(false, false, true, false));
		Assert.Equal(BagArrangeGate.Event, BagArrangeGate.Describe(false, false, false, true));
	}

	[Fact]
	public void Migrate_HandsToSchema5_MapsLegacyWeaponFieldToHands()
	{
		RegisterStubTables();
		RunSaveData legacy = new RunSaveData { SchemaVersion = 4 };
		legacy.CharacterSlots.Add(new RunCharacterSlotSave { CharacterId = 1002, EquippedWeaponDefinitionId = "长枪" });      // 双手
		legacy.CharacterSlots.Add(new RunCharacterSlotSave { CharacterId = 1003, EquippedWeaponDefinitionId = "行军短剑" }); // 单手
		legacy.CharacterSlots.Add(new RunCharacterSlotSave { CharacterId = 1004 });
		RunBagSystem.EnsureCollections(legacy);

		legacy.MigrateToCurrentSchema();

		Assert.Equal(RunSaveData.CurrentSchemaVersion, legacy.SchemaVersion);
		Assert.Equal("长枪", legacy.CharacterSlots[0].LeftHandDefinitionId);
		Assert.Equal("长枪", legacy.CharacterSlots[0].RightHandDefinitionId);   // 双手占满两槽
		Assert.Equal("长枪", legacy.CharacterSlots[0].EquippedWeaponDefinitionId);
		Assert.Equal("行军短剑", legacy.CharacterSlots[1].LeftHandDefinitionId);
		Assert.Equal(string.Empty, legacy.CharacterSlots[1].RightHandDefinitionId);
		Assert.Equal(string.Empty, legacy.CharacterSlots[2].LeftHandDefinitionId);

		// 幂等：再迁移一次不改变任何字段
		legacy.MigrateToCurrentSchema();
		Assert.Equal("长枪", legacy.CharacterSlots[0].RightHandDefinitionId);
		Assert.Equal("行军短剑", legacy.CharacterSlots[1].LeftHandDefinitionId);
	}

	[Fact]
	public void Migrate_CarrySlotMirror_AdoptsEntryOwnership()
	{
		RegisterStubTables();
		RunSaveData legacy = new RunSaveData { SchemaVersion = 4 };
		RunBagSystem.EnsureCollections(legacy);
		RunBagEntrySave potion = RunBagSystem.Add(legacy, BagCategory.Item, 301, 1);
		legacy.CarryItemSlots[0] = potion.InstanceId; // v4 只有镜像列表，条目没有归属

		legacy.MigrateToCurrentSchema();
		Assert.Equal(0, potion.CarrySlot);
		Assert.Same(potion, RunBagSystem.CarrySlotEntry(legacy, 0));
		Assert.Equal(potion.InstanceId, legacy.CarryItemSlots[0]);

		// 反向补齐：条目声明在格子里、镜像为空时，镜像要跟上
		legacy.CarryItemSlots[0] = string.Empty;
		potion.CarrySlot = 1;
		legacy.MigrateToCurrentSchema();
		Assert.Equal(potion.InstanceId, legacy.CarryItemSlots[1]);
	}

	[Fact]
	public void EquipmentLoad_DerivedFromHandsRequired_UnlessTableProvidesLoad()
	{
		RegisterStubTables();
		Assert.Equal(2f, ItemNameResolver.LoadOf(BagCategory.Equipment, 10001), 3);  // 单手推导
		Assert.Equal(4f, ItemNameResolver.LoadOf(BagCategory.Equipment, 10003), 3);  // 双手推导

		ItemNameResolver.RegisterWeapons(new Dictionary<int, WeaponDefinition>
		{
			[10004] = new WeaponDefinition { WeaponId = 10004, DefinitionId = "魔典", HandsRequired = 2, Load = 1.5f },
		});
		Assert.Equal(1.5f, ItemNameResolver.LoadOf(BagCategory.Equipment, 10004), 3); // 表里有 Load 就用表里的
		Assert.Equal(2, ItemNameResolver.HandsRequiredOfDefinition("魔典"));
		Assert.True(ItemNameResolver.TryGetEquipmentKey("魔典", out int key));
		Assert.Equal(10004, key);
	}

	// ── 2026-10-02 用户指令：① 闸门拦落点不拦拿起（横幅文案）② 背包格区均匀网格 + 页数 / 翻页 ──

	[Fact]
	public void ArrangeGate_RestrictionBanner_SaysPickUpIsStillAllowed()
	{
		// 空原因 = 不显示横幅
		Assert.Equal(string.Empty, BagArrangeGate.DescribeRestriction(string.Empty));
		Assert.Equal(string.Empty, BagArrangeGate.DescribeRestriction(null));
		// 四个原因都套同一句式：先给原因，再说「能拿起 / 不能放进槽位」
		foreach (string reason in new[] { BagArrangeGate.Rest, BagArrangeGate.Battle, BagArrangeGate.Event, BagArrangeGate.Settlement })
		{
			string text = BagArrangeGate.DescribeRestriction(reason);
			Assert.StartsWith(reason, text, System.StringComparison.Ordinal);
			Assert.Contains("不能放进道具栏", text);
			Assert.Contains("可以拿起", text);
		}

		Assert.Equal("拖动受限 · ", BagArrangeGate.RestrictedHintPrefix);
	}

	[Fact]
	public void BagPage_PageCountOf_CoversEmptyExactAndRemainder()
	{
		// 均匀网格：固定 5 列 × 5 行，每页 25 格（空位也画格）
		Assert.Equal(5, BagPageMath.Columns);
		Assert.Equal(5, BagPageMath.Rows);
		Assert.Equal(25, BagPageMath.PageCapacity);

		Assert.Equal(1, BagPageMath.PageCountOf(0));    // 空背包也是 1 页（显示「第 1 / 1 页」）
		Assert.Equal(1, BagPageMath.PageCountOf(1));
		Assert.Equal(1, BagPageMath.PageCountOf(25));   // 恰好整页
		Assert.Equal(2, BagPageMath.PageCountOf(26));   // 多一件就翻页
		Assert.Equal(2, BagPageMath.PageCountOf(50));
		Assert.Equal(3, BagPageMath.PageCountOf(51));
	}

	[Fact]
	public void BagPage_ClampSliceAndButtons_MatchRenderedGrid()
	{
		// 页码夹取：条目变少 / 页签切换后停在合法页
		Assert.Equal(0, BagPageMath.ClampPage(9, 0));
		Assert.Equal(0, BagPageMath.ClampPage(-3, 30));
		Assert.Equal(1, BagPageMath.ClampPage(5, 30));  // 30 件 = 2 页 → 最大页号 1

		// 切片：第 1 页取 0..24，末页只取剩余件数
		Assert.Equal(0, BagPageMath.FirstIndexOn(0, 30));
		Assert.Equal(25, BagPageMath.FirstIndexOn(1, 30));
		Assert.Equal(25, BagPageMath.FirstIndexOn(7, 30)); // 越界页夹到最后
		Assert.Equal(0, BagPageMath.CountOn(0, 0));
		Assert.Equal(25, BagPageMath.CountOn(0, 25));
		Assert.Equal(5, BagPageMath.CountOn(1, 30));
		Assert.Equal(5, BagPageMath.CountOn(9, 30));

		// 按钮可用性：只有一页时两边都禁用
		Assert.False(BagPageMath.HasPrevious(0, 0));
		Assert.False(BagPageMath.HasNext(0, 0));
		Assert.False(BagPageMath.HasPrevious(0, 26));
		Assert.True(BagPageMath.HasNext(0, 26));
		Assert.True(BagPageMath.HasPrevious(1, 26));
		Assert.False(BagPageMath.HasNext(1, 26));

		// 文案（1 基）
		Assert.Equal("第 1 / 1 页", BagPageMath.PageText(0, 0));
		Assert.Equal("第 1 / 2 页", BagPageMath.PageText(0, 26));
		Assert.Equal("第 2 / 2 页", BagPageMath.PageText(9, 26));
	}
}
