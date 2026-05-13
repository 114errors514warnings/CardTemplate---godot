using System;
using System.Collections.Generic;
using System.Linq;
using CardSimulator;

internal sealed class MonsterIntentionFormatter
{
    private sealed class MonsterIntentionPreviewEntry
    {
        public string EffectName { get; }
        public int Value { get; }
        public int Count { get; private set; }
        public int? TargetUniqueInGameId { get; }
        public string TargetText { get; }

        public MonsterIntentionPreviewEntry(string effectName, int value, int? targetUniqueInGameId, string targetText = "")
        {
            EffectName = effectName;
            Value = value;
            TargetUniqueInGameId = targetUniqueInGameId;
            TargetText = targetText ?? string.Empty;
            Count = 1;
        }

        public void IncreaseCount()
        {
            Count++;
        }
    }

    private readonly BattleSytem battle;

    public MonsterIntentionFormatter(BattleSytem battle)
    {
        this.battle = battle;
    }

    public string FormatSelectedMonsterIntention(MonsterInstance monster)
    {
        if (monster == null || monster.SelectedIntention == null || monster.SelectedIntention.Length == 0)
        {
            return "无";
        }

        List<MonsterIntentionPreviewEntry> previewEntries = new List<MonsterIntentionPreviewEntry>();
        foreach (int[] effectConfig in monster.SelectedIntention)
        {
            MonsterIntentionPreviewEntry previewEntry = BuildMonsterEffectPreviewEntry(monster, effectConfig);
            if (previewEntry == null)
            {
                continue;
            }

            MonsterIntentionPreviewEntry existingEntry = previewEntries.FirstOrDefault(entry =>
                entry.EffectName == previewEntry.EffectName &&
                entry.Value == previewEntry.Value &&
                entry.TargetUniqueInGameId == previewEntry.TargetUniqueInGameId &&
                entry.TargetText == previewEntry.TargetText);
            if (existingEntry != null)
            {
                existingEntry.IncreaseCount();
                continue;
            }

            previewEntries.Add(previewEntry);
        }

        if (previewEntries.Count == 0)
        {
            return "无";
        }

        List<string> effectParts = new List<string>();
        foreach (MonsterIntentionPreviewEntry entry in previewEntries)
        {
            string countSuffix = entry.Count > 1 ? $"*{entry.Count}" : string.Empty;
            string targetSuffix = entry.TargetUniqueInGameId.HasValue
                ? $" 目标{battle.FormatUniqueInGameId(entry.TargetUniqueInGameId.Value)}"
                : (string.IsNullOrWhiteSpace(entry.TargetText) ? string.Empty : $" {entry.TargetText}");
            effectParts.Add($"{entry.EffectName} {entry.Value}{countSuffix}{targetSuffix}");
        }

        return string.Join(" ", effectParts);
    }

    private MonsterIntentionPreviewEntry BuildMonsterEffectPreviewEntry(MonsterInstance monster, int[] effectConfig)
    {
        if (monster == null || effectConfig == null || effectConfig.Length == 0)
        {
            return null;
        }

        EffectType effectType = (EffectType)effectConfig[0];
        int[] effectArgs = MonsterIntentionService.GetMonsterEffectArguments(effectConfig);

        switch (effectType)
        {
            case EffectType.Damage:
                MonsterDamageTargetMode damageTargetMode = MonsterIntentionService.ParseMonsterDamageTargetMode(effectArgs, out int[] damageArgs);
                int? targetUniqueInGameId = damageTargetMode == MonsterDamageTargetMode.RandomSameTargetWithinIntention
                    ? GetMonsterIntentionPreviewTargetUniqueInGameId(monster)
                    : null;
                return new MonsterIntentionPreviewEntry(effectType.ToString(), Math.Max(0, monster.Attack + MonsterIntentionService.GetEffectArgument(damageArgs, 0)), targetUniqueInGameId);

            case EffectType.Shield:
                return new MonsterIntentionPreviewEntry(effectType.ToString(), Math.Max(0, monster.Defend + MonsterIntentionService.GetEffectArgument(effectArgs, 0)), null);

            case EffectType.AddState:
                EffectTargetType targetType = ParseAddStateTargetType(effectArgs);
                int[] addStateArgs = GetAddStateArguments(effectArgs);
                return new MonsterIntentionPreviewEntry(
                    addStateArgs.Length == 0 ? effectType.ToString() : $"{effectType}({(StateType)MonsterIntentionService.GetEffectArgument(addStateArgs, 0)})",
                    MonsterIntentionService.GetEffectArgument(addStateArgs, 1, 1),
                    targetType == EffectTargetType.SelectedTarget ? GetMonsterIntentionPreviewTargetUniqueInGameId(monster) : null,
                    targetType == EffectTargetType.Self ? "自身" : string.Empty);

            case EffectType.ClearState:
                return new MonsterIntentionPreviewEntry(
                    effectArgs.Length == 0 ? effectType.ToString() : $"{effectType}({(StateType)MonsterIntentionService.GetEffectArgument(effectArgs, 0)})",
                    0,
                    null);

            default:
                return new MonsterIntentionPreviewEntry(effectType.ToString(), 0, null);
        }
    }

    private static int? GetMonsterIntentionPreviewTargetUniqueInGameId(MonsterInstance monster)
    {
        if (monster == null || monster.SelectedIntentionTargetUniqueInGameId <= 0)
        {
            return null;
        }

        return monster.SelectedIntentionTargetUniqueInGameId;
    }

    private static EffectTargetType ParseAddStateTargetType(int[] effectArgs)
    {
        if (effectArgs == null || effectArgs.Length == 0)
        {
            return EffectTargetType.Self;
        }

        EffectTargetType targetType = (EffectTargetType)effectArgs[0];
        return Enum.IsDefined(typeof(EffectTargetType), targetType) ? targetType : EffectTargetType.Self;
    }

    private static int[] GetAddStateArguments(int[] effectArgs)
    {
        if (effectArgs == null || effectArgs.Length <= 1)
        {
            return Array.Empty<int>();
        }

        int[] normalizedArgs = new int[effectArgs.Length - 1];
        Array.Copy(effectArgs, 1, normalizedArgs, 0, normalizedArgs.Length);
        return normalizedArgs;
    }
}