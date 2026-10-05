---
name: code-search
description: "当要找代码定义、调用点、字符串常量，或要读源码时使用（Use when locating a definition or call site）。含本仓库三个检索口的真实能力边界：read_files 读内容、递归 grep 找代码（Scripts 只有这条路）、search_codebase 只索引 README 与产物；并列出具坑：Select-String -Path 通配不递归、Get-Content 默认 ANSI 解码导致乱码与中文模式假阴性、search_codebase 命中被 .godot 缓存淹没、read_files 带行区间返回 outdated 的解法。关键词：grep、search_codebase、read_files、否定结论复核。"
---


# 代码检索

> 找代码定义 / 调用点，或读源码时看这份。**先选对工具**：本仓库三个检索口的能力边界不一样。

## 一、工具分工（照这个选）

| 我要干什么 | 用什么 | 备注 |
|---|---|---|
| 读源码 / 文档内容、按行区间看、**定位锚点** | `read_files` | 可一次多文件、可带 `start_line/end_line`，**并行发**；要看结构就看内容，**别用 exec 统计猜** |
| 找代码定义、调用点、字符串常量 | exec 里的**递归 grep** | 见 §三；`Scripts/**` 只有这条路 |
| 搜文档 / 测试产物 / **旧路径与引用残留** | `search_codebase` | 一次调用可带**多个 pattern**；**优先于自写 PowerShell 正则遍历**，见 §二 |
| 查历史结论 / 用户口径 / 本项目特性 | `hil__search_project_memory`、`hil__get_project_context`（先 `hil__index_project` 建索引） | 开工先查再动手 —— 实测能一次拿回「验证口径分级」「BagUi 闸门语义」这类既有口径，省掉重复踩坑 |
| git、`dotnet build`/`dotnet test`、烟测、字节级体检（BOM / CRLF / U+FFFD）、文件移动 | exec | **这些没有 MCP 等价物**，只能 exec；但**一次调用批量合并**，且同一份体检**不要重复跑** |

## 二、`search_codebase` 的真实索引范围（2026-10-03 实测）

实测（2026-10-03；同日先测 `Searched 306 files`，本轮新增文件后补测 `Searched 324 files`）：

- 搜 `static void Require(`、`RunSessionReconstructionSmoke`、`RestoreRunSaveFile`（三者只存在于 `Scripts/Run/RunFlowScene.cs`）→ **全部 0 命中**；
- 搜 `BATTLEFIELD_SMOKE_PASS` → 只命中 `README/**` 文档、`Tests/*.txt` 烟测日志、`.ai-memory/**`（连刚生成的烟测日志都索引了）。

**结论**：它的索引 ≈ `README/**` + `Tests/` 产物 + `.ai-memory/**` + `_tmp/**`，**不含 `Scripts/` 源码**。
→ **不要在 `search_codebase` 上搜代码符号**：每次 0 命中都是一次白往返。搜文档 / 日志 / 记忆时它很好用。

**有效用例（2026-10-03 实测，优先用它而不是自写脚本）**：一次调用并行发 3 个 pattern（`Skill/(9 个中文 skill 名)`、`\]\((轮次纪律|代码检索|文本文件编辑)\.md\)`、`clinerules`）→ 立刻给出全库 10 处指向旧扁平 skill 的引用，**全部落在 `_tmp/skill-flat-backup/`**（备份目录），并确认 `Skill/` 下已无旧引用。这比自写 PowerShell 正则遍历 187 份 md 更快、覆盖更广（324 files，含 `_tmp/`），且不会踩 `[IO.File]::*` 的 CWD 坑。

## 三、递归 grep 模板（找代码）

```powershell
Get-ChildItem -Recurse -File -Filter '*.cs' -Path 'Scripts' |
  Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
  Select-String -Pattern 'Foo'
```

