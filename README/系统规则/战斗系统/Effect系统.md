# Effect 系统

效果（Effect）是卡牌出牌时实际执行的动作单位。每个 `EffectType` 对应一个具体的"做什么"，由 `EffectSystem` 静态类统一分发。

## 一、EffectType 完整清单

`Scripts/DataUnit/Card/CardEnum.cs:45-63`：

| 值 | 枚举名 | 用途 | 参数 |
|----|--------|------|------|
| 0 | None | 占位 | — |
| 1 | Damage | 造成伤害 | `[1] extraDamage`（可省），`[最后] hitCount`（可省，默认 1）|
| 2 | Shield | 增加护盾 | `[1] extraShield`（可省），`[最后] shieldCount`（可省，默认 1）|
| 3 | AddState | 附加状态 | `[1] stateType`，`[2] stacks`（默认 1）|
| 4 | ClearState | 清除指定状态 | `[1] stateType` |
| 5 | Heal | 治疗 | — |
| 6 | DrawCard | 抽卡 | — |
| 7 | AddCost | 增加能量 | `[1] amount` |
| 8 | ClearAllStates | 清除全部状态 | — |
| 9 | ShieldSlam | 护盾冲撞 | `[1] extraDamage`（可省）|
| 10 | ClearFirstNormalDebuff | 清除普通弱化 | `[1] count`（默认 1）|
| 11 | UpgradeBattleCard | 战斗中升级卡 | `[0] cardTargetType`，`[1] count`（默认 1），`[2] requireKilledTarget` |
| 12 | UpgradePermanentCard | 永久升级卡组卡 | 同上 |
| 13 | DamageByBattleLostHp | 按失去生命造成伤害 | `[1] baseExtraDamage` |
| 14 | AddKeyword | 添加运行时关键词 | `[0] cardTargetType`，`[1] keyword`，`[2] keywordFlag`，`[3] count` |
| 15 | MirrorShieldToAllies | 护盾分给全体友方 | `[1] extraShield`（可省）|
| 16 | RearrangeMonsterTargets | 怪物单攻目标重定向 | （无参数，本回合触发，**新增 2026-08**）|

## 二、EffectTargetType 目标类型

`Params[i][0]` 固定为目标类型：

| 值 | 枚举名 | 行为 |
|----|--------|------|
| 0 | Auto | 有选中目标用选中目标，否则自身 |
| 1 | Self | 自身 |
| 2 | SelectedTarget | 玩家选定的目标（卡牌标记 `NeedTarget=true`）|
| 3 | AllEnemies | 全体敌人（玩家出牌=所有怪物；怪物出牌=所有玩家）|
| 4 | AllUnits | 场上所有单位 |
| 5 | AllAllies | 全体友方（不含自身）|

## 三、多效果卡执行顺序

多效果按 `EffectType` 字段中 `|` 分隔的顺序**依次执行**。

要让某一效果吃到另一效果的加成（先施加易伤再造成伤害），必须把加状态放在伤害前面：

```
EffectType: AddState|Damage
Params:     3;1;2|2
```

→ 效果 1 `AddState`：目标=AllEnemies，stateType=Vulnerable(1)，stacks=2
→ 效果 2 `Damage`：目标=SelectedTarget，伤害吃到易伤加成

## 四、怪物意图

怪物意图（`MonsterInstance.Table`）是一组**预定义的 Effect 序列**，每条 Effect 单独一个 int[]：

```
[int EffectType, ...args]
```

### 4.1 Damage 意图格式

- 旧格式（保持兼容）：`[Damage, 伤害修正值]`
- 新格式：`[Damage, MonsterDamageTargetMode, 伤害修正值]`

| 模式 | 含义 |
|------|------|
| `RandomPerHit=1` | 每次 Damage 各自随机选目标 |
| `RandomSameTargetWithinIntention=2` | 同条意图内多次 Damage 共享同一随机目标 |

只有当 Damage 携带至少 2 个额外参数时，第一个额外参数才被解释为 `MonsterDamageTargetMode`（保持旧格式兼容）。

### 4.2 AddState 意图格式

`[AddState, EffectTargetType, stateType, stacks]`

- `1;6;2` = 对自身添加 2 层 AddAttack
- `2;1;2` = 对本次意图中最近一次 Damage 命中的目标添加 2 层 Vulnerable
- `3;2;1` = 对所有敌人添加 1 层 Weak

### 4.3 怪物攻击目标解析

`MonsterIntentionService.ResolveMonsterTarget` → `ResolveRandomAlivePlayerTarget`：

1. 若玩家拥有 `ForcedTaunt` 状态**且** `Shield > 0`，从这些玩家中**随机**选一个
2. 否则从所有存活玩家中随机选

## 五、反击与回合外

`EffectType.Damage` 触发时（`EffectSystem.ApplyAttack` 内部）：

1. 若目标有 `CounterAttack` 状态，触发一次**反击**（`isCounterAttack=true` 标志）
2. 反击走正常 Damage 路径，但不会再触发反击（避免递归）
3. `IsOutOfTurn` 判断：源 = 玩家 / 目标 = 怪物 = 在玩家回合中（不是"出牌回合"，是"当前 IsPlayerTurn"）
4. 反击会再触发 `TryTriggerRetainAllBattleCardsCounter`：若反击者有 `RetainAllBattleCards` 状态，自动免费打出其手牌中第一张战斗牌

详见 [状态系统](状态系统.md) 反击/蓄势待发小节。

## 六、运行时入口

- **卡牌路径**：`Card.Apply` → 按 `EffectType` switch → `Apply*Effect` 私有方法
- **怪物路径**：`MonsterIntentionService.TryExecuteMonsterEffect` → 内部调 `EffectSystem.Apply*`
- **状态牌**：`StateCardPipeline` 负责把状态牌移到 StatePile 并注册 end callback

## 七、相关文档

- [状态系统](状态系统.md) — 状态生命周期 + IsDebuff/IsElite 4 象限
- [关键词系统](关键词系统.md) — AddKeyword / CardKeyWord
- [战斗循环](战斗循环.md) — 伤害结算在怪物回合的位置
- [卡牌参数](卡牌与配置/卡牌参数.md) — CSV 配置 + 完整 EffectType 表
