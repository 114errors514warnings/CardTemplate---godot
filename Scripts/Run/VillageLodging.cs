// VillageLodging.cs
// 村庄休息类设施（旅馆 / 民宿）的**纯逻辑**：时段判定、回复比例、互斥与封门校验、结算落档（无 Godot 依赖，可 xUnit 直测）。
// 口径出处：README/施工文档/2026/2026.10/交互/{旅馆交互案,民宿交互案}.md 与 村庄地图交互案.md §八。
// 分工：规则、数值与**全部文案**只住这里；`RunSession` 只做「调它 → Save()」，界面只读它的返回值。
using System;
using System.Collections.Generic;
using CardSimulator;

public static class VillageLodging
{
	/// <summary>旅馆进入代价（金币，旅馆案 §一）。</summary>
	public const int InnGold = 1;

	/// <summary>旅馆白天回复比例（最大生命的 50% = 营地满配休息的水平，旅馆案 §三）。</summary>
	public const float InnDayRatio = 0.50f;

	/// <summary>旅馆晚上回复比例（100% = 回满；本局只能用一次的强效果）。</summary>
	public const float InnNightRatio = 1.00f;

	/// <summary>民宿白天回复比例（25%，民宿案 §四）。</summary>
	public const float GuesthouseDayRatio = 0.25f;

	/// <summary>民宿晚上回复比例（40%）。</summary>
	public const float GuesthouseNightRatio = 0.40f;

	// ── 文案（唯一定义处：界面 / 烟测 / 单测都读这里，避免三处各写一份）──

	/// <summary>旅馆本局已用（旅馆案 §五）。</summary>
	public const string InnUsedText = "旅馆本局只能进一次。";

	/// <summary>本局已选民宿过夜（旅馆案 §五 / §六）。</summary>
	public const string ChoseGuesthouseText = "你已经答应在民宿过夜了。";

	/// <summary>本局已选旅馆过夜（民宿案 §五）。</summary>
	public const string ChoseInnText = "你已经在这家旅店住下了。";

	/// <summary>民宿被事件选项封门（民宿案 §五）。</summary>
	public const string GuesthouseLockedByEventText = "民宿不再欢迎你们。";

	/// <summary>民宿当天已住（民宿案 §五）。</summary>
	public const string GuesthouseUsedTodayText = "今天已经借住过了，明天再来吧。";

	/// <summary>金币不足（与商店 / 锻铁铺同一句式）。</summary>
	public static string GoldShortText(int need, int have) => $"金币不足：需要 {need}，当前 {have}。";

	/// <summary>民宿被 Debuff 封门（民宿案 §五：文案里带该状态的显示名）。</summary>
	public static string DebuffBlockText(string stateName) => $"民宿因你们的恶名（{stateName}）拒绝收留。";

	/// <summary>是否「晚上」（村庄案 §八：当天剩余 ≤ 1.0）。</summary>
	public static bool IsNight(RunSaveData run) => RunTimePoints.IsNight(run?.MapState?.RemainingToday ?? 0f);

	/// <summary>本次休息的回复比例：旅馆 50% / 100%，民宿 25% / 40%（越界时段按白天算）。</summary>
	public static float HealRatio(bool inn, bool night) => inn
		? (night ? InnNightRatio : InnDayRatio)
		: (night ? GuesthouseNightRatio : GuesthouseDayRatio);

	/// <summary>单槽的**实际**回复量（不超过最大生命；口径同营地 `RunRestResolver.PreviewHeal`）。</summary>
	public static int PreviewHeal(RunCharacterSlotSave slot, float ratio)
	{
		if (slot == null)
		{
			return 0;
		}

		return Math.Min(Math.Max(0, slot.MaxHp - slot.CurrentHp), RunTimePoints.HealAmount(slot.MaxHp, ratio));
	}

	/// <summary>逐槽预览（与 `CharacterSlots` 同序；界面 tips 的数值预览与结算结果共用这一处算法）。</summary>
	public static List<int> PreviewAll(RunSaveData run, bool inn)
	{
		List<int> healed = new List<int>();
		if (run?.CharacterSlots == null)
		{
			return healed;
		}

		float ratio = HealRatio(inn, IsNight(run));
		foreach (RunCharacterSlotSave slot in run.CharacterSlots)
		{
			healed.Add(PreviewHeal(slot, ratio));
		}

		return healed;
	}

