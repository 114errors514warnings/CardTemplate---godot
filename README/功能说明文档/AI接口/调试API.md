# 调试 API（越权通道）

> 2026-10-02 新增。与[玩家 AI API](README.md) **严格分开**：这一页的指令都**超出玩家范围**，
> 只给自动化测试与排错用；类型名一律以 `debug.` 开头，响应 `permission` 字段写「调试」。

## 为什么要有这一层

改完一个模块后，「从主菜单点进本局 → 打到出问题的那一步」这条链路太长。有了越权通道，
测试可以**直接把世界摆成要验证的样子**（选关 / 改时间点 / 塞一件装备 / 掉血），再只用玩家通道做那一次
真正要验证的操作 —— 这就是[跑测使用说明](跑测使用说明.md) 里「定向 API 验证」的核心。

口径要求：**越权的事只做「摆场景」，判断仍走玩家通道**。例如验背包拖动规则，用 `debug.run.add_bag_item`
摆出物品，再用 `run.bag.drag` 拖；不要用调试指令去改拖动结果。

## 指令总表

### 服务级引导（`debug.game.*`，任何场景都可用）

| type | 字段 | 说明 |
|---|---|---|
| `debug.game.read` | — | 当前场景 / 是否在本局 / 是否有存档 / 端口 |
| `debug.game.new_run` | `characterIds[]`、`seed` | 新建本局并切到运行局场景（**覆盖当前存档**） |
| `debug.game.enter_run` | — | 切到运行局场景（沿用现有存档） |
| `debug.game.enter_menu` | — | 切回主菜单 |
| `debug.game.capture` | `name`、`captureMode` | 任意场景截图到 `Tests/ApiCaptures/` |
| `debug.game.quit` | `amount`（退出码） | 退出进程（自动化收尾，比外部杀进程干净） |

### 运行局（`debug.run.*`）

| type | 字段 | 说明 |
|---|---|---|
| `debug.run.select_level` | `levelId` | **选关**：直接以指定关卡开战（绕过地图、池档位、时间点、可达判定） |
| `debug.run.jump_event` | `eventId` | 直接进入指定事件（绕过地图与节点类型） |
| `debug.run.next_combat` | — | **一键跳关**：进最近的未访问战斗格（无视可达与只读闸门；时间点照常结算） |
| `debug.run.enter_node` | `nodeId` | 无视可达 / 只读闸门直接进入指定格 |
| `debug.run.skip_node` | — | 把当前格标成已访问并推进层内遭遇计数（不打架直接过一关） |
| `debug.run.complete_level` | — | **完成关卡**：把**当前战斗关卡**直接判胜，随后照常走战斗结算（结算面板 / 选卡 / 回地图）。**只认战斗内容**：事件（或没有内容）一律拒绝（`COMPLETE_LEVEL_REJECTED`），事件不能跳过；已结算的战斗也拒绝 |
| `debug.run.back_to_map` | — | 放弃当前内容回到可选地图（不结算、不领取）—— **模块级验证的「随时回地图」捷径** |
| `debug.run.map_state` | — | 全图节点：类型 / 已访问 / 可达 / 当前 / 起终点 |
| `debug.run.set_time_points` | `value` | **直接设时间点（允许负向）**，可把当天耗光来触发营地转场 |
| `debug.run.add_time_points` | `amount` | 加时间点（正向，走正式入口） |
| `debug.run.set_slot_hp` | `slotIndex`、`hp`、`maxHp` | 直接写角色槽血量 |
| `debug.run.set_hand` | `slotIndex`、`hand`、`definitionId` | 直接写左右手装备（不校验部位组合） |
| `debug.run.add_bag_item` | `category`、`definitionKey`、`count` | 直接塞背包物品（不做负荷校验） |
| `debug.run.clear_bag` | — | 清空背包（含随身格里的条目） |
| `debug.run.bag_entries` | — | 原始背包条目（含随身归属 / 数量 / 到期天数） |
| `debug.run.set_carry_slot` | `slot`、`instanceId` | 直接把条目放进随身格 |
| `debug.run.force_bag_gate` | `reason` | **强制背包闸门**：`reason` 空 = 强制放行（内容进行中也能拖）；非空 = 强制按该原因阻断 |
| `debug.run.set_suppress_confirm` | `suppress` | 写「本局游戏内不再显示放弃确认」 |
| `debug.run.abort_run` | — | 放弃本局（清档 + 回主菜单） |
| `debug.run.capture` | `name`、`captureMode` | 运行局截图（不必真有战斗场） |

