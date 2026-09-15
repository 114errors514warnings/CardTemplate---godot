# 六边形战场指令 API

开发环境打开六边形战斗场景后，默认监听 `http://127.0.0.1:17880/api/game/`。服务仅绑定本机，所有请求都会排入 Godot 主线程并复用 `BattlefieldSession` 的正式规则入口。

## 请求

- `GET /api/game/`：读取当前战斗状态，包含全部单位（敌方意图、HP、护盾、坐标、Presence）、各角色手牌、当前角色的左右手/随身栏，以及全图地面物品与触发物件实例 ID。
- `POST /api/game/`：提交 JSON 指令。

通用字段：`type` 为指令名；仅 `battle.select_unit` 使用 `unitId` 切换当前角色；其他玩家操作均使用当前角色。格点使用 `q`、`r` 与 `hasTarget:true`；手位 `hand` 为 `left` 或 `right`。

## 玩家指令

| type | 主要字段 | 行为 |
|---|---|---|
| `battle.select_unit` | `unitId` | 选择当前角色 |
| `battle.move` | `path:[{"q":1,"r":0}]` | 提交完整移动路径 |
| `battle.play_card` | `cardId`, `hasTarget`, `q`, `r` | 出牌并指定格点目标 |
| `battle.end_turn` | 无 | 结束回合并启动怪物行动队列 |
| `battle.pick_item` | `instanceId`, `slot` | 将当前格道具拾取到随身栏 |
| `battle.use_item` | `slot` 或 `fromCurrentCell:true, instanceId`, 可选目标 | 使用随身或地面道具 |
| `battle.throw_item` | 同 `battle.use_item`，但必须提供目标 | 向目标格投掷/使用目标型道具 |
| `battle.equip` | `instanceId`, `hand` | 将当前格装备装入手位 |
| `battle.drop_equipment` | `hand` | 将手位装备卸至当前格 |

## 只读测试辅助

这些接口不改变战斗状态，不提供生成单位、修改 HP、固定随机数或直接指定怪物意图等越权能力。

| type | 内容 |
|---|---|
| `battle.select_unit` | 切换当前玩家角色 |
| `battle.legal_actions` | 当前角色实际可移动路径、手牌合法目标与当前格物件 |
| `battle.events` | 本局已发生的移动、攻击、消息与胜负事件 |
| `battle.wait_idle` | 返回怪物回合/表现是否已结束，调用方可轮询 |
| `battle.capture` | 保存当前视口截图；可选 `name` 指定文件名前缀 |

示例：

```json
{
  "type": "battle.play_card",
  "cardId": 11001001,
  "hasTarget": true,
  "q": 1,
  "r": 0
}
```

成功响应为 `{ "ok": true, "message": "指令成功。", "data": { ... } }`；失败响应为 `{ "ok": false, "errorCode": "...", "message": "..." }`。
