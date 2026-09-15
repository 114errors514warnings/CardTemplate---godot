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
