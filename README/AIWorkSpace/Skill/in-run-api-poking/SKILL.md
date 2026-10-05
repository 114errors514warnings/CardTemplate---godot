---
name: in-run-api-poking
description: "当要验证界面或运行局交互（背包、营地、时间点、地图、战斗表现）时使用（Use when verifying in-game UI or run-loop behaviour）。主力手段是起一次游戏再用本机 HTTP 接口点打：含复制即用的完整会话脚手架（备份存档、等接口就绪、断言、quit、还原、核对哈希）、四条要点（UTF8 字节 body、被拒绝也是 JSON、看 permission、一次启动只付一次代价）、debug 与玩家通道红线、截图。关键词：API 点打、127.0.0.1 接口、Invoke-RestMethod、摆场景。"
---


# 局内 API 点打

> 改**界面 / 运行局**（背包、营地、时间点、地图、战斗表现）时的主力验证方式：起一次游戏，用本机 HTTP 接口把世界摆成要验的样子，只执行改动模块那几步，读响应断言。
> 指令表与各模块配方见 [跑测使用说明](../../../功能说明文档/AI接口/跑测使用说明.md)；越权清单见 [调试API.md](../../../功能说明文档/AI接口/调试API.md)。

## 一、一次会话的完整模板（复制即用，别自创）

```powershell
$exe  = 'D:\MY\Godot\Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64_console.exe'
$proj = 'D:\MY\My Game\卡牌模拟器'
$save = Join-Path $env:APPDATA 'Godot\app_userdata\卡牌模拟器\run_save_v1.json'
$h0   = if (Test-Path $save) { (Get-FileHash $save).Hash } else { 'none' }          # 开跑前留哈希
if (Test-Path $save) { Copy-Item -LiteralPath $save -Destination ($save + '.apibak') -Force }

$p = Start-Process -FilePath $exe -PassThru -ArgumentList @(
  '--path', '"' + $proj + '"', '--position', '3000,3000') `
  -RedirectStandardOutput 'Tests\api-out.txt' -RedirectStandardError 'Tests\api-err.txt'
Start-Sleep -Seconds 15      # 等接口就绪；起不来先看 Tests\api-err.txt（多半是 --path 引号问题）

$u = 'http://127.0.0.1:17880/api/game/'
function Api($json) {
  try   { Invoke-RestMethod -Uri $u -Method Post -Body ([Text.Encoding]::UTF8.GetBytes($json)) `
            -ContentType 'application/json; charset=utf-8' -TimeoutSec 20 }
  catch { $s = (New-Object IO.StreamReader($_.Exception.Response.GetResponseStream())).ReadToEnd()
          $s | ConvertFrom-Json }        # 被规则拒绝是 400，但响应体仍是 JSON
}

Api '{"type":"debug.game.new_run"}'      # 摆场景（调试通道）
Api '{"type":"run.bag.open"}'            # 判断（玩家通道）；配方见跑测使用说明 §四
Api '{"type":"debug.game.quit"}'         # 收尾：干净退出（退出码 0）

if (Test-Path ($save + '.apibak')) { Copy-Item -LiteralPath ($save + '.apibak') -Destination $save -Force }
if (Test-Path $save) { "saveHash 前后：$h0 -> $((Get-FileHash $save).Hash)" }
```

## 二、四条要点（栽过的都在这）

1. `-Body` 传 **UTF8 字节数组**（`[Text.Encoding]::UTF8.GetBytes($json)`）—— PowerShell 5.1 的原生参数有引号坑；**中文 JSON 别用 `curl.exe`**（会吃引号）。
2. **被拒绝也是 JSON**：HTTP 400 的响应体里有 `ok=false` + `errorCode` + 原因 + 当刻状态，所以 `catch` 里也要解析（这是断言「越权被拦」最省事的路径）。
3. 每条响应都有 `permission`（`玩家` / `调试`）——顺手确认自己没跑到越权通道上。
4. **一次启动只付一次代价**：起游戏 ≈ 分钟级。先把这一轮要验的步骤**全部列好**一次跑完；**不要**拿到空 body / 报错就换一种写法重启重来（2026-10-03 实测：这样连搓 4 个临时 `.ps1`、重启 4 次，全废）。

## 三、通道红线

- `debug.*` 前缀 = **越权**，只用于**摆场景**（选关 / 跳关 / 塞物品 / 改时间点 / 加时间点）。
- **判断**走玩家通道（`battle.*` / `run.*`）——否则验的不是玩家能走的路。
- 新增指令按这条分文件（`DebugApi*` / `PlayerApi*`）、分表登记；`Tests/ApiCommandCatalogTests.cs` 会断死边界。

## 四、发现 API 缺口怎么办

新功能做完就补 API（用户口径）：

1. 玩家界面上能做的 → 玩家通道（`PlayerApi*`）+ 在 `RunFlowScene` / UI 类上开一个「等同点一下」的窄口；
2. 只有测试才需要的摆场景能力 → 调试通道（`DebugApi*` + `RunSession.Debug.cs` / `BattlefieldSession.Debug.cs`）；
3. 两处都要：① 表里加一条 `ApiCommandInfo`；② 绑上处理器。运行时 `api.catalog` 的 `integrity` 会自检出不一致。

## 五、截图

```powershell
Api '{"type":"debug.game.capture","name":"bag","captureMode":"compressed"}'   # 产物在 Tests/ApiCaptures/
```

截图必须**图形版**（headless 下 viewport 取不到纹理）；出图后人工核对，不要只信断言。
