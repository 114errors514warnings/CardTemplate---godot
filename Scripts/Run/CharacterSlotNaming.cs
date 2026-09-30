// CharacterSlotNaming.cs
// 队伍槽位的角色显示名（交互案 §3.1）：队伍内出现同名角色时按「同名出现次序」在名字末尾加编号。
// 纯逻辑（只依赖传入的槽位角色 Id 列表与取名委托），便于单测；界面一律经 RunSession.GetSlotDisplayName 取名。
using System;
using System.Collections.Generic;

public static class CharacterSlotNaming
{
	/// <summary>按槽位序列出显示名：同名第 k 次出现（k ≥ 2）显示 `原名 + k`，第 1 次显示原名。</summary>
	/// <param name="characterIds">槽位角色 Id（顺序 = 槽位序 0 → 1 → 2）。</param>
	/// <param name="resolveName">角色 Id → `Character.csv` 的 `Name`；解析不出名字时返回空串/ null。</param>
	public static List<string> BuildDisplayNames(IReadOnlyList<int> characterIds, Func<int, string> resolveName)
	{
		List<string> names = new List<string>();
		if (characterIds == null)
		{
			return names;
		}

		// 编号口径（2026-09-27 第四轮）：扫描到第 i 个槽位时，统计前面出现过几个同一 CharacterId。
		// 编号是「同名出现次序」而不是「槽位号 + 1」：重剑手 / 伊瑟拉 / 重剑手 → 重剑手 / 伊瑟拉 / 重剑手2。
		Dictionary<int, int> occurrences = new Dictionary<int, int>();
		for (int i = 0; i < characterIds.Count; i++)
		{
			int characterId = characterIds[i];
			occurrences.TryGetValue(characterId, out int seen);
			occurrences[characterId] = seen + 1;

			string baseName = resolveName == null ? null : resolveName(characterId);
			if (string.IsNullOrWhiteSpace(baseName))
			{
				// 兜底（§3.1）：角色未在 Character.csv 中找到 → 显示 `角色 {id}`，不做编号。
				names.Add($"角色 {characterId}");
				continue;
			}

			names.Add(seen == 0 ? baseName : $"{baseName}{seen + 1}");
		}

		return names;
	}

	/// <summary>取某个槽位的显示名；槽位越界返回 `角色 ?`（调用方应先校验槽位）。</summary>
	public static string GetDisplayName(IReadOnlyList<int> characterIds, int slotIndex, Func<int, string> resolveName)
	{
		List<string> names = BuildDisplayNames(characterIds, resolveName);
		return slotIndex >= 0 && slotIndex < names.Count ? names[slotIndex] : "角色 ?";
	}
}