- 正则 / 搜索串用**单引号**包（避免 `$` `(` 被 PowerShell 解释）。
- `-Filter '*.cs'` 之外要搜多种类型时，改用 `-Include '*.cs','*.tscn'`。
- **坑一**：`Select-String -Path 'Scripts\**\*.cs'` **不递归** —— PowerShell 的 `-Path` 通配不跨目录分隔符，`Scripts\**\*.cs` 等价于「`Scripts\<某一个目录>\*.cs`」（只一层）。用它找「谁调用了 Foo」会漏掉 `Scripts/Battlefield/Presentation/*.cs` 这类两级路径。
- **坑二：`Get-Content` / `Select-String` 的默认编码**。本机是 Windows PowerShell 5.1，两者不加 `-Encoding UTF8` 时按 **ANSI（GBK）** 解码 UTF-8 文件，后果有两种，都很容易被误读：
  1. **显示乱码** → 你会误判「文件坏了」（2026-10-03 实测：`Get-Content` 无参数读 `Skill/*.md` 得 `# Skill 鈥?鏃ュ父鎿嶄綔娴佺▼`，按字节验却是完好的无 BOM UTF-8）；
  2. **中文模式的假阴性** → 用中文串 grep 会 0 命中（同次实测：探 `含「何时用」` 得 0/10，加编码后正常）。
  3. **行数被合并、行号整体前移**（2026-10-04 实测）→ GBK 解码会把某些「中文尾字节 + 换行」吃成一个双字节字符，于是 `Get-Content | Select-Object -Skip N -First M` 给出的**行号与 `read_files` 差几十行**（同一次会话里读 `BattlefieldSession.cs` 差 70 行），看起来像「文件被别的 Agent 改了 / 内容对不上」。**按行区间读文件一律 `-Encoding UTF8`（或 `[IO.File]::ReadAllLines`），行号以 `read_files` 为准**；`Get-Content` 无编码时的行号不可当证据。
  ```powershell
  Get-Content -LiteralPath $f -Encoding UTF8      # 或直接 [IO.File]::ReadAllLines($f)
  ```
  → 读内容优先用 `read_files`；脚本里要么 `-Encoding UTF8`，要么用 `[IO.File]::ReadAllLines`（它会自行处理 UTF-8）。**对「没搜到 / 全文乱码」这类结论，先怀疑编码，再下结论。**

- **坑三：`[IO.File]::*` 的相对路径不跟 `cd`**。`cd` / `Set-Location` 只改 PowerShell 自己的位置，**.NET 的进程当前目录不变** → `[IO.File]::ReadAllBytes('a\b.md')` 按**进程启动目录**解析，实测会读错文件或报「未能找到路径 … 的一部分」。只读脚本里一律传**绝对路径**：用 `Get-Item` / `Get-ChildItem` 的 `.FullName`，或先 `Join-Path (Get-Location) '相对路径'`。（另注：同一会话里变量会跨命令保留，读失败时可能拿到**上一次的 `$t`**，误判成「文件内容不对」。）

## 四、否定结论必须递归复核

说「没人调用 / 没接线 / 没写断言」之前，必须用 §三 的真递归命令跑一遍；否则很可能把「搜漏了」说成「没实现」（2026-10-02 曾因此差点误报「食物开场效果没接进战斗」）。

## 五、引用要带三要素

引用代码 / 问题时写：**文件:行号** + **一句话梗概** + **状态**（已查证 / 实施中 / 待批 / 已完成）。
不要用 `#1 #2` 指代，见 [问题分析与解决规范 §十三](../../AgentOps/问题分析与解决规范.md)。

## 六、两个检索噪声（2026-10-03 实测）

1. **`search_codebase` 的命中会被 `.godot/editor/filesystem_cache*` 淹没**：本轮搜 `商人|Merchant|Shop`，前 100 条里绝大多数是 `.godot` 缓存里的资源路径行（形如 `名字.translation::OptimizedTranslation::…`），真正的文档命中要往后翻很多。
   - `search_codebase` 没有「限定路径」参数，所以对策是**把 pattern 写成独特串**（例如直接搜 `已修订：见 §19`、`SmithyContext`），或对代码 / 目录内检索改走 §三 的**递归 grep + `-Path`**。
   - 已知噪声源清单：`.godot/**`（缓存）、`.ai-memory/**`、`Tests/*.txt`（烟测日志）、`.hil/index.json`（行号是 JSON 内部序号，不是文件行号）。

2. **`read_files` 带 `start_line/end_line` 可能返回 `[outdated - see the latest file content]`**：本轮实测对同一次会话里已经读过的文件（`README/施工文档/2026/2026.10/交互/商人交互案.md`、`锻铁铺交互案.md`）反复出现，**改前改后都会出现**，不是编辑器写坏文件。
   - 对策（按优先级）：① 改成**不带区间**读整个文件；② 或 exec 取区间：`Get-Content -LiteralPath $f -Encoding UTF8 | Select-Object -Skip N -First M`（务必带 `-Encoding UTF8`，见 §三 坑二）；
   - **不要**因为这一条就把「读不到」当成「文件不在 / 内容为空」。
   - 相反方向的坑：单次 `read_files` 对**很长**的文件会在中间截断（本轮读 `10月施工文档.md` 时中段被截掉约 500 字符）→ 关键段落要么分段读，要么用 exec 精确取区间。
