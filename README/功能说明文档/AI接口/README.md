# 本机 AI 接口（两层通道）

> 2026-10-02 起：原来的「战斗指令 API」升级成**一个服务、两条通道**。目的见
> [跑测使用说明](跑测使用说明.md)：改完一个模块用**定向 API 点打**验证，不必每次陪跑整条 UI 烟测。

## 目的

让外部 AI / 自动化工具以**玩家权限**操作当前局（战斗 / 运行局 / 背包 / 营地），并读取足够的状态来选择
下一步动作、复现问题、执行跑测；同时给测试与排错留一条**明确标注的越权通道**。

服务随游戏启动（autoload），默认监听：

```text
http://127.0.0.1:17880/api/game/
```

仅监听本机。可在 `Scenes/Api/ApiService.tscn`（或 Inspector）改 `StartupPort` / 关掉 `EnableDebugApi`。

## 两条通道（不许混）

| 通道 | 类 | 类型名前缀 | 含义 |
|---|---|---|---|
| **玩家 AI API** | `PlayerApiBattle` / `PlayerApiRun` | `battle.*` / `run.*` / `api.*` | 玩家在界面上**真能点的那一下**（移动 / 出牌 / 拾取 / 开背包 / 拖物品 / 结束本天 / 营地操作…）|
| **调试 API** | `DebugApiBootstrap` / `DebugApiRun` / `DebugApiBattle` | `debug.*` | **超出玩家范围**：选关、一键跳关、生成单位与道具、改 HP / 状态、改时间点、清档、截图… |

- 判据唯一：**类型名前缀 `debug.` = 调试通道**，其余 = 玩家通道（`ApiLanes.LaneOf`）。
- 每条响应都带 `"permission":"玩家" | "调试"` 自证；单测 `Tests/ApiCommandCatalogTests.cs` 断死这条边界。
- 两类**不同文件、不同类、不同指令表**；`api.catalog` 会返回全部指令 + 各自通道 + 自检结果 `integrity`。

## 通用约定

- `GET /api/game/` = `api.read`：进程级摘要（端口 / 已注册域 / 战斗摘要 / 本局摘要）。
- `POST /api/game/` = 提交 JSON 指令（字段是各指令字段的并集，见 `api.catalog`）。
- 元指令（服务自带，任何场景可用）：`api.read` / `api.catalog`（= `api.lanes`）/ `api.events`。
- 写指令可用 `responseMode` 控制回包体量：`none`（只回结果）/ `summary`（默认）/ `full`。
- 失败响应带 `errorCode` 与原因文案；被规则拒绝时 `data` 里仍带当刻状态，方便一次拿到「为什么 + 现在什么样」。

## 玩家通道指令

### 战斗（`battle.*`）

| type | 权限 | 用途 |
|---|---|---|
| `battle.state` / `battle.hand` / `battle.inventory` / `battle.unit` / `battle.board` / `battle.inspect_cell` | 只读 | 按需读取状态 |
| `battle.legal_actions` / `battle.legal_move` / `battle.preview_move` / `battle.legal_card` | 只读 | 当前角色可行操作与原因 |
| `battle.events` / `battle.wait_idle` / `battle.capture` | 只读 | 事件流 / 表现是否空闲 / 截图 |
| `battle.select_unit` | 玩家 | 切换当前角色 |
| `battle.move` / `battle.play_card` / `battle.end_turn` | 玩家 | 移动 / 出牌 / 结束回合 |
| `battle.pick_item` / `battle.use_item` / `battle.throw_item` | 玩家 | 拾取 / 使用 / 投掷道具 |
| `battle.equip` / `battle.drop_equipment` | 玩家 | 从当前格装备 / 把手位装备放到当前格 |

### 运行局：时间点 / 夜间（营地）/ 背包 / 地图 / 结算 / 村庄（`run.*`）

| type | 用途 |
|---|---|
| `run.state` | 本局全景：天数 / 剩余时间点 / 模式 / 角色槽 / 负荷 / 界面形态 |
| `run.end_day` | 点「结束当天」→ 进营地（内容进行中 / 结算面板打开时拒绝） |
| `run.camp.state` | 营地：守夜模式 / 篝火饱食度 / 三个面板 / 按钮可用性 / 预览文案 |
| `run.camp.toggle_food` · `toggle_cook` · `toggle_watch` | 点营地三个面板按钮 |
| `run.camp.add_food` · `remove_food` · `cook` | 篝火草稿加 / 取食物、烹饪 |
| `run.camp.set_watch` | 守夜模式（`none` / `rotation` / `single`）+ 单人守夜角色 |
| `run.camp.rest` | 点「休息」：回复 → 新一天 → 淡出回地图 |
| `run.bag.open` · `close` · `toggle` | 背包界面开合（= 顶栏「背包」） |
| `run.bag.state` | 页签 / 页码 / 负荷 / 横幅 / **每格格名 + 拖动载荷** |
| `run.bag.tab` · `character_tab` · `page` · `next_page` · `prev_page` | 页签 / 角色 Tab / 翻页 |
| `run.bag.drag` | **移动物品**：`fromCell` → `toCell`（与鼠标拖放共用同一条落点判定） |
| `run.map.state` · `run.map.toggle` | 地图状态 / 开合世界地图 |
| `run.map.enter_node` · `enter_next` | 点**可达**格进入（不可达一律拒绝） |
| `run.settlement.state` | 结算界面（面板 / 选牌 / 放弃确认 / 未领取物品清单 + **卡牌份候选**） |
| `run.settlement.claim` · `claim_card` · `close_panel` | 领物品（按 `claimKey`）/ 卡牌份里选一张（`slotIndex` + `cardId`）/ 点「关闭」（领完 → 回地图） |
| `run.village.state` | **村庄**：所在格 / 入口 / 离开格 / 可走格 / 设施 / tips / 打开的界面 / 提示行（只读） |
| `run.village.move` · `walk_to` · `exit` | 走相邻格 / 沿相邻格逐格走到目标格 / 走到离开格并回世界地图 |
| `run.village.tips_accept` · `tips_decline` · `close` | 确认 tips 的「进入 / 稍后」、关闭设施界面（锻铁铺 / 餐厅） |
| `run.village.smithy_state` · `smithy_select` · `smithy_craft` | 锻铁铺：状态（只读）/ 选配方 / 点「打造」（0.1 时间点 + 配方金币） |
| `run.village.restaurant_state` · `restaurant_tab` · `restaurant_order` · `restaurant_cook` · `restaurant_sell` | 餐厅：状态（只读）/ 切页签 / 点菜（0.1 + 菜价）/ 现做（0.1 + 材料）/ 卖出（纯交易） |

## 调试通道指令

见 [调试API.md](调试API.md)：`debug.game.*`（开局 / 切场景 / 截图 / 退出）、`debug.run.*`（选关 / 一键跳关 /
改时间点 / 塞物品 / 清档 / 强制放行背包闸门 / **完成关卡**（仅战斗关卡））、`debug.battle.*`（原调试面板的全部指令）、`debug.capture`。

## 相关文档

- [调试API.md](调试API.md) — 越权通道全表与用法
- [技术实现.md](技术实现.md) — 一个服务 + 域注册 + 主线程派发
- [跑测使用说明.md](跑测使用说明.md) — **按模块点打**的验证流程（当前推荐）
- [战斗使用指南](../运行测试说明/战斗使用指南.md) — 界面口径

