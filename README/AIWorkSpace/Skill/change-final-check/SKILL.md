---
name: change-final-check
description: "当改动要交付或提交前使用（Use when finishing a change）。用可执行检查代替看起来没问题：按改动类型分级的体检表、通用四步（diff 抽查、抽读文件、链接体检、临时产物清理）、实测可用的链接体检脚本与已知误报基线、跑测后存档哈希核对、文档侧收尾。关键词：收尾、体检、断链、numstat、临时产物、存档哈希。"
---


# 改动收尾体检

> 交付 / 提交前跑一遍：**用可执行检查代替「看起来没问题」**。

## 一、按改动类型分级（细则见 [编译验证规则.md](../../../Skill/编译验证规则.md)）

| 改动类型 | 必做体检 |
|---|---|
| C# 代码（`Scripts/**`、`Tests/**`） | `dotnet build` 0 警告 0 错误 → **`dotnet test Tests\卡牌模拟器.Tests.csproj`** 失败 0 → 相关烟测 / API 点打（见 [烟测选择](../smoke-test-choice/SKILL.md)） |
| 配置（`DataBase/**`） | 定向 `--filter` 测试；「能否进场」类改动加跑 `--battlefield-smoke` |
| 文档（`README/**`、`*.md`） | 链接体检（§三）+ 行尾 / 编码抽查 |
| 场景 / 资源（`Scenes/**`、`Resources/**`、`*.tscn`） | 相关场景烟测；Godot 生成的 `.import` / `.translation` 边车是否齐 |

> **测试命令必须写全（2026-10-05 实测踩坑，假绿）**：本仓库 `Tests/卡牌模拟器.Tests.csproj` **不在 `卡牌模拟器.sln` 里** —— 在根目录直接跑 `dotnet test` 只做 restore + build 就退出，`$LASTEXITCODE = 0`，**一条测试都不执行**（本轮因此白跑两次）。C# 改动一律 `dotnet test Tests\卡牌模拟器.Tests.csproj`；要**可信计数**再加 trx 日志（本机控制台里 `dotnet test` 的「已通过! …」汇总行会被进度重绘吞掉，`| Select-String` / `| Select-Object -Last` 可能抓到空）：
>
> ```powershell
> [Console]::OutputEncoding = [Text.Encoding]::UTF8
> dotnet test Tests\卡牌模拟器.Tests.csproj --nologo --logger 'trx;LogFileName=x.trx' --results-directory '_tmp/testresults'
> $x = [xml] (Get-Content '_tmp/testresults/x.trx' -Encoding UTF8); $c = $x.TestRun.ResultSummary.Counters
> 'outcome=' + $x.TestRun.ResultSummary.outcome + ' total=' + $c.total + ' passed=' + $c.passed + ' failed=' + $c.failed
> ```

## 二、通用四步

1. `git status --short` + `git --no-pager diff --numstat -- <本次文件>`：只应出现本次动过的文件、行数与预期同量级（异常 = 静默污染 → 整体回滚重做）。
   - **未跟踪文件要逐文件清点必须加 `--untracked-files=all`（`-uall`）**：默认 `git status --porcelain` 会把未跟踪目录**折叠成一项**（如 `?? Resources/Images/Characters/FrameV2/`），照它数总数会少算几十条（2026-10-06 实测：折叠口径 137 条 vs `-uall` 162 条）。
   - **PS 里 `-like '??*'` 不是「以 `??` 开头」**：`?` 是单字符通配符，`'??*'` 匹配**任意**两字符开头的行 —— 本轮据此把 125 条已跟踪改动全算成了「未跟踪」。判前缀要用 `$_.Substring(0,2) -eq '??'`。
2. 抽 1~2 个改过的文件**实际读一遍**内容，不要只看统计。
3. 链接体检（§三）——**文档改动必跑**。相对链接是手写高危区：`../` 级数按「改动文件所在目录 → 目标文件」重算一遍，别照搬邻行（2026-10-06 实测：5 篇设施案 + `地图玩法.md` 共 8 条链接少写一级，体检从基线 6 升到 14 才暴露）。
4. 临时产物清理（§五）。

## 三、链接体检（2026-10-03 实测可用）

```powershell
$bad=@(); $files=@(Get-ChildItem -Recurse -File -Filter '*.md' -Path 'README')
foreach($f in $files){ $dir=$f.DirectoryName; $i=0
  foreach($l in [IO.File]::ReadAllLines($f.FullName)){ $i++
    foreach($m in [regex]::Matches($l,'\]\(([^)]+)\)')){
      $t=($m.Groups[1].Value -split '#')[0].Trim()
      if([string]::IsNullOrEmpty($t)){continue}
      if($t -match '^[a-zA-Z]+:'){continue}                       # 跳过 http / file:// 绝对链接
      if($t.Contains('*') -or $t.Contains('|') -or $t.Contains('?')){continue}   # 跳过正则示例
      if(-not (Test-Path -LiteralPath (Join-Path $dir $t))){ $bad+=($f.FullName + ':' + $i + ' -> ' + $t) }
    } } }
'badlinks=' + $bad.Count; $bad
```

