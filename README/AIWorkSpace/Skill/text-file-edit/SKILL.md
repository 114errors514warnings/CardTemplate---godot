---
name: text-file-edit
description: "当要修改任何文本文件（.cs / .md / .csv / .tscn / .json）时使用（Use when editing any text file）。硬规则是改内容只用 editor 工具、脚本只做只读检查；含两起真实事故（IO.File 脚本截断 RunSession.cs 前 290 行、Set-Content 以 ANSI 写坏中文 tscn）与改完必做的 diff 与编码体检。关键词：编码、UTF-8 BOM、行尾 CRLF、截断、editor。"
---


# 文本文件编辑

> 改任何文本文件（`.cs` / `.md` / `.csv` / `.tscn` / `.json`）都走这份流程。**一句话：只用 editor 工具改内容；脚本只做只读检查。**

## 一、硬规则

| 规则 | 原因 |
|---|---|
| 改内容**只用 editor 工具** | 精确匹配、命中失败即报错，不会静默乱改 |
| **禁止** `Set-Content` / `Out-File` / `[IO.File]::WriteAllText` / `WriteAllLines` 改写项目文本 | 见 §二的两起真实事故 |
| 脚本只用于**读 / 检索 / 统计**（grep、行数、行尾统计、链接体检） | 只读不会破坏文件 |
| 删文件不用 `Remove-Item`（硬安全策略拦截） | 用 `Rename-Item` 加 `.archived` / `.discard`，或 `Move-Item` 到 `_tmp/`，见 [工作守则 §二](../../AgentOps/工作守则.md) |

## 二、两起事故（真实代价）

1. **截断**（2026-10-02）：用 `[IO.File]::ReadAllLines / WriteAllLines` 脚本给 `Scripts/Run/RunSession.cs` 插代码，脚本漏写前半段 → 该文件前 ~290 行被吞。当时该文件有大量未提交改动，`git checkout` 会连带毁掉、VS Code History 与 checkpoints 都没有可用快照 → 只能按全库调用面手工重建。
2. **编码破坏**：`Set-Content -NoNewline` 默认用系统编码（Windows = ANSI/GBK），写含中文的 `.tscn` 会把汉字变成 `?` / 替换字符（U+FFFD）；Godot 侧表现为报错路径里出现替换字符，形如 `res://Scripts/Interact/修改属<U+FFFD>?SetPlayerHealth.cs`。细则见 [tscn_utf8_safe_edit.md](../../../Skill/tscn_utf8_safe_edit.md)。

**唯一例外**：Godot 资源（`.tscn` 等）必须用脚本写时，显式 `[Text.UTF8Encoding]::new($false)`（UTF-8 **无 BOM**），并按同目录已有的写法操作（[tscn_no_bom.md](../../../Skill/tscn_no_bom.md)）。

## 三、行尾（CRLF / LF）

- **本仓库 `core.autocrlf=true`（2026-10-03 实测；无 `.gitattributes`）**：提交时 CRLF 归一为 LF、检出时转回 CRLF → **纯行尾差异不进 diff / 历史**，`git add` 只给一条 `LF will be replaced by CRLF` 提示。因此：
  - 混用行尾可以存在：`AgentOps/命名规范.md` 实测 CRLF=84 + 裸 LF=3，而 `git diff --numstat` 仍只有 `1 1`（无噪声）；
  - 但也**不必**为「统一行尾」跑批量替换 —— 不污染 diff 不代表值得改，属无意义改动。
- editor 写入实测：**新建文件与插入的行都是 CRLF**（本轮 9 份 `SKILL.md` + `.clinerules` + `README/AIWorkSpace/Skill/README.md` 实测「CRLF，裸 LF=0」）。
- 多行 `old_text` 锚点**可用**（本轮在 CRLF 文件上用「两行连续」锚点替换成功）；但锚点越长越容易因不可见差异失配 → 优先**单行唯一锚点**，插行用行号 `insert_line`；大段正文不要用脚本拼（同时踩行尾 + 编码两个坑）。
- **editor 写入的行尾跟随「文件主导行尾」（2026-10-03 实测）**：CRLF 文件里插入的多行块 → 新行都是 CRLF；而**整份为裸 LF 的文件**（实测 `README/施工文档/README.md`，89 行全部裸 LF）改一行后**仍全为 LF**，没有被拉成混用 —— 换句说，不必预先判断、改完照 §四 逐行体检即可。

## 四、改完必检（30 秒）

```powershell
git --no-pager diff --numstat -- <本次改的文件>        # 行数与预期同量级？异常 = 静默污染
```

```powershell
# 编码体检：不应出现 U+FFFD；不应有 UTF-8 BOM
$p = '<文件绝对路径>'
$b = [IO.File]::ReadAllBytes($p)
if ([Text.Encoding]::UTF8.GetString($b).Contains([char]0xFFFD)) { '编码损坏：出现 U+FFFD' }
if (($b[0..2] -join ',') -eq '239,187,191') { '出现 UTF-8 BOM' }
"首三字节=$($b[0..2] -join ',')"
```

```powershell
# 逐行行尾体检：打印「裸 LF」所在**行号**（比只数总量更能定位 —— 用来确认 editor 插入的多行块没有把块末换行写成裸 LF）
$p = '<文件绝对路径>'; $b = [IO.File]::ReadAllBytes($p); $lone=@(); $line=1
for($i=0;$i -lt $b.Length;$i++){ if($b[$i] -eq 10){ if(-not ($i -gt 0 -and $b[$i-1] -eq 13)){ $lone += $line }; $line++ } }
"loneLF=$($lone.Count) at=$($lone -join ',')"
```

- 行数量级异常 → **整体回滚重做，不要就地修补**：`git checkout -- <file>`（回滚点：改动前跑 `git status --short` 确认目标文件在 HEAD 干净；未跟踪新文件先留副本）。
- 抽 1~2 个改过的文件**看实际内容**，不要只看统计数字。
- 复核内容时：用 `read_files`，或 `Get-Content -Encoding UTF8`。**本机 PowerShell 5.1 的 `Get-Content` 不加 `-Encoding UTF8` 会按 ANSI 解码**，UTF-8 中文文件显示成乱码 → 会让你把好文件误判成损坏（2026-10-03 实测，详见 [代码检索](../code-search/SKILL.md) §三 坑二）；同理，中文串 grep 不加编码会得到假阴性。

## 五、收尾

- 临时脚本 / 临时 ps1 不要留在仓库；确需保留的中间产物 `Move-Item` 到 `_tmp/`。
- 改动涉及文档索引（新增文件 / 改名）时，同步该目录的 `README.md`，见 [文档回写地图](../doc-writeback-map/SKILL.md)。
