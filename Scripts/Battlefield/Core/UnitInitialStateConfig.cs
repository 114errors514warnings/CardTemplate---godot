using System;
using System.Collections.Generic;

namespace CardSimulator.Battlefield;

/// <summary>解析后的单位初始值：初始生命（可空）、初始状态（按出现顺序）与未被机制消费的额外键值。</summary>
public sealed record UnitInitialState(int? Hp, IReadOnlyList<(StateType State, int Stacks)> States,
    IReadOnlyDictionary<string, string> Extras)
{
    public static readonly UnitInitialState Empty =
        new(null, Array.Empty<(StateType, int)>(), new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
}

/// <summary>
/// 关卡对象的初始值（单关卡 CSV 的 `InitialValue` 列）：`|` 分隔多个 `Key=Value` 条目。
/// 机制当前消费的键：
/// - `HP=&lt;正整数&gt;`：初始生命，超过模板上限时按上限截断；
/// - `State=&lt;StateType&gt;:&lt;层数&gt;`：初始状态，可重复出现并依次叠加；层数省略时按 1 层。
/// `StateType` 可写枚举名（如 `Ignite`）或编号（与 `通用State.csv` 的 `StateType` 列一致，如 `3`）。
/// **未定义的键不报错、也不会被丢弃**：原样保留在 <see cref="UnitInitialState.Extras"/> 里，供后续机制读取
/// （例如关卡 `F1-006` 使用的 `窃取=1`）。已知键的值非法（HP 非正整数、层数 ≤0、StateType 不存在等）
/// 或条目缺少 `=` 时抛错并带出原始文本——配置错误必须在开局就暴露，不能静默忽略。
/// </summary>
public static class UnitInitialStateConfig
{
    public static UnitInitialState Parse(string raw, string context = "")
    {
        if (string.IsNullOrWhiteSpace(raw)) return UnitInitialState.Empty;
        int? hp = null;
        var states = new List<(StateType, int)>();
        var extras = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string entry in raw.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            string token = entry.Trim();
            if (token.Length == 0) continue;
            int separator = token.IndexOf('=');
            if (separator <= 0) throw Invalid(raw, context, $"条目必须是 Key=Value：{token}");
            string key = token[..separator].Trim();
            string value = token[(separator + 1)..].Trim();
            if (key.Length == 0) throw Invalid(raw, context, $"条目缺少键名：{token}");
            if (key.Equals("HP", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(value, out int parsedHp) || parsedHp <= 0) throw Invalid(raw, context, $"HP 必须是正整数：{token}");
                hp = parsedHp;
                continue;
            }
            if (key.Equals("State", StringComparison.OrdinalIgnoreCase))
            {
                states.Add(ParseState(value, raw, context));
                continue;
            }
            extras[key] = value;
        }
        return new UnitInitialState(hp, states, extras);
    }

    private static (StateType State, int Stacks) ParseState(string value, string raw, string context)
    {
        string[] parts = value.Split(':');
        if (parts.Length is < 1 or > 2) throw Invalid(raw, context, $"State 格式应为 <StateType>[:<层数>]：{value}");
        string typeText = parts[0].Trim();
        StateType type;
        if (int.TryParse(typeText, out int numeric))
        {
            if (!Enum.IsDefined(typeof(StateType), numeric)) throw Invalid(raw, context, $"StateType 编号不存在：{typeText}");
            type = (StateType)numeric;
        }
        else if (!Enum.TryParse(typeText, ignoreCase: true, out type) || !Enum.IsDefined(type))
        {
            throw Invalid(raw, context, $"StateType 名称不存在：{typeText}");
        }
        if (type == StateType.None) throw Invalid(raw, context, "StateType 不能是 None。");
        int stacks = 1;
        if (parts.Length == 2 && (!int.TryParse(parts[1].Trim(), out stacks) || stacks <= 0))
            throw Invalid(raw, context, $"状态层数必须是正整数：{value}");
        return (type, stacks);
    }

    /// <summary>把解析结果写入单位：先按模板上限截断生命，再按配置顺序叠加状态。</summary>
    public static void Apply(IUnitInstance unit, UnitInitialState state)
    {
        if (unit == null || state == null) return;
        if (state.Hp is int hp) unit.HP = Math.Clamp(hp, 1, Math.Max(1, unit.Max_HP));
        foreach ((StateType type, int stacks) in state.States) StateSystem.AddOrUpdateState(unit, type, stacks);
    }

    /// <summary>解析并应用；空白文本直接跳过。</summary>
    public static void Apply(IUnitInstance unit, string raw, string context = "")
    {
        if (string.IsNullOrWhiteSpace(raw)) return;
        Apply(unit, Parse(raw, context));
    }

    private static ArgumentException Invalid(string raw, string context, string reason) =>
        new($"单位初始值无效{(string.IsNullOrWhiteSpace(context) ? string.Empty : $"（{context}）")}：{reason}；原始文本 = \"{raw}\"");
}
