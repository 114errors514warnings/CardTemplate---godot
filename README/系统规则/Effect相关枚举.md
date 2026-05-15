# Effect 相关枚举

## EffectType

| 值 | 枚举名 | 说明 |
|----|--------|------|
| 0 | None | 无效果 |
| 1 | Damage | 造成伤害 |
| 2 | Shield | 增加护盾 |
| 3 | AddState | 添加状态 |
| 4 | ClearState | 清除指定状态 |
| 5 | Heal | 治疗 |
| 6 | DrawCard | 抽卡 |
| 7 | AddCost | 增加能量 |
| 8 | ClearAllStates | 清除全部状态 |
| 9 | ShieldSlam | 护盾冲撞 |

## EffectTargetType

| 值 | 枚举名 | 说明 |
|----|--------|------|
| 0 | Auto | 有选中目标则用选中目标，否则默认自身 |
| 1 | Self | 自身 |
| 2 | SelectedTarget | 选定目标 |
| 3 | AllEnemies | 全体敌人 |
| 4 | AllUnits | 所有单位 |

## StateType

| 值 | 枚举名 | 说明 |
|----|--------|------|
| 0 | None | 无 |
| 1 | Vulnerable | 易伤 |
| 2 | Weak | 虚弱 |
| 3 | Ignite | 点燃 |
| 4 | CounterAttack | 反击 |
| 5 | WhirlwindSlash | 旋风斩 |
| 6 | AddAttack | 增加攻击力 |
| 7 | ExtraEnergy | 下回合额外获得能量 |

## MonsterDamageTargetMode

| 值 | 枚举名 | 说明 |
|----|--------|------|
| 1 | RandomPerHit | 每次 Damage 各自随机选择目标 |
| 2 | RandomSameTargetWithinIntention | 同一条怪物意图内的多次 Damage 共用同一个随机目标 |

## 备注

- 卡牌参数中，`Params[i][0]` 固定表示 `EffectTargetType`。
- 怪物意图参数中，第一个整数为 `EffectType`，后续整数为该效果的参数。
- 实际效果与数值结算以代码实现为准，本文仅记录当前枚举映射关系。
- 当前 `AddAttack` 的效果为：来源造成攻击伤害时，额外增加等同层数的伤害。

## 怪物 Damage 意图的目标模式补充

- 旧格式保持兼容：`1` 或 `1;伤害修正值`
	- 表示 `RandomPerHit`，即每次 Damage 结算时各自随机选择一名存活玩家。
- 新格式使用专门的枚举槽位：`1;目标模式;伤害修正值`
	- `1;1;3`：`RandomPerHit`，伤害修正值为 `+3`
	- `1;2;3`：`RandomSameTargetWithinIntention`，伤害修正值为 `+3`
	- `1;2;0`：`RandomSameTargetWithinIntention`，不额外加伤害
- 由于需要兼容旧配置，只有当 Damage 至少携带两个额外参数时，系统才会把第一个额外参数解释为 `MonsterDamageTargetMode`。

## 怪物 AddState 意图格式补充

- 怪物 `AddState` 意图使用格式：`3;目标类型;状态类型;层数`
- 示例：
	- `3;1;6;2`：对自身添加 2 层 `AddAttack`
	- `3;2;1;2`：对本次意图中最近一次 Damage 命中的目标添加 2 层 `Vulnerable`
	- `3;3;2;1`：对所有敌人添加 1 层 `Weak`
- 其中目标类型使用 `EffectTargetType`：
	- `1` = `Self`
	- `2` = `SelectedTarget`
	- `3` = `AllEnemies`
	- `4` = `AllUnits`
