# State 牌 vs Skill 施加 State

"状态"在战斗中有两种产生方式：作为**独立的状态牌**打出，或作为**技能牌**的附加效果。两者生命周期完全不同。

## 一、State 牌的生命周期

1. 打出 State 牌后，该牌移动到**目标单位的状态牌区**（`unit.StatePile`），**不进入弃牌堆**
2. 状态效果持续期间，该牌保留在状态牌区
3. 状态持续时间结束时（衰减到 0 层），将该牌**移回其所属角色的弃牌堆**
4. State 牌占用状态牌区槽位，可被 `ClearState` / `ClearAllStates` 等效果清除

## 二、Skill 牌施加 State

1. 打出带有 `AddState` 效果的 Skill 牌后，Skill 牌**直接进入弃牌堆**
2. 仅施加的状态标记进入目标单位的状态栏（状态牌区**不增加牌**）
3. Skill 牌本身不进入状态牌区，因此**无法**被 `ClearState` 等效果以"牌"的形式移除（但施加的状态标记本身仍可被清除）

## 三、对比

| 类型 | 示例卡牌 | 打出后卡牌去向 | 状态去向 |
|------|----------|---------------|----------|
| State 牌 | 反攻号角 | 目标状态牌区 | 目标状态栏 |
| State 牌 | 勇气铠甲 | 自身状态牌区 | 自身状态栏 |
| State 牌 | 阵地 / 城墙 / 蓄势待发 | 自身状态牌区 | 自身状态栏 |
| Skill + AddState | 统一战线 | 弃牌堆 | 友方状态栏 |

## 四、State 牌回收机制

State 牌 end callback 在 `StateSystem.AddOrUpdateState` 时通过 `stateData.RegisterEndCallbackForLatestStacks` 注册：

- 触发时机：状态从 `unit.States` 移除时（任何原因：衰减、ClearState、ClearAllStates）
- 行为：把状态牌从 `unit.StatePile` 移回 `所属角色` 的 `DiscardPile`
- 适用前提：必须是 `CardCategory.State` 牌且打到了状态牌堆

## 五、当前限制

- 同一张状态牌**当前不支持同时进入多个不同单位的状态牌堆**
- 状态牌本身在状态牌堆时是 `CardCategory.State`，但 `StatePile` 的容器按单位组织，每个单位一份

## 六、相关文档

- [状态系统](../战斗系统/状态系统.md) — 状态生命周期 / IsDebuff/IsElite 4 象限
- [Effect 系统](../战斗系统/Effect系统.md) — `AddState` / `ClearState` / `ClearAllStates` 效果
