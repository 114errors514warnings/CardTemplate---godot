# Godot 运行环境

## 已验证路径（2026-09-15）

- 图形版：`D:\MY\Godot\Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64.exe`
- 无界面/自动化版：`D:\MY\Godot\Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64_console.exe`

项目使用 C#，必须使用 Mono 版 Godot。

## 无界面启动示例

```powershell
& 'D:\MY\Godot\Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64_console.exe' --headless --path 'D:\MY\My Game\卡牌模拟器' --scene 'res://Scenes/Battle/HexBattleScene.tscn' -- --battlefield-smoke
```

该路径已成功启动场景并加载全部战斗数据。**`--battlefield-smoke` 现在（2026-09-22 修复后）全绿**：`--battlefield-smoke` 不再借用正式地图，而是固定加载烟测夹具 `DataBase/Battlefield/FoundationMap.json`（3 人 + 6 怪 + `(-1,0)` 拾取物，见 `HexBattleScene.SmokeFixtureMapPath`），因此正式地图改版不会再打断它。

- 入口与产物：`--battlefield-smoke` 先跑一批自包含检查（独立前缀，例如 `BATTLEFIELD_BOW_RAY_PASS`、`BATTLEFIELD_ASSET_PATH_PASS`），再跑场景内集成流程，末尾打印 `BATTLEFIELD_SMOKE_PASS: deployment, CSV, click, hover, pan, fixed scale, movement, equipment, items, card pipeline, thrust, burst self exclusion, spatial damage, monster turn, states, victory`。
- 烟测断言要与数据解耦：**不要写死**人数 / 怪物数 / 能量上限等会随配表变化的数字（能量取自 `DataBase/GameVariables.csv` 的 `DefaultEnergyPerTurn`，怪物数取自关卡表），否则配表一改烟测就红。
- 弓箭范围高亮出图：`-- --battlefield-smoke --battlefield-bow-capture`（**图形版** exe，headless 无法出图），产物 `Tests/battlefield-bow-range.png`；黄=六方向完整直线，红=被首个阻挡截断的弹道段。

## 烟测进程管理（2026-09-22，硬规则）

- **只关自己启动的那个进程**：烟测用 `Start-Process -PassThru` 拿 PID，等它自己退出（烟测末尾 `GetTree().Quit()`）。
- **禁止** `Get-Process -Name 'Godot*' | Stop-Process` 之类按名字批量结束：用户的**编辑器 / 正在测试的项目窗口**是同名 exe，会被一起杀掉。确实要强杀时只 `Stop-Process -Id <自己启动的 PID>`。
- 出图/图形烟测用 `--position 3000,3000` 把窗口挪到屏幕外；长跑不退出时先看日志，不要批量清理进程。
- 有些烟测**不会自己退出**（如 `--story-smoke` 在**图形版**下：打开事件剧情后一直停在场景里，方便肉眼查看）。这类用法必须配超时（`Start-Process -PassThru` + `WaitForExit(毫秒)`），超时后只 `Stop-Process -Id <自己拿到的 PID>`；**不要**用按名字批量结束。
- `--story-smoke` 在 `--headless` 下会自检（剧情浮层 + 背景图加载）后**自动退出**并打印 `STORY_SMOKE_PASS` / `STORY_SMOKE_FAIL`（2026-09-22 起）；图形版保持打开，便于人工看 UI。
- 注意：`GetTree().Quit()` 在剧情浮层刚建好时可能被主循环忽略（实测等 2 帧仍挂着不退出，改成等 0.75s 计时器后正常退出）；新烟测收尾统一留一点延时再 `Quit()`。

```powershell
$exe = 'D:\MY\Godot\Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64_console.exe'
$p = Start-Process -FilePath $exe -PassThru -Wait -ArgumentList @(
  '--headless','--path','D:\MY\My Game\卡牌模拟器',
  '--scene','res://Scenes/Battle/HexBattleScene.tscn','--','--battlefield-smoke')
"exit=$($p.ExitCode) pid=$($p.Id)"   # 只跟踪这一个 PID，不碰其它 Godot 进程
```

## 烟测环境要求（2026-09-21）

| 烟测类型 | 启动方式 | 能否 `--headless` |
|---|---|---|
| 纯逻辑烟测（数据加载、战斗流程、战场断言） | `--headless --scene ... -- --battlefield-smoke` | ✅ 可以（2026-09-22 起全绿） |
| 像素素材烟测 | `--headless --scene res://Scenes/Debug/AnimationMaterialTestScene.tscn -- --animation-material-smoke` | ✅ 可以（截图自动跳过） |
| 剧情烟测 | `--headless --scene res://Scenes/Battle/HexBattleScene.tscn -- --story-smoke` | ✅ 可以（自检后自动退出） |
| GUI 输入烟测（如 `--run-flow-ui-smoke`） | **图形版** console exe + 真实窗口 | ❌ 不可以 |
| 出图烟测（`--animation-material-smoke`、`--battlefield-bow-capture`、`--story-capture`） | **图形版** console exe（可 `--position 3000,3000`） | ❌ 不可以 |

- `--headless` 下 `GetViewport().GetTexture().GetImage()` 返回 null：出图会在 `SavePng` 处崩（2026-09-22 实测）。两边都已处理：`AnimationMaterialTestScene.CaptureFrame` 在无窗口时**跳过截图并照常 PASS**，`BattlefieldSceneSmoke.VerifyAssetPaths()` 逐条断言图片路径可解析（图片统一在 `Resources/Images/` 下）。

- `--headless` 下 `GetViewport().GuiGetHoveredControl()` 恒为 `<none>`，GUI 点击路由断言会失真 → GUI 烟测的终验环境是**图形版**；可用 `--position 3000,3000` 把窗口挪到屏幕外，避免打断用户。
- 鼠标注入必须**同帧**按下 + 抬起（连续两条 `PushInput`）：窗口在屏幕外/物理光标未移动时，跨帧注入会丢 `pressed`。注入前先 `WarpMouse` + 一条 `InputEventMouseMotion` 建立 hover，并打印 `GuiGetHoveredControl()` 路径自证命中。
- 烟测要能**还原环境**：会新建运行局并写档的烟测，必须在开头备份存档、结束时还原。
- 断言失败要**抛出并带实测值**（`REQUIRE ... 实际 ...`），不要只打印日志。
- 烟测产物（截图 / 日志）统一放 `Tests/`，例如 `Tests/run-flow-ui-smoke.png`。
