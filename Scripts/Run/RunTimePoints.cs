// RunTimePoints.cs
// 时间点系统（[地图玩法](../../README/玩法说明文档/系统规则/地图玩法/地图玩法.md) §五）的纯计算层：
// 计量精度、跨天边界、消耗判定与休息回复公式。纯逻辑、无 Godot 依赖 ——
// 供 RunMapStateSave / RunSession / 营地场景调用，并可直接单测。
using System;
using System.Collections.Generic;

/// <summary>时间点的计量与换算（地图玩法 §5.1 / §5.2、篝火休息与食物 §二）。</summary>
public static class RunTimePoints
{
	/// <summary>最小计量单位 = 战斗中的 1 个回合（0.1 时间点）。</summary>
	public const float Step = 0.1f;

	/// <summary>一天固定包含的时间点数（不随游戏进程变化）。</summary>
	public const float PointsPerDay = 4f;

	/// <summary>
	/// 移动到相邻节点的时间点进程（3 回合 = 0.3）的**默认值 / 兜底值**：正式数值读全局数据表
	/// `DataBase/GameVariables.csv` 的 `MoveTimePointCost`（消费点 `MapScene.EnterNode`，2026-10-05 用户口径：
	/// 数值放表里便于修改）；表里未配置该列时回落到这里。
	/// </summary>
	public const float MoveCost = 0.3f;

	/// <summary>战斗内每经过 1 个回合的进程（10 回合 = 1 时间点）。</summary>
	public const float BattleRoundCost = 0.1f;

	/// <summary>篝火总饱食度的有效上限（超出部分只提供饱食度、不提供食物效果）。</summary>
	public const int MaxSatiety = 10;

	/// <summary>休息基础回复比例（10%）。</summary>
	public const float RestBaseHealRatio = 0.10f;

	/// <summary>休息的剩余时间点比例项上限（剩余 = 4 时达到，20%）。</summary>
	public const float RestRemainingHealRatio = 0.20f;

	/// <summary>每点篝火总饱食度的回复比例（2%）。</summary>
	public const float RestSatietyHealRatio = 0.02f;

	/// <summary>时间点**只能消耗、不能回复**（进程不可回退）：负值与 NaN 一律按 0 处理。</summary>
	public static float Sanitize(float value) => float.IsNaN(value) || value < 0f ? 0f : value;

	/// <summary>对齐到最小计量单位（0.1）：避免多次累加出现 `0.30000001` 这类浮点漂移。</summary>
	public static float Quantize(float value)
	{
		double steps = Math.Round(Sanitize(value) / Step, MidpointRounding.AwayFromZero);
		return (float)(steps * Step);
	}

	/// <summary>当前是第几天（1 起）：进程累计每满 4 点即进入新的一天。</summary>
	public static int DayIndex(float total) => (int)Math.Floor(Quantize(total) / PointsPerDay) + 1;

	/// <summary>当天已消耗的时间点（0 ~ 4）。</summary>
	public static float UsedToday(float total) => Quantize(total) - (DayIndex(total) - 1) * PointsPerDay;

	/// <summary>当天剩余时间点（0 ~ 4）：跨天边界即「当天耗尽」。</summary>
	public static float RemainingToday(float total) => PointsPerDay - UsedToday(total);

	/// <summary>时间点是否够支付一项代价；不足时禁止继续前往，转为营地转场（地图交互 §五）。</summary>
	public static bool CanSpend(float total, float cost) => cost <= 0f || RemainingToday(total) + 1e-4f >= cost;

	/// <summary>
	/// 「晚上」的时间点阈值（村庄案 §八：当天剩余 ≤ 1.0 视为晚上）。
	/// 2026-10-06 从 `VillageLayout.NightRemainingThreshold` 迁来（村庄专用版图逻辑撤除；
	/// 阈值本身是时间点口径，与版图无关）—— 消费方：`VillageLodging.IsNight`（旅馆 / 民宿过夜回复比例）。
	/// </summary>
	public const float NightThreshold = 1.0f;

	/// <summary>
	/// 当天剩余是否已到「晚上」（≤ <see cref="NightThreshold"/>）；
	/// 浮点容差与 <see cref="CanSpend"/> 同口径（正好 1.0 也算晚上）。
	/// </summary>
	public static bool IsNight(float remainingToday) => remainingToday <= NightThreshold + 1e-4f;

	/// <summary>推进到「下一天的开始」：当天剩余作废（主动结束当天与耗尽强制休息都走这里，进程只增不减）。</summary>
	public static float NextDayStart(float total) => DayIndex(total) * PointsPerDay;

	/// <summary>进休息时的回复比例：10% + 20% ×（剩余时间点 ÷ 4）+ 2% × 篝火总饱食度。</summary>
	public static float RestHealRatio(float remaining, int satiety)
	{
		float remainingRatio = RestBaseHealRatio + RestRemainingHealRatio * Clamp01(Sanitize(remaining) / PointsPerDay);
		float satietyRatio = RestSatietyHealRatio * Math.Clamp(satiety, 0, MaxSatiety);
		return remainingRatio + satietyRatio;
	}

	/// <summary>守夜折算（篝火休息与食物 §四）：轮流守夜 ×2/3；单人守夜的守夜者不回复；其余不变。</summary>
	public static float ApplyWatch(float ratio, RunWatchMode mode, bool isWatcher) => mode switch
	{
		RunWatchMode.Rotation => ratio * 2f / 3f,
		RunWatchMode.Single when isWatcher => 0f,
		_ => ratio,
	};

