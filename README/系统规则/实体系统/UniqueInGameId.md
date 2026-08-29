# 实体唯一标识（UniqueInGameId）

> 摘录自 `README/系统规则/实体规则.md`（2026-08-29 迁移）

## 一、适用范围

- 人物实例（`CharacterInstance`）
- 怪物实例（`MonsterInstance`）
- 卡牌实例（`Card`）

## 二、UniqueID 格式

- 统一使用 **7 位数字字符串/数字**
- 第 1 位为类型前缀，后 6 位为流水号（`000001` - `999999`）

> 代码中的正式字段名为 `UniqueInGameId`，本文中的 UniqueID 指代该字段。

## 三、前缀约定

| 实体类型 | 前缀 |
|----------|------|
| 人物实例 | `0xxxxxx` |
| 怪物实例 | `1xxxxxx` |
| 卡牌实例 | `3xxxxxx` |

## 四、生成规则

通过 `UniqueIdGenerator` 统一生成：

- `NextCharacterId()` — 人物实例 ID
- `NextMonsterId()` — 怪物实例 ID
- `NextCardId()` — 卡牌实例 ID

流水号超过 `999999` 时按 `1` 重新循环。

## 五、代码落点

- `IUnitInstance` 包含 `UniqueInGameId` 字段
- `CharacterInstance` / `MonsterInstance` 在构造时自动赋值
- `Card.GenerateUniqueInGameId()` 为卡牌实例生成 7 位局内唯一 ID
- `BattleSytem.AddCardToPlayer` 在创建卡牌运行时实例后写入该 ID

## 六、多玩家战斗中的实体组织

- `BattleSytem.Players` 维护玩家单位，键为 `UniqueInGameId`
- `BattleSytem.Monsters` 维护怪物单位，键为 `UniqueInGameId`
- `BattleSytem.Player` **仅作为兼容访问器**存在，返回当前第一个可用玩家，**不应再视为唯一真实玩家数据源**

## 七、UI 与命令层的目标引用

- UI 调试命令中，玩家、怪物、卡牌目标的定位均**优先使用 `UniqueInGameId`**
- 多玩家模式下，涉及"指定操作者玩家"的命令应**显式传入**玩家的 `UniqueInGameId`
