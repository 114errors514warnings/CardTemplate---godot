// BagArrangeGate.cs
// 背包整理（拖动）的「无内容进行中」判据（背包系统交互案 §三）——**纯逻辑**，无 Godot 依赖。
// 为什么单独一层：判定与文案要能被 xUnit 直接断言，而 `RunSession` 是 autoload 的 Godot `Node`
// （测试项目只引用构建好的 dll、不引用 GodotSharp），因此规则不能住在 `RunSession` 里。
// 宿主（`RunFlowScene`）每帧把当刻的场景状态翻译成这四个开关，写进 `RunSession.BagArrangeBlockReason`（不入档）。
//
// 2026-10-02 用户口径（改判 §三 的「界面只读」）：闸门拦的是**落点**，不是**拿起** ——
// 内容进行中打开背包时，物品照样能拖起来（拖动预览正常），但放进道具栏 / 装备栏会被拒绝，
// 并由界面顶部横幅给出原因。下面两个文案常量就是这条口径的唯一定义处。
public static class BagArrangeGate
{
	/// <summary>营地 / 休息期间。</summary>
	public const string Rest = "休息中不可整理背包";
	/// <summary>战斗进行中。</summary>
	public const string Battle = "战斗中不可整理背包";
	/// <summary>事件进行中。</summary>
	public const string Event = "事件进行中不可整理背包";
	/// <summary>结算面板（或放弃确认弹窗）未关闭。</summary>
	public const string Settlement = "结算面板未关闭，不可整理背包";

	/// <summary>提示行前缀：说明被拦下的是落点（内容进行中仍可拿起物品查看）。</summary>
	public const string RestrictedHintPrefix = "拖动受限 · ";

	/// <summary>隐藏后的横幅句式（`{0}` = 原因）。</summary>
	public const string RestrictionFormat = "{0}：物品可以拿起查看，但不能放进道具栏 / 装备栏。";

	/// <summary>
	/// 横幅文案（用户口径 2026-10-02 第 1 条：内容进行中被拦下的拖动**要有横幅提示**）：
	/// 把闸门原因扩成一句能解释「能做什么 / 不能做什么」的话。空原因 = 无横幅（返回空串）。
	/// </summary>
	public static string DescribeRestriction(string reason) =>
		string.IsNullOrEmpty(reason) ? string.Empty : string.Format(RestrictionFormat, reason);

	/// <summary>
	/// 返回只读原因；空串 = 可拖动。优先级：营地 → 结算面板 → 战斗 → 事件。
	/// 「战后待领取态」由宿主播报为「都不成立」（结算面板已关闭、地图可选）→ 放行，与 §三 的清单一致。
	/// </summary>
	public static string Describe(bool campActive, bool settlementPanelOpen, bool battleContentActive, bool eventContentActive)
	{
		if (campActive)
		{
			return Rest;
		}

		if (settlementPanelOpen)
		{
			return Settlement;
		}

		if (battleContentActive)
		{
			return Battle;
		}

		return eventContentActive ? Event : string.Empty;
	}
}