	/// <summary>本次休息的回复量：以最大生命计算并向上取整（与事件表的百分比口径一致），不超过最大生命。</summary>
	public static int HealAmount(int maxHp, float ratio)
	{
		if (maxHp <= 0 || ratio <= 0f) return 0;
		// 减去 1e-6：`30 × 10% × 2/3` 这类正好落在整数上的取值不因浮点误差被向上多进 1 点。
		return Math.Min(maxHp, (int)Math.Ceiling(maxHp * (double)ratio - 1e-6));
	}

	/// <summary>界面显示：保留 1 位小数（地图玩法 §5.1）。</summary>
	public static string Format(float value) => Quantize(value).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

	/// <summary>常驻栏 / 地图信息条的时间点文案：`第 N 天 · 剩余 X.X / 4`。</summary>
	public static string FormatDayAndRemaining(float total) =>
		$"第 {DayIndex(total)} 天 · 剩余 {Format(RemainingToday(total))} / {Format(PointsPerDay)}";

	/// <summary>
	/// 「时间点不足 + 去处指引」的一行原因（设施操作 / 移动等共用这一处句式，避免每个设施各写一份）：
	/// `时间点不足：需要 1.0，当前剩余 0.4。请前往旅馆或民宿过夜，或回营地结束当天。`
	/// </summary>
	public static string ShortRestText(float need, float remaining, string restHint) =>
		$"时间点不足：需要 {Format(need)}，当前剩余 {Format(remaining)}。{restHint}";

	/// <summary>事件选项里「0.5 时间点（5 回合）」这类可换算写法（事件系统 §2.2 律 3）。</summary>
	public static string FormatWithRounds(float value)
	{
		float points = Quantize(value);
		int rounds = (int)Math.Round(points / Step, MidpointRounding.AwayFromZero);
		return $"{Format(points)} 时间点（{rounds} 回合）";
	}

	private static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
}

/// <summary>
/// 休息结算的纯逻辑（可单测，不碰 Godot / 存档 IO）：按「进入休息时的当天剩余 + 篝火总饱食度 + 守夜方式」
/// 回复每名角色，然后推进到新一天（当天剩余作废）。`RunSession.ApplyRest` 负责落档，营地界面负责显示。
/// </summary>
public static class RunRestResolver
{
	/// <summary>本次休息的**基础**回复比例（未折算守夜）：10% + 20% ×（剩余 ÷ 4）+ 2% × 饱食度。</summary>
	public static float BaseHealRatio(RunSaveData run, int satiety) =>
		RunTimePoints.RestHealRatio(run?.MapState == null ? 0f : run.MapState.RestRemainingTimePoints, satiety);

	/// <summary>预览某一名角色的回复量（营地界面的实时预览；不改动任何状态）。</summary>
	public static int PreviewHeal(RunSaveData run, int slotIndex, RunWatchMode watchMode, int watcherSlotIndex, int satiety)
	{
		if (run?.CharacterSlots == null || slotIndex < 0 || slotIndex >= run.CharacterSlots.Count)
		{
			return 0;
		}

		RunCharacterSlotSave slot = run.CharacterSlots[slotIndex];
		if (slot == null)
		{
			return 0;
		}

		bool isWatcher = watchMode == RunWatchMode.Single && slotIndex == watcherSlotIndex;
		float ratio = RunTimePoints.ApplyWatch(BaseHealRatio(run, satiety), watchMode, isWatcher);
		return Math.Min(Math.Max(0, slot.MaxHp - slot.CurrentHp), RunTimePoints.HealAmount(slot.MaxHp, ratio));
	}

	/// <summary>
	/// 结算休息：逐槽回复（超过最大生命值的部分无效）→ 推进到新一天。返回每名角色的**实际**回复量（与角色槽同序）。
	/// 夜袭与"睡眠不佳"本期未接入（需战斗遭遇生成与状态牌内容），见 9 月施工文档 §48 遗留清单。
	/// </summary>
	public static List<int> Apply(RunSaveData run, RunWatchMode watchMode, int watcherSlotIndex, int satiety)
	{
		List<int> healed = new List<int>();
		if (run?.CharacterSlots == null || run.CharacterSlots.Count == 0)
		{
			return healed;
		}

		float ratio = BaseHealRatio(run, satiety);
		for (int i = 0; i < run.CharacterSlots.Count; i++)
		{
			RunCharacterSlotSave slot = run.CharacterSlots[i];
			if (slot == null)
			{
				healed.Add(0);
				continue;
			}

			bool isWatcher = watchMode == RunWatchMode.Single && i == watcherSlotIndex;
			int amount = RunTimePoints.HealAmount(slot.MaxHp, RunTimePoints.ApplyWatch(ratio, watchMode, isWatcher));
			int before = slot.CurrentHp;
			slot.CurrentHp = Math.Min(slot.MaxHp, slot.CurrentHp + amount);
			healed.Add(slot.CurrentHp - before);
		}

		run.MapState.AdvanceToNextDay(); // 推进到新一天并清掉「待休息」标记（当天剩余作废）
		return healed;
	}
}

/// <summary>守夜方式（[篝火休息与食物](../../README/玩法说明文档/系统规则/营地系统/篝火休息与食物.md) §四）。</summary>
public enum RunWatchMode
{
	/// <summary>不守夜：全额回复（夜袭概率项属遗留，见 CampScene 注释）。</summary>
	None = 0,

	/// <summary>轮流守夜：每名角色回复降为 2/3，不触发夜袭。</summary>
	Rotation = 1,

	/// <summary>单人守夜：守夜角色不回复，其他角色全额。</summary>
	Single = 2,
}
