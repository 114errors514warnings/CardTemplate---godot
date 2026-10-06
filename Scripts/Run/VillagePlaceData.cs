// VillagePlaceData.cs
// 村庄 / 商人场景**运行期**的候选池构造（读已加载的配表 → 纯逻辑要吃的 DTO）。
// 只在场景 / `RunSession` 侧调用；纯逻辑（`VillageForage` / `SmithyCrafting` / `MerchantStock`）仍只吃 DTO，
// 这样规则的 xUnit 直测不需要起 Godot、也不需要读表。
using System;
using System.Collections.Generic;
using CardSimulator;

public static class VillagePlaceData
{
	/// <summary>
	/// 树林搜寻池（树林案 §三：现役材料表全表，每层同一张池）。
	/// 顺带按 `MaterialId` 升序固定顺序，抽样结果与表行序无关（只受随机源影响）。
	/// </summary>
	public static List<ForageMaterialEntry> ForagePool()
	{
		List<ForageMaterialEntry> pool = new List<ForageMaterialEntry>();
		foreach (KeyValuePair<int, MaterialDefinition> pair in LoadingSystem.LoadMaterialsByKey())
		{
			MaterialDefinition definition = pair.Value;
			if (definition == null)
			{
				continue;
			}

			pool.Add(new ForageMaterialEntry
			{
				MaterialId = definition.MaterialId,
				DefinitionId = definition.DefinitionId,
				Rarity = definition.Rarity,
			});
		}

		pool.Sort((a, b) => a.MaterialId.CompareTo(b.MaterialId));
		return pool;
	}

	/// <summary>材料显示名（材料表；查不到时由 `ItemNameResolver` 退回兜底名）。</summary>
	public static string MaterialName(int materialId) => ItemNameResolver.NameOf(BagCategory.Material, materialId);

	/// <summary>
	/// 一次抽样用的随机源：种子绑在（本局种子 × 天数 × 用途盐）上 ——
	/// 同一局同一天走同一处设施会得到同一串结果，读档重抽可复现（与 `MerchantStock` 的确定性口径同源）。
	/// </summary>
	public static Random RandomFor(RunSaveData run, int salt)
	{
		int runSeed = run?.MapState?.Seed ?? 0;
		int day = run?.MapState?.CurrentDay ?? 1;
		return new Random(unchecked(runSeed * 397 ^ day * 31 ^ salt));
	}
}
