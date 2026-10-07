---
name: smoke-test-choice
description: "当要跑烟测（整套或单段 --run-flow-ui-smoke、--battlefield-smoke、--story-smoke 等）时使用（Use when choosing which smoke test to run）。先选档位再谈跑法：选档表、单段档位与产物行、段间纪律（进战斗的段尾必须把世界收回地图态）、图形版与 headless 的能力差异、存档备份与还原、断言的输出差异判据。关键词：烟测、单段、PASS 行、IsOnMap、图形版。"
---


# 烟测选择

> 要跑烟测时看这份：**先选档位，再谈跑法**。启动命令与进程纪律见 [验证链与进程纪律](../verify-chain/SKILL.md) / [Godot运行环境.md](../../Godot运行环境.md)。

## 一、选档表（按「我要验证什么」选）

| 我要验证的 | 跑哪个 | 实测耗时 |
|---|---|---|
| 纯逻辑规则（`Scripts/**`、`Tests/**`） | `dotnet test`（定位时加 `--filter`） | ≈ 0.2 s |
| 某一条 UI 流程段（背包 / 地图 / 结算 / 战斗 / 事件 / 营地 / 读档其中之一） | `--run-flow-ui-smoke=<段名>` | ≈ 6 s |
| 整条运行局流程（跨段联动） | `--run-flow-ui-smoke`（整套） | ≈ 14.4 s |
| 战斗场地 / 数据表 / 关卡配置能否进场 | `--battlefield-smoke`（headless） | 秒～十几秒 |
| 像素素材拼接 | `--animation-material-smoke` | —— |
| 剧情浮层 | `--story-smoke` | —— |
| 界面 / 运行局交互（改了按钮、拖放、面板） | **定向 API 点打**（主力），见 [局内API点打](../in-run-api-poking/SKILL.md) | 1～3 分钟 |

判据：**改动只落在一段 → 只跑那一段；跨段联动 → 跑整套；只在真界面里能验的 → 走 API 点打。**

## 二、单段档位（`--run-flow-ui-smoke=<段名>`，2026-10-03 新增）

```powershell
$p = Start-Process -FilePath $exe -PassThru -ArgumentList @(
  '--path','"D:\MY\My Game\卡牌模拟器"',
  '--scene','res://Scenes/Run/RunFlowScene.tscn','--position','3000,3000','--',
  '--run-flow-ui-smoke=event-battle') `
  -RedirectStandardOutput 'Tests\smoke-seg-out.txt' -RedirectStandardError 'Tests\smoke-seg-err.txt'
```

| 项 | 口径 |
|---|---|
| 段名 | `event-battle`（危险事件战斗选项点下即进战）、`time-point-camp`（时间点闸门 / 营地 / 食物烹饪 / 休息结算）、`place`（地点关：村庄设施 tips / 锻铁铺 / 树林搜寻 / 商人买入 / 离开格回地图）；大小写不敏感 |
| 产物行 | 成功 `RUN_FLOW_UI_SMOKE_SEGMENT_PASS: <段名>`（退出码 0）；失败 `RUN_FLOW_UI_SMOKE_SEGMENT_FAIL: <段名>: <原因>`（退出码 1） |
| 断言口径 | 与整套**共用同一条**建档 / 存档备份 / 收尾路径，不缩水 |
| 新增段 | 在 `RunFlowScene.RunUiSmokeSegment` 的 switch 里登记一行，段方法自带「摆场景 → 断言 → 收尾」 |

**它真正的价值是隔离失败**：无关段挂、前段残留污染都不会算进本段的结论。裸 `--run-flow-ui-smoke` 仍是**整套**（提交门），不要拿它当定位工具。

## 三、整套跑法

同上命令去掉 `=<段名>`；末尾须打印 `RUN_FLOW_UI_SMOKE_PASS`（失败 `RUN_FLOW_UI_SMOKE_FAIL: <原因>`）。
必须用**图形版 console exe**（GUI 命中类断言在 headless 下失真），窗口用 `--position 3000,3000` 挪到屏幕外。

**PASS 与 FAIL 不在同一条流上**（2026-10-07 实测）：`RUN_FLOW_UI_SMOKE_PASS` 走 stdout（`GD.Print`），
`RUN_FLOW_UI_SMOKE_FAIL: <原因>` + 堆栈（含 `RunFlowScene.cs:line N`）走 **stderr**（`GD.PrintErr`）。
只轮询 `*-out.txt` 会得到「进程已退出、既无 PASS 也无 FAIL」的假象 —— **判据要 out 与 err 两个文件一起看**。

玩家自己的 Godot 编辑器窗口开着时会占住 `http://127.0.0.1:17880/api/game/`，err 里会刷几条
`[API] 启动失败：… conflicts with an existing registration` —— 与 UI 烟测**无关**（UI 烟测直接驱动界面，不走局内 API），
不要据此判失败；要清干净只能由用户自己关编辑器窗口，**不要按进程名杀 Godot**。

