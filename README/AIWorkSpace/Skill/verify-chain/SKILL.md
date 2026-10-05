---
name: verify-chain
description: "当改完代码或配置要跑验证时使用（Use when running build, tests or Godot after a change）。给出 dotnet build 到 dotnet test 到烟测的不可颠倒顺序（测试与烟测共用预构建 dll，build 不许与烟测同批并行，否则出现行号与源码对不上的假失败）、按改动类型分级，以及 Godot 进程纪律（只杀自己的 PID、--path 内嵌引号、输出重定向、中文编码）。关键词：dotnet build、dotnet test、旧 dll、进程纪律。"
---


# 验证链与进程纪律

> 改完代码 / 配置要跑验证时看这份。分级口径的权威源是 [编译验证规则.md](../../../Skill/编译验证规则.md)，这里是**动作顺序与进程纪律**。

## 一、顺序铁律（不可颠倒）

```powershell
dotnet build 卡牌模拟器.csproj -v q --nologo        # 期望 0 警告 / 0 错误
dotnet test Tests\卡牌模拟器.Tests.csproj --nologo  # 期望 失败: 0
```

- 测试工程用 `<Reference>` 引用**已构建的 dll**（`.godot/mono/temp/bin/Debug/卡牌模拟器.dll`），不是 `ProjectReference` → 漏 build 就是**拿旧 dll 测**，会出现「代码已改、断言仍按旧行为失败」的假象。
- 同一份 dll 也供 Godot 烟测使用 → **`dotnet build` 与 Godot 烟测不许放进同一批并行命令**。2026-10-03 实测教训：两个命令同批发出被并行调度，烟测用了上一轮 dll，报出「断言行号 / 文案与最新源码对不上」的假失败，白跑一轮。
- 正确顺序：**build →（确认绿）→ test →（确认绿）→ 烟测**；每步**单独发**。

## 二、按改动类型分级（细则见编译验证规则）

| 改动 | build | test | 烟测 / 定向 |
|---|---|---|---|
| `Scripts/**`、`Tests/**` | ✅ 必须 | ✅ 必须（全量） | 见 [烟测选择](../smoke-test-choice/SKILL.md) |
| `DataBase/**`（结构性改动） | ❌ 不跑 | ⚠️ 定向 `--filter` | 「能否进场」类改动跑 `--battlefield-smoke` |
| `README/**`、`*.md` | ❌ | ❌ | 链接体检（[改动收尾体检](../change-final-check/SKILL.md)） |
| `Scenes/**`、`Resources/**`、`*.tscn` | ❌ | ❌ | 相关场景烟测 |

- `--filter` 只用于**定位**失败，不作为交付证据；交付前跑一次全量 test（0.2 s，没有理由省）。

## 三、进程纪律（硬规则）

```powershell
$exe = 'D:\MY\Godot\Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64_console.exe'
$p = Start-Process -FilePath $exe -PassThru -ArgumentList @(
  '--headless','--path','"D:\MY\My Game\卡牌模拟器"',
  '--scene','res://Scenes/Battle/HexBattleScene.tscn','--','--battlefield-smoke') `
  -RedirectStandardOutput 'Tests\smoke-out.txt' -RedirectStandardError 'Tests\smoke-err.txt'
$null = $p.WaitForExit(240000)
"exit=$($p.ExitCode) pid=$($p.Id)"
```

| 纪律 | 说明 |
|---|---|
| 只跟踪自己的 PID | `Start-Process -PassThru` 拿 PID；**禁止** `Get-Process -Name 'Godot*' \| Stop-Process`：用户的编辑器 / 测试窗口是同名 exe，会被一起杀掉 |
| `--path` 带空格必须内嵌双引号 | 写成 `'"D:\MY\My Game\卡牌模拟器"'`；否则 Godot 只吃到 `D:\MY\My`，报 `Invalid project path specified`，看起来像「烟测没输出」 |
| 必须等退出或超时 | `-Wait` 或 `$p.WaitForExit(ms)`；不自退的烟测（图形版 `--story-smoke`）先看日志再只杀自己的 PID |
| 输出重定向到 `Tests\*.txt` | 只挑关键行看：`Select-String 'PASS\|FAIL'`；退出码是最终判据 |
| 只按 **PID** 杀进程 | 终止范围永远只限自己起的那一个：`Stop-Process -Id $p.Id`。**任何「按进程名管道进 `Stop-Process`」都禁止** —— `Get-Process powershell \| Stop-Process` 会杀掉 IDE 的 shell 进程池（2026-10-05 盲杀 32 个进程的实测事故），`Get-Process -Name 'Godot*'` 会连用户编辑器一起杀 |
| 清理临时文件只 `Move-Item` | 命令里**不得出现** `Remove-Item` / `del` / `erase` / `rm`：临时脚本 / 日志 / 截图一律 `Move-Item` 到 `_tmp/`，或 `Rename-Item` 加 `.discard`（硬安全策略见 [工作守则 §二](../../AgentOps/工作守则.md)） |
| 中文输出先设编码 | `[Console]::OutputEncoding = [Text.Encoding]::UTF8`，否则 `dotnet test` / Godot 输出乱码、`Select-String` 过滤失效 |

> **归因（2026-10-05：两条禁令都已落档，却仍各犯一次）**
> 症状：① 收尾时用 `Remove-Item` 删自己写的临时 `.ps1`（应 `Move-Item`）；② 临时脚本写错触发刷屏死循环，用 `Get-Process powershell | Stop-Process` 盲杀，连带杀掉 IDE 的 shell 进程池。
> 起因不是「不知道规则」，而是**规则的触发面错配**：`Remove-Item` 的禁令住在「硬安全策略」清单里，触发动作却是「收尾清理」这一习惯动作；`Stop-Process` 的禁令按 `Godot*` 举例（见上表旧写法），换成 `powershell` 时读者不认为被覆盖。
> 三条补救（本文件 + 常驻锚点 + 写前自检）：
> 1. 本表把禁令改写成**不区分进程名**的形态（上方两行）。
> 2. 仓库根 `.clinerules` 加「两条命令铁律」——常驻锚点每个任务必读，不依赖「碰巧打开了哪份 skill」。
> 3. **写前自检**：发出任何含清理 / 删除 / 杀进程语义的命令前，先扫命令里有没有 `Remove-Item` / `del` / `erase` / `rm` / `Stop-Process` 字面量；有则改写成上表模板再发。判据：**命令文本零命中这些字面量**（文件移动例外：`Move-Item` / `Rename-Item` 不算）。

受限工作区内若 Godot 在启动时报告 `Failed to open 'user://logs/…'` 并随即崩溃，为该次命令加入 `--log-file Tests/<本次烟测>.log`（放在 `--` 前）。Godot 官方命令行支持把日志写到项目相对路径；`Tests/` 在可写工作区内。角色素材图形烟测已用此方式恢复截图。不要为解决日志写入而改动玩家存档路径。

## 四、环境与入口

- 图形版 / 无界面版路径见 [Godot运行环境.md](../../Godot运行环境.md)；C# 项目**必须用 Mono 版**。
- 参数顺序：`--scene <场景>` → `--position 3000,3000`（图形版，把窗口挪到屏幕外）→ **`--`** → 烟测开关（`--run-flow-ui-smoke` 等）。
- 跑测要能**还原环境**：会写档的跑测先备份 `user://run_save_v1.json`，收尾还原并核对哈希（见 [改动收尾体检](../change-final-check/SKILL.md) §四）。
