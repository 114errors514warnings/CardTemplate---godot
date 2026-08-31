# P0 #8 状态系统重构方案

> 用户 2026-08-30 提出：在已落地的 P0 #7（`StateDecayTiming` 衰减时机枚举）基础上，进一步做 3 件事：
> 1. 合并 `IsPermanent` 到 `DecayTiming`（`Never` 替代 `IsPermanent=true`）
> 2. 衰减层数从 `int` 改为 `StateDecayMode` 枚举（None / Flat / Half / ClearAll）
> 3. 清除条件做成枚举添加到"衰减时机"段（`OnAttackPlayed` 打出攻击牌后 / `OnDamaged` 受到伤害后）

## 一、枚举扩展

### 1.1 `StateDecayTiming` 加 `Never` + 事件触发

```csharp
public enum StateDecayTiming
{
    Never = 0,           // 永久（替代 IsPermanent=true）
    OnTurnStart = 1,     // 任意回合开始（玩家+怪物）
    OnTurnEnd = 2,       // 任意回合结束（玩家+怪物）
    OnAttackPlayed = 3,  // 打出攻击牌后
    OnDamaged = 4,       // 受到伤害后
}
```

### 1.2 新增 `StateDecayMode`

```csharp
public enum StateDecayMode
{
    None = 0,            // 不衰减
    Flat = 1,            // 按 StacksToRemove 层数衰减（替代 TurnStartDecayAmount=1）
    Half = 2,            // 衰减一半（向上取整）
    ClearAll = 3,        // 全部清除
}
```

## 二、StateDefinition 字段变化

| 操作 | 字段 | 备注 |
|------|------|------|
| **删** | `IsPermanent` | 合并到 `DecayTiming=Never` |
| **删** | `TurnStartDecayAmount` | 合并到 `DecayMode=Flat + StacksToRemove` |
| **加** | `DecayMode` | 新枚举 |
| **加** | `StacksToRemove` | 仅 `Flat` 模式有效 |
| 保留 | `Type` / `Name` / `IsStackable` / `IsDebuff` / `IsElite` / `EffectDescription` / `DecayTiming` | — |

最终 8 字段：Type, Name, IsStackable, IsDebuff, IsElite, DecayTiming, DecayMode, StacksToRemove, EffectDescription（9 项含 EffectDescription）。

## 三、StateSystem.OnTurnStart 重构

**当前问题**（line 489-560）混合了 4 件事：
1. 衰减逻辑（按 `TurnStartDecayAmount`）
2. `TurnStartEffect` 给资源 + 移除
3. `ShieldGuard` 给 3 护盾 + 移除
4. `ShieldCapEqualsHP` cap 护盾

**新设计**：
- **保留**：`OnTurnStart` 函数处理 #2 #3 #4（状态机制效果）
- **删除**：#1 衰减逻辑（由 `StateDecayProcessor` 接管）
- **删除**：#2 #3 内的 `toRemove.Add(TurnStartEffect)` / `toRemove.Add(ShieldGuard)`（由 `ClearAll` 模式 + `StateDecayProcessor` 接管）

**调点顺序**（同一回合开始时）：
```csharp
// 先：OnTurnStart（给资源 / 护盾 / cap）— 此时状态仍存在
StateSystem.OnTurnStart(player);
// 后：ProcessDecayAtTiming（衰减 + 移除）— ClearAll 模式状态被移除
StateDecayProcessor.ProcessDecayAtTiming(player, DecayTrigger.OnTurnStart);
```

## 四、StateDecayProcessor 完整实现

