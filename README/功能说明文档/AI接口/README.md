# 六边形战斗 AI API

## 目的

本 API 让外部 AI 或自动化工具以玩家权限操作当前六边形战斗，并读取足够的状态来选择下一步动作、复现问题和执行跑测。

服务仅在六边形战斗场景中启用，默认监听：

```text
http://127.0.0.1:17880/api/game/
```

仅监听本机，不对局域网或公网开放。可在 `HexBattleScene` Inspector 用 `EnableCommandApi` 与 `CommandApiPort` 配置。

## 权限边界

API 不提供任何越权调试能力：不重置、固定随机数、生成单位/道具、改 HP/状态、加卡、指定意图或直接推进怪物。

重新开始测试按正常玩家流程退出并重新进入战斗场景。测试武器通过 `FoundationMap.json` 的固定地面物件提供，并由角色正常移动、拾取、装备和卸装。

## 指令

| type | 权限 | 用途 |
|---|---|---|
| `battle.state` | 只读 | 完整战斗快照 |
| `battle.select_unit` | 玩家 | 选择存活角色 |
| `battle.legal_actions` | 只读 | 当前角色可执行的移动、卡牌目标、当前格物件 |
| `battle.move` | 玩家 | 提交合法完整路径 |
| `battle.play_card` | 玩家 | 使用当前手牌并选择目标格 |
| `battle.end_turn` | 玩家 | 结束玩家回合，正常触发怪物回合 |
| `battle.pick_item` | 玩家 | 拾取当前格道具到随身栏 |
| `battle.use_item` | 玩家 | 使用随身或当前格道具 |
| `battle.throw_item` | 玩家 | 向合法目标格使用目标型道具 |
| `battle.equip` | 玩家 | 从当前格装备到左右手 |
| `battle.drop_equipment` | 玩家 | 将手位装备放到当前格 |
| `battle.events` | 只读 | 已发生的消息、移动、攻击和胜负事件 |
| `battle.wait_idle` | 只读 | 查询怪物行动与表现是否结束 |
| `battle.capture` | 只读 | 保存当前视口截图 |

具体 JSON 格式和技术实现见同目录其他文档。