**已知误报基线（6 条，属示例占位，不要去改）**：`AgentOps/命名规范.md`（归档块模板里的 `新位置.md` 占位）、`AgentOps/工作守则.md` 与 `AgentOps/问题分析与解决规范.md`（各 1 条 `x` 占位示意）、`AgentOps/示例/8月施工文档.md` 三条示意链接。
→ 判据：**除基线外应为 0**；新增的断链必须修（含顺带发现的既有断链）。
链接写法与搬迁流程见 [文档编写规则 §十一](../../AgentOps/文档编写规则.md)。

> **临时校验脚本用什么写（2026-10-05 实测）**：几何 / 集合 / 组合类自查（枚举格点、BFS 连通性、逐格不变量）用 **Python**（本机 `python` = 3.14 可用，脚本放 `_tmp/xxx.py` 直接 `python _tmp/xxx.py`）。同一件事在 **PowerShell 5.1** 里极易翻车，两个已实测的坑：① 嵌套数组语义 —— `@($a[0], $a[1])` / `@(, $t)` 在参数传递中会被展平，取出的「坐标」变成字符串，`[Math]::Abs($a[0])` 报 `Cannot index into a null array`，配合 `while` 会变成**刷屏死循环**（本次冲出 29 万行报错，须 `Stop-Process` 手动终止；注意**别** `Get-Process powershell | Stop-Process` 盲杀 —— 会连带杀掉 IDE 的 shell 进程池，只杀自己起的那个 PID）；② 编码 —— 编辑器写出的 `.ps1` 是 **UTF-8 无 BOM**，PS 5.1 按 GBK 解码 → 中文乱码甚至 `ParserError`（要么脚本里只用 ASCII 输出，要么先把文件转成 UTF-8 **带 BOM** 再 `-File` 跑）。

## 四、跑测后的存档核对

```powershell
$save = Join-Path $env:APPDATA 'Godot\app_userdata\卡牌模拟器\run_save_v1.json'
(Get-FileHash $save).Hash    # 与跑测前记录的哈希比对：一致 = 跑测自己还原干净
```

2026-10-03 实测：`--run-flow-ui-smoke` 整套 / 单段跑完，玩家存档哈希**不变**（烟测内部 `BackupRunSaveFile` / `RestoreRunSaveFile` 生效）。

## 五、临时产物清理

| 产物 | 处理 |
|---|---|
| `Tests/*.txt` 烟测日志、`Tests/*.png` 截图、`Tests/ApiCaptures/*` | 已被 `.gitignore` 忽略，但**无引用的用完就清**（`Move-Item` 到 `_tmp/`） |
| `*.apibak` 存档备份 | 确认已还原后清掉 |
| 临时 `.ps1` / 中间 json | 同上；不要留在仓库根或 `Tests/` |

> **隔离区里的 `.cs` 必须加 `.discard` 后缀（2026-10-06 实测，方案甲撤除轮）**：把要删的 `*.cs` `Move-Item` 到 `_tmp/` 之后，`dotnet build` 仍会**把它们当源码编进去** —— 主 `卡牌模拟器.csproj` 的默认 glob 是整仓 `**/*.cs`，`_tmp/` 不在排除表里（本轮因此报 38 个 `CS0246`，全部来自隔离区的两份旧测试）。判据：**移完立刻 `dotnet build`**；报错文件名出现在 `_tmp/` 下 → 用 `Get-ChildItem -Recurse -File '_tmp/xxx' | Rename-Item { $_.Name + '.discard' }` 补后缀（`.uid` / `.csv` / `.tscn` 同样建议加，免得 Godot 侧再扫到）。

**删文件不走 `Remove-Item`**（硬安全策略，2026-10-05 又犯一次）：一律 `Move-Item` 到 `_tmp/` 或 `Rename-Item` 加 `.discard`，见 [工作守则 §二](../../AgentOps/工作守则.md)。

> **写前自检（2026-10-05 复盘后补）**：发出任何「清理 / 删除 / 杀进程」命令前，先扫命令文本里有没有 `Remove-Item` / `del` / `erase` / `rm` / `Stop-Process` 字面量 —— 有就改写：删文件 → `Move-Item <path> _tmp\`（或 `Rename-Item <path> <path>.discard`）；停进程 → `Stop-Process -Id <自己 Start-Process -PassThru 的 PID>`，**任何进程名管道进 `Stop-Process` 都禁止**。判据：命令文本零命中上述字面量。归因（规则已落档却仍犯 = 触发面错配）与模板见 [进程纪律](../verify-chain/SKILL.md) §三。

## 六、文档侧

- 完成情况要写两处（案文件顶部 + 当月施工文档新增 `## §N`）→ 见 [文档回写地图](../doc-writeback-map/SKILL.md) §五。
- 新增 / 改名文档后，别忘了同步所在目录的 `README.md` 索引。
