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

## 二、五条要点（栽过的都在这）

1. `-Body` 传 **UTF8 字节数组**（`[Text.Encoding]::UTF8.GetBytes($json)`）—— PowerShell 5.1 的原生参数有引号坑；**中文 JSON 别用 `curl.exe`**（会吃引号）。
2. **被拒绝也是 JSON**：HTTP 400 的响应体里有 `ok=false` + `errorCode` + 原因 + 当刻状态，所以「被规则拒绝」也要当正常结果解析（这是断言「越权被拦」最省事的路径）。**但别用 `Invoke-RestMethod` 去读它** —— 见第 5 条。
3. 每条响应都有 `permission`（`玩家` / `调试`）——顺手确认自己没跑到越权通道上。
4. **一次启动只付一次代价**：起游戏 ≈ 分钟级。先把这一轮要验的步骤**全部列好**一次跑完；**不要**拿到空 body / 报错就换一种写法重启重来（2026-10-03 实测：这样连搓 4 个临时 `.ps1`、重启 4 次，全废）。
5. **请求一律走 `curl + 文件 body`（2026-10-05 实测，取代上面模板里的 `Invoke-RestMethod`）**：PS 5.1 的 `Invoke-RestMethod` 在 **400 分支**里读响应流会**静默失败**（函数返回 `$null` → 断言里 `$r.ok` 是空的，看起来像「API 没回话」；2026-10-05 的 `run.village.smithy_craft` / `restaurant_cook` 就是这样被误判成「没回响应」，实际两条都规矩地回了 `CRAFT_REJECTED` / `COOK_REJECTED`）。

   ```powershell
   $u = 'http://127.0.0.1:17880/api/game/'
   $body = Join-Path $env:TEMP 'poke-body.json'
   function Api($json) {
     [IO.File]::WriteAllText($body, $json, (New-Object Text.UTF8Encoding($false)))   # UTF-8 无 BOM
     $raw = & curl.exe -s --max-time 60 -X POST -H 'Content-Type: application/json; charset=utf-8' `
       --data-binary ('@' + $body) $u
     if ([string]::IsNullOrWhiteSpace($raw)) { return $null }
     return ($raw | ConvertFrom-Json)
   }
   ```

   - **不要**写 `--data-binary $json` 直接传串：PS 会把内层双引号吃掉 → 服务端回 `REQUEST_ERROR: 't' is an invalid start of a property name`（2026-10-05 实测）。
   - 400 / 200 都返回可解析 JSON；`ConvertFrom-Json` 会把 `\uXXXX` 还原成中文。
   - 脚本文件本身保持 **ASCII（或存成 UTF-8 带 BOM）**：PS 5.1 按 GBK 读无 BOM 的 UTF-8，中文路径 / 文案会乱码甚至 `ParserError`；路径别写死中文 → 用 `$PSScriptRoot` / 枚举 `$env:APPDATA\Godot\app_userdata` 推。

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
