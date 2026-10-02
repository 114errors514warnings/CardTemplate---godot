# 六边形战场指令 API

> 2026-10-02 起：本页的 `battle.*` 指令并入**玩家通道**，服务由 autoload 统一提供（端口不变）。
> 新增的 `run.*`（背包 / 时间点 / 营地）与 `debug.*`（越权）见
> [AI接口](../AI接口/README.md) 与 [调试API](../AI接口/调试API.md)；
> 跑测流程见 [跑测使用说明](../AI接口/跑测使用说明.md)。

开发环境启动游戏（任意场景）后即监听 `http://127.0.0.1:17880/api/game/`。服务仅绑定本机，
所有请求都会排入 Godot 主线程并复用 `BattlefieldSession` 的正式规则入口。
`EnableDebugApi=false` 可关掉调试通道（只剩玩家通道）。

## 请求

- `GET /api/game/`：读取进程级摘要（端口 / 已注册域 / 战斗摘要 / 本局摘要）。
- `POST /api/game/`：提交 JSON 指令；`{"type":"api.catalog"}` 可列出全部指令与所属通道。


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
