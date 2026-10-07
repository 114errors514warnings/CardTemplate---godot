// PlaceLevelTypes.cs
// **非战斗地点关卡**（村庄 / 商人 / 将来的任何地点）的类型判据与显示名 —— 全库唯一一处。
// 口径出处：README/施工文档/2026/2026.10/10月施工文档.md §33（地点一律走关卡统一流程，不得有专用场景）、
//   §33.2.1（地点内一切可交互单位 = 一个类型 `InteractPoint`）。
// 分工：关卡 CSV 第 3 列 `LevelType` 的字符串判据只住这里；`RunBattleScene` 用它决定
//   「开战场（`HexBattleScene`）」还是「开地点视图（`PlaceLevelView`）」，两边不各写一份字符串比较。
using System;

namespace CardSimulator.Battlefield;

public static class PlaceLevelTypes
{
	/// <summary>村庄（`LevelType` 取值，配表规范的 `LevelType` 取值表内）。</summary>
	public const string Village = "Village";

	/// <summary>商人（同上）。</summary>
	public const string Merchant = "Merchant";

	/// <summary>是否非战斗地点关：这类关卡只驱动 `PlaceLevelView`，不建战场、不算折损、不发结算。</summary>
	public static bool IsPlace(string levelType)
	{
		string text = (levelType ?? string.Empty).Trim();
		return string.Equals(text, Village, StringComparison.OrdinalIgnoreCase)
			|| string.Equals(text, Merchant, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>地点关的显示名（标题行 / 日志用）；非地点关原样返回关卡类型。</summary>
	public static string DisplayName(string levelType)
	{
		string text = (levelType ?? string.Empty).Trim();
		return string.Equals(text, Village, StringComparison.OrdinalIgnoreCase) ? "村庄"
			: string.Equals(text, Merchant, StringComparison.OrdinalIgnoreCase) ? "商人"
			: text;
	}
}