	/// <summary>tips 第 2 行：本次进入的效果摘要（旅馆案 §二 第 2 行「必须与结算结果逐字一致」）。</summary>
	public static string DescribeEffect(RunSaveData run, bool inn)
	{
		bool night = IsNight(run);
		if (inn)
		{
			// 每日 Debuff（P2-20）落地前不写「清除 N 层」——旅馆案 §八 的降级口径。
			return night ? "过夜：回复到满生命（100%）" : "过夜：回复 50% 生命";
		}

		return night ? "过夜：回复 40% 生命" : "过夜：回复 25% 生命";
	}

	/// <summary>tips 第 3 行：代价。</summary>
	public static string DescribeCost(bool inn) => inn ? $"代价：{InnGold} 金币" : "代价：无";

	/// <summary>旅馆可用性校验：**空串 = 可以进**（顺序：本局已用 → 已选民宿 → 金币）。</summary>
	public static string ValidateInn(RunSaveData run)
	{
		if (run?.VillageState == null)
		{
			return "本局尚未建立村庄状态。";
		}

		if (run.VillageState.ChosenLodging == RunLodgingChoice.Guesthouse)
		{
			return ChoseGuesthouseText;
		}

		if (run.VillageState.InnUsed)
		{
			return InnUsedText;
		}

		return GoldShortTextOrEmpty(InnGold, run.Gold);
	}

	/// <summary>民宿可用性校验：**空串 = 可以进**（顺序：Debuff 封门 → 事件封门 → 已选旅馆 → 当天已住）。</summary>
	public static string ValidateGuesthouse(RunSaveData run)
	{
		if (run?.VillageState == null)
		{
			return "本局尚未建立村庄状态。";
		}

		RunVillageStateSave state = run.VillageState;
		if (!string.IsNullOrEmpty(state.GuesthouseDebuffBlockName))
		{
			return DebuffBlockText(state.GuesthouseDebuffBlockName);
		}

		if (state.GuesthouseLockedByEvent)
		{
			return GuesthouseLockedByEventText;
		}

		if (state.ChosenLodging == RunLodgingChoice.Inn)
		{
			return ChoseInnText;
		}

		int day = run.MapState?.CurrentDay ?? 1;
		return state.GuesthouseUsedDay == day ? GuesthouseUsedTodayText : string.Empty;
	}

	/// <summary>
	/// 结算一次过夜（旅馆案 §四 / 民宿案 §六）：校验 → （旅馆）扣 1 金币 → 逐槽回复 → 清每日 Debuff（旅馆）→
	/// 推进到新一天 → 置状态位。**不触发篝火休息**（不吃食物、不选守夜、不判夜袭）。
	/// 返回逐槽实际回复量；`error` 非空 = 被拒（不改任何状态）。
	/// </summary>
	public static List<int> Apply(RunSaveData run, bool inn, out string error)
	{
		List<int> healed = new List<int>();
		error = inn ? ValidateInn(run) : ValidateGuesthouse(run);
		if (!string.IsNullOrEmpty(error) || run?.CharacterSlots == null || run.MapState == null)
		{
			if (string.IsNullOrEmpty(error))
			{
				error = "本局数据不完整。";
			}

			return healed;
		}

		if (inn)
		{
			run.Gold -= InnGold;
		}

		float ratio = HealRatio(inn, IsNight(run));
		foreach (RunCharacterSlotSave slot in run.CharacterSlots)
		{
			int before = slot == null ? 0 : slot.CurrentHp;
			int amount = PreviewHeal(slot, ratio);
			if (slot != null)
			{
				slot.CurrentHp = before + amount;
			}

			healed.Add(slot == null ? 0 : slot.CurrentHp - before);
		}

		// 旅馆的「清除每日 Debuff 全部层」在 P2-20 落地前是空操作（旅馆案 §八）。
		// 落地点：这里对每槽清 `IsDebuff` 状态；现在运行局没有「跨天 Debuff」容器，故留空。

		int stayedDay = run.MapState.CurrentDay;
		run.MapState.AdvanceToNextDay(); // 当天剩余作废；食物有效期在跨天结算里照常 −1

		if (inn)
		{
			run.VillageState.InnUsed = true;
			run.VillageState.ChosenLodging = RunLodgingChoice.Inn;
		}
		else
		{
			run.VillageState.GuesthouseUsedDay = stayedDay;
			run.VillageState.ChosenLodging = RunLodgingChoice.Guesthouse;
		}

		return healed;
	}

	private static string GoldShortTextOrEmpty(int need, int have) => have >= need ? string.Empty : GoldShortText(need, have);
}