```csharp
public static void ProcessDecayAtTiming(IUnitInstance unit, DecayTrigger trigger)
{
    if (unit == null || unit.States.Count == 0) return;

    List<StateType> toRemove = new();
    List<StateEndedContext> callbacks = new();

    foreach (var pair in unit.States)
    {
        var def = GetDefinition(pair.Key);
        if (def == null) continue;
        if (def.DecayTiming == StateDecayTiming.Never) continue;       // 永久
        if (def.DecayTiming != trigger) continue;                     // 时机不匹配

        switch (def.DecayMode)
        {
            case StateDecayMode.None: continue;
            case StateDecayMode.ClearAll:
                for (int i = 0; i < pair.Value.Stacks; i++)
                {
                    var ctx = pair.Value.ConsumeOneStack(unit, StateEndReason.Expired);
                    if (ctx != null) callbacks.Add(ctx);
                }
                callbacks.AddRange(pair.Value.ConsumeAllCallbacks(unit, StateEndReason.Expired));
                toRemove.Add(pair.Key);
                break;
            case StateDecayMode.Half:
                int halfStacks = (pair.Value.Stacks + 1) / 2;
                for (int i = 0; i < halfStacks && pair.Value.Stacks > 0; i++)
                {
                    var ctx = pair.Value.ConsumeOneStack(unit, StateEndReason.Expired);
                    if (ctx != null) callbacks.Add(ctx);
                }
                if (pair.Value.Stacks <= 0) toRemove.Add(pair.Key);
                break;
            case StateDecayMode.Flat:
                for (int i = 0; i < def.StacksToRemove && pair.Value.Stacks > 0; i++)
                {
                    var ctx = pair.Value.ConsumeOneStack(unit, StateEndReason.Expired);
                    if (ctx != null) callbacks.Add(ctx);
                }
                if (pair.Value.Stacks <= 0) toRemove.Add(pair.Key);
                break;
        }
    }

    if (toRemove.Count > 0)
    {
        foreach (var t in toRemove) unit.States.Remove(t);
        foreach (var ctx in callbacks) unit.OnStateEnded?.Invoke(ctx);
    }
}
```

## 五、CSV 9 列重写

```
StateType,Name,IsStackable,IsDebuff,IsElite,DecayTiming,DecayMode,StacksToRemove,EffectDescription
0,无,FALSE,FALSE,FALSE,Never,None,0,无效果
1,易伤,TRUE,TRUE,FALSE,OnTurnStart,Flat,1,受到的伤害增加50%（向下取整）
2,虚弱,TRUE,TRUE,FALSE,OnTurnStart,Flat,1,造成的攻击伤害降低25%
3,燃烧,FALSE,TRUE,FALSE,Never,None,0,持续受到火焰伤害
4,反击,TRUE,FALSE,FALSE,OnTurnStart,Flat,1,在回合外受到攻击时，对来源进行一次反击（反击不会触发反击）
5,旋风斩,FALSE,FALSE,FALSE,Never,None,0,回合外的攻击会作用于所有敌人
6,增加攻击力,TRUE,FALSE,FALSE,Never,None,0,当前额外攻击伤害提升
7,额外能量,TRUE,FALSE,FALSE,OnTurnStart,ClearAll,0,下回合额外获得能量
8,虚无,FALSE,TRUE,FALSE,Never,None,0,虚无状态
9,勇气铠甲,FALSE,FALSE,FALSE,OnTurnEnd,Flat,1,每打出一张战斗牌后防御一次
10,回合开始效果,TRUE,FALSE,FALSE,OnTurnStart,ClearAll,0,回合开始时获得资源
11,燃血,TRUE,FALSE,FALSE,OnTurnStart,Flat,1,失去生命时获得1点攻击
12,紧咬不放,FALSE,FALSE,FALSE,Never,None,0,攻击施加者时触发其反击
13,持盾防守,FALSE,FALSE,FALSE,OnTurnStart,ClearAll,0,下回合防御+3、攻击-1、禁战斗牌
14,阵地,FALSE,FALSE,FALSE,Never,None,0,回合开始时至多保留等同你当前生命值的护盾值
15,城墙,FALSE,FALSE,FALSE,Never,None,0,当你拥有护盾时，敌人的单体攻击仅会以你为目标
16,蓄势待发,FALSE,FALSE,FALSE,OnTurnStart,ClearAll,0,在本回合保留你的所有战斗牌，当你反击时，打出手牌中的第一张战斗牌作为替代
17,强撑,FALSE,FALSE,FALSE,OnTurnStart,ClearAll,0,每打出一张牌失去1点生命
18,禁战斗牌,FALSE,FALSE,FALSE,OnTurnStart,ClearAll,0,禁止打出战斗牌（回合始移除）
19,战术支援,FALSE,FALSE,FALSE,OnTurnStart,ClearAll,0,本回合不能再抽牌
20,统一战线,FALSE,FALSE,FALSE,OnTurnStart,ClearAll,0,本回合的下一张战斗牌免费
```

