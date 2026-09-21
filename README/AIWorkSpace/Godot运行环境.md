# Godot 运行环境

## 已验证路径（2026-09-15）

- 图形版：`D:\MY\Godot\Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64.exe`
- 无界面/自动化版：`D:\MY\Godot\Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64_console.exe`

项目使用 C#，必须使用 Mono 版 Godot。

## 无界面启动示例

```powershell
& 'D:\MY\Godot\Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64_console.exe' --headless --path 'D:\MY\My Game\卡牌模拟器' --scene 'res://Scenes/Battle/HexBattleScene.tscn' -- --battlefield-smoke
```

该路径已成功启动场景并加载全部战斗数据。当前 `--battlefield-smoke` 在旧的“单击立即移动”断言处失败；新版移动已改为路径规划与确认流程，更新烟测后再将其作为完整通过依据。

## 烟测环境要求（2026-09-21）

| 烟测类型 | 启动方式 | 能否 `--headless` |
|---|---|---|
| 纯逻辑烟测（数据加载、战斗流程、战场断言） | `--headless --scene ... -- --battlefield-smoke` | ✅ 可以 |
| GUI 输入烟测（如 `--run-flow-ui-smoke`） | **图形版** console exe + 真实窗口 | ❌ 不可以 |

- `--headless` 下 `GetViewport().GuiGetHoveredControl()` 恒为 `<none>`，GUI 点击路由断言会失真 → GUI 烟测的终验环境是**图形版**；可用 `--position 3000,3000` 把窗口挪到屏幕外，避免打断用户。
- 鼠标注入必须**同帧**按下 + 抬起（连续两条 `PushInput`）：窗口在屏幕外/物理光标未移动时，跨帧注入会丢 `pressed`。注入前先 `WarpMouse` + 一条 `InputEventMouseMotion` 建立 hover，并打印 `GuiGetHoveredControl()` 路径自证命中。
- 烟测要能**还原环境**：会新建运行局并写档的烟测，必须在开头备份存档、结束时还原。
- 断言失败要**抛出并带实测值**（`REQUIRE ... 实际 ...`），不要只打印日志。
- 烟测产物（截图 / 日志）统一放 `Tests/`，例如 `Tests/run-flow-ui-smoke.png`。
