using System.Collections.Generic;
using System.Linq;

namespace CardSimulator.Battlefield;

/// <summary>劫匪升级节奏规则（关卡 `BattleRule=BanditEscalation`，如 `F1-006`）。
/// 只接手"低攻 / 高攻 / 逃跑三列意图齐全"的怪物；其余怪物不介入，保持均匀随机。
/// 阈值与意图列位置集中在本类，规则本身不含具体数值，数值仍来自 `Monster.csv` 与 `EnemyIntent.csv`。</summary>
public sealed class BanditEscalationRule : IBattleRule
{
    /// <summary>低攻 / 高攻 / 逃跑在 `Monster.csv` 中的意图列索引（第 1 / 2 / 3 个意图列）。</summary>
    public const int LowIntentIndex = 0;
    public const int HighIntentIndex = 1;
    public const int FleeIntentIndex = 2;
    /// <summary>第几个怪物回合进入高攻阶段：该批次必定高攻，其后等概率。</summary>
    public const int HighAttackBatch = 3;
    /// <summary>逃跑触发条件：累计命中玩家达到该次数后开始判定。</summary>
    public const int FleeTriggerHits = 2;
    /// <summary>达到触发条件后，每批次切逃跑的概率（0–1）。</summary>
    public const double FleeChance = 0.5;

    private readonly HashSet<int> lockedUnits = new();
    private int lastBatch = -1;
    private bool contagionPending;
    private bool contagion;

    public BattleRuleKind Kind => BattleRuleKind.BanditEscalation;

    public bool TrySelectIntention(IntentSelectionContext context)
    {
        if (context == null || context.EnemyId <= 0 || context.Candidates == null || context.Random == null) return false;
        if (!context.Candidates.Contains(LowIntentIndex) || !context.Candidates.Contains(HighIntentIndex) ||
            !context.Candidates.Contains(FleeIntentIndex)) return false;

        BeginBatchIfNeeded(context.IntentBatch);

        int unitId = context.EnemyId;
        bool flee = lockedUnits.Contains(unitId) || contagion;
        if (!flee && context.HitPlayerCount >= FleeTriggerHits && context.Random.NextDouble() < FleeChance)
        {
            flee = true;
            // 传染从"下一批次"起生效：本批次其余个体仍按原规则走。
            contagionPending = true;
        }

        if (flee)
        {
            lockedUnits.Add(unitId);
            context.SelectedIndex = FleeIntentIndex;
            return true;
        }

        if (context.IntentBatch < HighAttackBatch) context.SelectedIndex = LowIntentIndex;
        else if (context.IntentBatch == HighAttackBatch) context.SelectedIndex = HighIntentIndex;
        else context.SelectedIndex = context.Random.NextDouble() < 0.5 ? LowIntentIndex : HighIntentIndex;
        return true;
    }

    private void BeginBatchIfNeeded(int batch)
    {
        if (batch == lastBatch) return;
        lastBatch = batch;
        contagion |= contagionPending;
    }
}