## 六、文件改动清单

| # | 文件 | 改动 |
|---|------|------|
| 1 | `Scripts/DataUnit/Card/CardEnum.cs` | 加 `StateDecayMode` 枚举（`StateDecayTiming` 已存在） |
| 2 | `Scripts/DataUnit/State/StateDefinition.cs` | 删 2 字段 + 加 2 字段 + 构造函数更新 |
| 3 | `Scripts/System/Modules/Loading/LoadStateCsv.cs` | 解析新列（删 2 旧列 + 加 3 新列）|
| 4 | `DataBase/State/通用State.csv` | 9 列重写 |
| 5 | `Scripts/System/StateSystem.cs` `OnTurnStart` | 删衰减逻辑（line 501-525）+ 删 `toRemove.Add` 两处（line 544, 550）|
| 6 | `Scripts/System/StateDecayProcessor.cs` | 扩展：按 `DecayMode` 分支（None/Flat/Half/ClearAll）|
| 7 | `Scripts/System/BattleSytem.cs` | 调点顺序调整 + `ExecuteMonsterIntention` 末尾调 `OnAttackPlayed` |
| 8 | `Scripts/System/Battle/CardPlayController.cs` | `PlayHandCard` 末尾按 `CardCategory=Attack` 调 `OnAttackPlayed` |
| 9 | `Scripts/System/EffectSystem.cs` | `ApplyAttack` HP 实际减少时调 `OnDamaged` |

## 七、行为验证

| 状态 | DecayTiming | DecayMode | StacksToRemove | 实际行为 |
|------|-------------|-----------|----------------|----------|
| 勇气铠甲 (9) | OnTurnEnd | Flat | 1 | 玩家回合结束 -1 → 移除（仅本回合）|
| 蓄势待发 (16) | OnTurnStart | ClearAll | 0 | 玩家回合开始 → 立即全部清除 |
| 持盾防守 (13) | OnTurnStart | ClearAll | 0 | 玩家回合开始 → 给 3 护盾 + 立即全部清除 |
| 易伤 (1) | OnTurnStart | Flat | 1 | 玩家回合开始 -1 → 多叠层用 |
| 旋风斩 (5) | Never | None | 0 | 永久，不衰减 |
| 燃烧 (3) | Never | None | 0 | 永久 |

## 八、风险

| 风险 | 缓解 |
|------|------|
| `OnTurnStart` 内删 `toRemove` 影响 `TurnStartEffect` / `ShieldGuard` 移除 | 用 `ClearAll` 模式 + `StateDecayProcessor` 处理；调点顺序确保给资源在先、移除在后 |
| 调点顺序错误导致给资源时状态已移除 | 严格：`OnTurnStart` 在前、`ProcessDecayAtTiming` 在后 |
| `OnAttackPlayed` / `OnDamaged` 触发点多（每次出攻击牌 / 受伤）| 性能可接受（遍历 unit.States 通常 0-5 项）|
| 其他模块引用 `def.IsPermanent` 失败 | 全局搜索 `IsPermanent` 替换为 `DecayTiming == Never` 判定 |

## 九、文档同步（实施后）

- `README/系统规则/战斗系统/状态系统.md` §1.1 重写：双维度（`DecayTiming` + `DecayMode`）+ `StacksToRemove` 三字段
- `README/系统规则/战斗系统/战斗循环.md` §八 同步
- `README/施工文档/2026/2026.08/战斗系统打磨说明.md` 加 P0 #8 实现记录

## 十、不做的事

- 不新增 `OnSkillPlayed` / `OnHealed` 等其他事件触发（按需后续扩展）
- 不影响 P0 #4 #5 #6 动效
- 不影响状态牌堆 / 状态牌回收机制
- 不影响 `ClearState` / `ClearAllStates` 手动清除
- 不动 `EffectType.AddState` / `RemoveState` 效果逻辑
