# 系统规则总览

> 2026-08-29 重构为「**总-分**」结构。旧版根目录文件已迁移为 `.archived.md` 保留历史。

本目录是项目运行时规则的**单一事实来源（SSOT）**。所有战斗系统 / 卡牌配置 / 实体标识 / 工程规范都按"总-分"两层组织。

---

## 一、战斗系统

| 文档 | 范围 |
|------|------|
| [状态系统](战斗系统/状态系统.md) | State 生命周期、IsDebuff × IsElite **四象限分类**、StateDefinition 字段、ClearFirstNormalDebuff 行为 |
| [Effect 系统](战斗系统/Effect系统.md) | EffectType 完整表、多效果执行顺序、怪物意图格式、反击 / 回合外 |
| [关键词系统](战斗系统/关键词系统.md) | CardKeyWord / KeywordFlag / CardConditionType、AppliedKeywordEntry、卡牌升级 |
| [战斗循环](战斗系统/战斗循环.md) | 玩家回合 / 怪物回合完整流程、状态衰减时机、胜负判定 |
| [目标规则](战斗系统/目标规则.md) | EffectTargetType 方向、`ForcedTaunt` 优先级、怪物 Damage 目标模式 |
| [能量与费用](战斗系统/能量与费用.md) | 能量字段、`actualEnergyCost` 计算、`Card.GetCurrentEnergyCost` 三段优先级、死亡之舞动态费用 |

## 二、卡牌与配置

| 文档 | 范围 |
|------|------|
| [卡牌参数](卡牌与配置/卡牌参数.md) | CardId 8 位编号、CSV 字段、完整 EffectType / EffectTargetType / StateType 表、CSV 文件组织 |
| [State 牌 vs Skill 施加 State](卡牌与配置/State牌与Skill施加State.md) | State 牌进入 `StatePile` 生命周期、Skill 牌施加 AddState 直接进弃牌堆、回收机制 |
| [总体卡牌设计](卡牌与配置/总体卡牌设计.md) | D/C/B/A/S 五级卡牌分级、初始牌组来源 |

## 三、实体与标识

| 文档 | 范围 |
|------|------|
| [UniqueInGameId](实体与标识/UniqueInGameId.md) | 7 位 ID 格式（0/1/3 前缀）、`UniqueIdGenerator`、多玩家实体组织 |

## 四、工程规范

| 文档 | 范围 |
|------|------|
| [开发记录](工程规范/开发记录.md) | 月度完成事项记录位置、记录原则、配套目录结构 |

## 五、UI 使用

| 文档 | 范围 |
|------|------|
| [战斗使用指南](UI使用/战斗使用指南.md) | 战前配置、出牌命令格式、参数规则、UniqueInGameId 使用 |

## 六、数值平衡

| 文档 | 范围 |
|------|------|
| [卡牌数值平衡标准](数值平衡/卡牌数值平衡标准.md) | 卡牌资源价值点数、延迟效果规则（待确认）、卡牌价值与费用 / 等级关系、未评定行为清单 |
| [单位数值平衡标准](数值平衡/单位数值平衡标准.md) | 角色 / 怪物属性价值评定前提（生命跨战斗继承机制）与标准 |

## 七、地图玩法

| 文档 | 范围 |
|------|------|
| [地图玩法](地图玩法/地图玩法.md) | 地图结构（正六边形节点图）、特殊 / 常规节点类型、时间点与昼夜循环；几何示意图见同目录 地图示意图.png |

## 八、装备、材料、道具系统

| 文档 | 范围 |
|------|------|
| [装备、材料、道具系统](装备、材料、道具系统/装备、材料、道具系统.md) | 材料分类（合成 / 打造）、道具（合成台制作）、装备（5 部位佩戴）、与村庄 / 商人 / 夜晚休息的联动 |

---

## 附录 A：旧版文档索引（已迁移）

旧版根目录文件保留为 `.archived.md` 形式（**不删**），便于历史追溯。新内容请参考上述新结构。

| 旧文件 | 新位置 |
|--------|--------|
| `战斗规则.md.archived` | 拆分为 [战斗系统/战斗循环](战斗系统/战斗循环.md) + [战斗系统/目标规则](战斗系统/目标规则.md) + [战斗系统/能量与费用](战斗系统/能量与费用.md) |
| `卡牌参数配置说明.md.archived` | 合并进 [卡牌与配置/卡牌参数](卡牌与配置/卡牌参数.md) |
| `卡牌说明.md.archived` | 迁至 [卡牌与配置/State 牌 vs Skill 施加 State](卡牌与配置/State牌与Skill施加State.md) |
| `实体规则.md.archived` | 迁至 [实体与标识/UniqueInGameId](实体与标识/UniqueInGameId.md) |
| `总体卡牌设计思路.md.archived` | 迁至 [卡牌与配置/总体卡牌设计](卡牌与配置/总体卡牌设计.md) |
| `开发记录规则.md.archived` | 迁至 [工程规范/开发记录](工程规范/开发记录.md) |
| `使用介绍.md.archived` | 迁至 [UI 使用/战斗使用指南](UI使用/战斗使用指南.md) |
| `Effect相关枚举.md.archived` | 合并进 [战斗系统/Effect 系统](战斗系统/Effect系统.md) + [卡牌与配置/卡牌参数](卡牌与配置/卡牌参数.md) |

## 附录 B：核心概念快速索引

| 概念 | 文档位置 |
|------|----------|
| IsDebuff / IsElite 四象限 | [状态系统 §2](战斗系统/状态系统.md#二isdebuff--iselite-四象限分类) |
| 普通弱化定义 | [状态系统 §3.1](战斗系统/状态系统.md#31-isnormaldebuff-定义) |
| ClearFirstNormalDebuff 行为 | [状态系统 §4](战斗系统/状态系统.md#四clearfirstnormaldebuff-行为) |
| 死亡之舞动态费用 | [能量与费用 §3.1](战斗系统/能量与费用.md#31-cardgetcurrentenergycostplayer-三段优先级) |
| 城墙 / 蓄势待发 / 阵地 / 到我身后 | [状态系统 §2.2](战斗系统/状态系统.md#22-当前-csv-状态分布) + [Effect 系统 §3](战斗系统/Effect系统.md#三反击与回合外) + [目标规则 §3](战斗系统/目标规则.md#三城墙forcedtaunt目标重定向) |
| 状态牌 vs Skill 牌 | [卡牌与配置/State 牌 vs Skill 施加 State](卡牌与配置/State牌与Skill施加State.md) |
| 7 位 ID 格式 | [实体与标识/UniqueInGameId](实体与标识/UniqueInGameId.md#三前缀约定) |

## 附录 C：术语对照

| 中文 | 英文 / 代码标识 | 说明 |
|------|------------------|------|
| 状态 | State | 附着在单位身上的标记 |
| 普通弱化 | Normal Debuff | `IsDebuff && !IsElite` |
| 高等弱化 | Elite Debuff | `IsDebuff && IsElite` |
| 反击 | Counter Attack | 回合外受到攻击时对来源还击 |
| 强化 | Buff | 正向状态（`IsDebuff=false`）|
| 弱化 | Debuff | 负向状态（`IsDebuff=true`）|
| 战斗牌 | Battle Card | `CardCategory.Attack` 或 `Skill` |
| 状态牌 | State Card | `CardCategory.State` |
| 通用牌 | Common Card | `DataBase/Card/通用/通用Card.csv` |
| 角色专属牌 | Character Card | `DataBase/Card/<角色>Card.csv` |
