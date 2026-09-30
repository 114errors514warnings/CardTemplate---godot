using System;
using System.Collections.Generic;

namespace CardSimulator.Battlefield;

/// <summary>关卡级战斗规则（关卡 CSV 的 `BattleRule` 列）。规则不绑定具体关卡或地图：
/// 任何关卡填上枚举值即可复用；缺列 / 留空等于 <see cref="Default"/>。</summary>
public enum BattleRuleKind
{
    /// <summary>无额外规则：意图按现有均匀随机选择。</summary>
    Default = 0,
    /// <summary>劫匪升级节奏：低攻起步 → 门槛回合高攻 → 之后等概率；命中达阈值后按概率切逃跑，
    /// 进入逃跑后锁定，并在下一批次传染给同类意图的敌人。</summary>
    BanditEscalation = 1,
}

/// <summary>意图选择钩子的上下文。当前只暴露这一个钩子；后续规则需要别的时机时再各自新增。</summary>
public sealed class IntentSelectionContext
{
    /// <summary>第几个玩家回合（`BattlefieldSession.Round`，准备意图时代表"刚结束的那一轮"）。</summary>
    public int Round;
    /// <summary>第几个怪物回合的意图批次，从 1 开始（意图在上一怪物回合结束时准备）。</summary>
    public int IntentBatch;
    /// <summary>该怪物在战斗内的唯一单位 Id（规则按它记状态）。</summary>
    public int EnemyId;
    /// <summary>该怪物实例（需要读模板/已有状态时使用）。</summary>
    public MonsterInstance Monster;
    /// <summary>本次可选的意图列索引（已剔除空列）。</summary>
    public IReadOnlyList<int> Candidates;
    /// <summary>该怪物累计命中玩家的次数（含被护盾格挡）。</summary>
    public int HitPlayerCount;
    /// <summary>随机源（与战斗同一实例，便于复现）。</summary>
    public Random Random;
    /// <summary>规则写入所选意图列索引；保持 -1 表示不介入。</summary>
    public int SelectedIndex = -1;
}

/// <summary>战斗规则：当前只有"意图选择"一个钩子；返回 true 表示已写入 <see cref="IntentSelectionContext.SelectedIndex"/>。</summary>
public interface IBattleRule
{
    BattleRuleKind Kind { get; }
    bool TrySelectIntention(IntentSelectionContext context);
}

/// <summary>枚举 → 规则实例；<see cref="BattleRuleKind.Default"/> 不产生实例（等于无规则）。</summary>
public static class BattleRuleRegistry
{
    public static IReadOnlyList<IBattleRule> Create(IEnumerable<BattleRuleKind> kinds)
    {
        var rules = new List<IBattleRule>();
        if (kinds == null) return rules;
        var seen = new HashSet<BattleRuleKind>();
        foreach (BattleRuleKind kind in kinds)
        {
            if (kind == BattleRuleKind.Default || !seen.Add(kind)) continue;
            rules.Add(kind switch
            {
                BattleRuleKind.BanditEscalation => new BanditEscalationRule(),
                _ => throw new ArgumentException($"未实现的战斗规则：{kind}"),
            });
        }
        return rules;
    }

    /// <summary>解析关卡 CSV 的 `BattleRule` 单元格（可空；支持 `|` 分隔的多个规则）。未知名字直接报错。</summary>
    public static List<BattleRuleKind> Parse(string text, string context)
    {
        var kinds = new List<BattleRuleKind>();
        if (string.IsNullOrWhiteSpace(text)) return kinds;
        foreach (string entry in text.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            string token = entry.Trim();
            if (token.Length == 0) continue;
            if (!Enum.TryParse(token, ignoreCase: true, out BattleRuleKind kind) || !Enum.IsDefined(kind))
                throw new ArgumentException($"战斗规则名无效（{context}）：{token}");
            if (kind != BattleRuleKind.Default && !kinds.Contains(kind)) kinds.Add(kind);
        }
        return kinds;
    }
}