## 四、`--battlefield-smoke`（headless）

```powershell
& $exe --headless --path '"D:\MY\My Game\卡牌模拟器"' --scene 'res://Scenes/Battle/HexBattleScene.tscn' -- --battlefield-smoke
```

- 末尾须为 `BATTLEFIELD_SMOKE_PASS: …`；前面还有一批独立前缀断言（如 `BATTLEFIELD_MONSTER_TABLE_PASS`、`BATTLEFIELD_LEVEL_CONFIG_PASS`）。
- 它固定加载烟测夹具 `DataBase/Battlefield/FoundationMap.json`，不借正式地图 —— 正式地图改版不会打断它。
- **断言与数据解耦**：不要写死人数 / 怪物数 / 能量上限等随配表变化的数字，否则配表一改烟测就红。
- 关卡编组变化会让 `BATTLEFIELD_LEVEL_CONFIG_PASS` 里的「怪物总数」变化（例：74 → 68），属**可接受的输出差异**，不是失败。

## 五、段间纪律（血泪规则）

- **进入战斗内容的烟测段，段尾必须把世界收回地图态**：`run.CompletePendingEventToMap(); ApiBackToMap();`。
  否则留下 `GameMode = InBattleStart`，后续段末句 `run.IsOnMap` 断言会挂（营地休息流程本身不写 `GameMode`，只有 `CompletePendingEventToMap` / `CompleteSettlementToMap` 写 `OnMap`）。
- 新段优先做成「自带摆场景 + 收尾」的独立方法，并登记进单段 switch —— 这样以后可以单独跑，不会连带别人。

## 六、图形版 vs headless

| 能力 | headless | 图形版 |
|---|---|---|
| 纯逻辑 / 数据加载 / 战场断言 | ✅ | ✅ |
| 截图（`GetViewport().GetTexture()`） | ❌ 返回 null（会崩在 SavePng） | ✅ |
| GUI 命中（`GuiGetHoveredControl()`） | ❌ 恒 `<none>` | ✅ |
| 鼠标注入 | —— | 必须**同帧** press + release；注入前先 `WarpMouse` + 一条 `InputEventMouseMotion`，并打印命中控件路径自证 |

## 七、存档与产物

- 会写档的烟测必须**开头备份、结束还原** `user://run_save_v1.json`（`%APPDATA%\Godot\app_userdata\卡牌模拟器\run_save_v1.json`）。
- 产物（截图 / 日志）按约定落在 `Tests/` 根与 `Tests/ApiCaptures/`，`.gitignore` 已忽略；**无引用的临时日志用完就清**（`Move-Item` 到 `_tmp/`），不要留在工作区里污染 `git status`。
- 断言失败要**抛出并带实测值**（`REQUIRE … 实际 …`），便于区分「没到那一步」与「值不对」。
