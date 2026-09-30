// EnemyIntentDamageArgs.cs
// 怪物意图 Damage 段的取参口径（代码需求清单 P2-10.4）。
// 配置语义（`Monster.csv` 的意图列 / `EnemyIntent.csv`；口径见 单位数值平衡标准 §2.4）：
//   `1`                     → 无额外参数：伤害 = 攻击力
//   `1;<修正值>`            → **两元素 = 伤害修正值**：伤害 = 攻击力 + 修正值
//   `1;<目标模式>;<修正值>` → 三元素起，第 2 位是 `MonsterDamageTargetMode`，其后为伤害修正值
// 该语义与旧卡牌战斗的 `MonsterIntentionService.ParseMonsterDamageTargetMode`（额外参数只有 1 个时视为伤害修正值）一致。
// 六边形战场此前对 Damage 段统一 `Skip(2)`，两元素写法会把修正值整段丢掉（3115 / 3114 / 3106 / 3107 / 3111），
// 本类把「执行」与「界面预览」两条路径合到同一取参口径上。
// 纯逻辑：不依赖场景与数据加载，便于单测。
using System;

namespace CardSimulator.Battlefield;

public static class EnemyIntentDamageArgs
{
	/// <summary>
	/// Damage 段的伤害修正值（战斗执行与意图预览共用的唯一取值口径）：
	/// 两元素取 `effect[1]`、三元素及以上取 `effect[2]`、其余 0。非 Damage 段一律 0。
	/// </summary>
	public static int GetDamageModifier(int[] effect)
	{
		if (effect == null || effect.Length < 2 || (EffectType)effect[0] != EffectType.Damage)
		{
			return 0;
		}

		return effect.Length == 2 ? effect[1] : effect[2];
	}

	/// <summary>Damage 段交给 `EffectSystem.ApplyAttack` 的参数数组（`[0]` = 伤害修正值，无修正时为空数组）。</summary>
	public static int[] ResolveDamageParams(int[] effect)
	{
		int modifier = GetDamageModifier(effect);
		return modifier == 0 ? Array.Empty<int>() : new[] { modifier };
	}
}
