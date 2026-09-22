using System;
using CardSimulator;

public sealed class StateDefinition
{
	public StateType Type { get; }
	public string Name { get; }
	public bool IsStackable { get; }
	public bool IsDebuff { get; }
	public bool IsElite { get; }
	public StateDecayTiming DecayTiming { get; }
	public StateDecayMode DecayMode { get; }
	public int StacksToRemove { get; }
	public string EffectDescription { get; }
	/// <summary>状态表 `EnumName` 列记录的枚举名；未填写时自动取 <c>Type.ToString()</c>。</summary>
	public string EnumName { get; }

	public StateDefinition(
		StateType type,
		string name,
		bool isStackable,
		StateDecayTiming decayTiming = StateDecayTiming.OnTurnStart,
		StateDecayMode decayMode = StateDecayMode.None,
		int stacksToRemove = 0,
		bool isDebuff = false,
		bool isElite = false,
		string effectDescription = null,
		string enumName = null)
	{
		Type = type;
		Name = name ?? string.Empty;
		IsStackable = isStackable;
		IsDebuff = isDebuff;
		IsElite = isElite;
		DecayTiming = decayTiming;
		DecayMode = decayMode;
		StacksToRemove = stacksToRemove < 0 ? 0 : stacksToRemove;
		EffectDescription = effectDescription ?? string.Empty;
		EnumName = string.IsNullOrWhiteSpace(enumName) ? type.ToString() : enumName.Trim();
	}
}

/// <summary>
/// `StateType` 枚举名与枚举值的映射工具（纯逻辑，不依赖 Godot）：
/// 状态表 `通用State.csv` 的 `EnumName` 列靠它把"名字"和数字 `StateType` 对上，
/// 两边不一致时由加载器报错，避免出现"表里写了 21 却对不上任何枚举成员"这类静默失效。
/// </summary>
public static class StateTypeNames
{
	public static string NameOf(StateType type) => type.ToString();

	/// <summary>按枚举名解析：大小写不敏感，必须是已定义成员；不接受纯数字（数字属于 `StateType` 列）。</summary>
	public static bool TryParse(string enumName, out StateType type)
	{
		type = StateType.None;
		if (string.IsNullOrWhiteSpace(enumName)) return false;
		string trimmed = enumName.Trim();
		if (char.IsDigit(trimmed[0])) return false;
		if (!Enum.TryParse(trimmed, ignoreCase: true, out StateType parsed)) return false;
		if (!Enum.IsDefined(typeof(StateType), parsed)) return false;
		type = parsed;
		return true;
	}

	/// <summary>`EnumName` 列与数字 `StateType` 列是否一致；列为空视为"未填写"（由调用方补默认值）。</summary>
	public static bool Matches(StateType type, string enumName) =>
		string.IsNullOrWhiteSpace(enumName) || (TryParse(enumName, out StateType parsed) && parsed == type);
}