### 战斗（`debug.battle.*`）—— 原「调试面板」指令 1:1 搬过来

| type | 字段 | 对应面板项 |
|---|---|---|
| `debug.battle.jump_level` / `jump_event` | `levelId` / `eventId` | 跳转关卡 / 跳转事件 |
| `debug.battle.draw_card` / `add_card` / `clear_hand` | `unitId`、`cardId`、`pile`、`count` | 抽牌 / 添加卡牌 / 清空手牌 |
| `debug.battle.add_energy` / `add_moves` / `reset_round` | `unitId`、`amount` | 加能量 / 加移动 / 重置本回合 |
| `debug.battle.damage` / `heal` / `set_hp` / `add_shield` | `unitId`、`amount`、`hp` | 伤害 / 回复 / 设置生命 / 加护盾 |
| `debug.battle.add_state` / `remove_state` | `unitId`、`stateType`、`index`、`stacks` | 添加状态 / 删除状态 |
| `debug.battle.spawn_item` / `spawn_item_to_slot` / `clear_slot` / `equip_to_hand` / `place_trap` | `q`、`r`、`definitionId`、`isEquipment`、`slot`、`hand` | 道具 / 装备类 |
| `debug.battle.spawn_enemy` / `clear_enemies` | `q`、`r`、`unitId`（怪物 Id） | 生成敌人 / 清空敌人（= 判胜） |
| `debug.battle.end_turn` | — | 强制结束回合 |
| `debug.capture` | `name`、`captureMode` | 战斗视口截图（与 `battle.capture` 同一产物目录） |

`pile`：`0` 手牌 / `1` 抽牌堆 / `2` 弃牌堆 / `3` 消耗堆。写指令默认回包是 `BattlefieldSession.DebugSummary()`
（**含玩家看不到的隐藏信息**：状态层数、手牌 / 三个牌堆张数）。

## 用法示例

```powershell
# 0) 开局（从主菜单或任意场景）
POST {"type":"debug.game.new_run","characterIds":[1002,1003,1004]}
# 1) 摆场景：回地图 → 塞一件双手装备 + 一瓶药水 → 打开背包
POST {"type":"debug.run.back_to_map"}
POST {"type":"debug.run.add_bag_item","category":"equipment","definitionKey":10001,"count":1}
POST {"type":"debug.run.add_bag_item","category":"item","definitionKey":301,"count":1}
POST {"type":"run.bag.open"}
# 2) 只用玩家通道做要验证的那一次操作
POST {"type":"run.bag.drag","fromCell":"BagCell_1","toCell":"BagHand_0_0"}   # 期望被规则拒绝并说明原因
# 3) 时间点 / 夜间：把当天耗光 → 必须进营地
POST {"type":"debug.run.set_time_points","value":0.0}
POST {"type":"run.end_day"}
POST {"type":"run.camp.set_watch","mode":"single","watcherSlot":1}
POST {"type":"run.camp.rest"}
# 4) 收尾
POST {"type":"debug.game.capture","name":"bag","captureMode":"compressed"}
POST {"type":"debug.game.quit"}
```

## 安全与边界

- 服务只绑定 `127.0.0.1`；`EnableDebugApi=false` 可整条关掉调试通道（只剩玩家通道）。
- 调试指令**不写进存档约定之外的东西**：改的都是当前存档字段，与玩家操作落档同一份文件；跑测前请按
  [跑测使用说明](跑测使用说明.md) 备份 `user://run_save_v1.json`（脚本会还原）。
- 调试通道的实现文件（`Scripts/Run/RunSession.Debug.cs` 等）与正式逻辑同仓，注释里都标了「超出玩家范围」。
