# Skill — 日常操作流程

> AI Agent 在本项目（卡牌模拟器 / Godot 4.6 + C#）**动手时照着做**的操作流程：动作顺序 + 现成命令 + 踩过的坑。
> 与 [AgentOps](../AgentOps/README.md) 的分工：**AgentOps = 必须遵守什么**（规范 / 边界 / 安全策略）；**本目录 = 怎么做**（步骤 / 命令 / 判据）。两者冲突时以 AgentOps 为准。

## 怎么用（两步）

1. **开工先读本文件**（仓库根 `.clinerules` 已把这条设为常驻规则）——每个任务开始时读这一次。
2. 按「我要干什么」从下表挑 **1 条** skill 打开照着做；不要一次全读（正文进了上下文才有用）。

## 目录

| skill 目录 = frontmatter `name` | 什么时候用 | 关键词 |
|---|---|---|
| [round-discipline](round-discipline/SKILL.md) | 每个任务开始前 —— 少往返 = 少等引擎启动 | 往返、试错、先量数 |
| [code-search](code-search/SKILL.md) | 找代码定义 / 调用点、读源码 | grep、search_codebase、read_files |
| [text-file-edit](text-file-edit/SKILL.md) | 改 `.cs` / `.md` / `.tscn` 等文本文件 | 编码、BOM、行尾、截断 |
| [verify-chain](verify-chain/SKILL.md) | 改完代码要 build / test / 起 Godot | dotnet build、旧 dll、进程 |
| [smoke-test-choice](smoke-test-choice/SKILL.md) | 要跑 `--run-flow-ui-smoke` / `--battlefield-smoke` | 烟测、单段、PASS 行 |
| [in-run-api-poking](in-run-api-poking/SKILL.md) | 改界面 / 运行局（背包、营地、地图、时间点……） | API 点打、摆场景 |
| [change-final-check](change-final-check/SKILL.md) | 交付前：diff 抽查 / 链接体检 / 临时产物清理 | 收尾、体检、断链 |
| [commit-discipline](commit-discipline/SKILL.md) | `git add` / commit / push | 提交、漂移、分批 |
| [doc-writeback-map](doc-writeback-map/SKILL.md) | 功能落地后要同步哪些文档 | 回写、施工文档、索引 |

## 格式（Agent Skills 约定，2026-10-03 起）

一份 skill = **一个目录 + 入口 `SKILL.md`**，首行是 YAML frontmatter：

| 字段 | 要求 |
|---|---|
| `name` | 小写连字符，与**目录名同名**（如 `verify-chain/`）。`name` 要能被加载器当标识符，中文目录名不行 |
| `description` | 一句话「何时用 + 关键词」——**唯一的触发面**：加载器开局只读它，据此决定要不要读正文 |
| 正文 | `# 中文标题` + `> 一句触发说明` + `## 小节`；中文正文原样保留，不因迁移改写 |

- 目录内**不再**另放 `README.md`（入口就是 `SKILL.md`，索引在本文件）。
- 常驻锚点：仓库根 `.clinerules` —— 让「开工先读本文件 + 挑 1 条」跨会话不丢（否则换个会话就不知道有这套东西）。
- 迁移 / 改名：目录深一层 → 文内相对链接的 `../` 多一层，改完必跑链接体检（[change-final-check](change-final-check/SKILL.md) §三）。
- 位置与写法口径：[文档编写规则 §七](../AgentOps/文档编写规则.md)。

规范类文档在 [AgentOps](../AgentOps/README.md)（命名 / 文档编写 / 问题分析 / 数值 / 内容创作 / 工作守则）；
更早的 4 份素材类 skill（`build_check.md`、`编译验证规则.md`、`tscn_no_bom.md`、`tscn_utf8_safe_edit.md`）暂留在 `README/Skill/`，见下「待办」。

## 本目录治的病（为什么有这些 skill）

| 曾经犯的错 | 起因（缺什么） | 现在落在 |
|---|---|---|
| 自搓 `Invoke-WebRequest` + 临时 `.ps1` 做 API 点打，4 次重试 = 重启 4 次游戏 | 手册里有脚手架，但「起游戏 → 点打 → 断言 → 收尾」没有一份可照抄的流程 | [in-run-api-poking](in-run-api-poking/SKILL.md) |
| `dotnet build` 与 Godot 烟测放进同一批并行命令 → 烟测用旧 dll，出现「行号 / 文案对不上源码」的假失败 | 只写了「先 build 再 test」，没写「构建产物与运行构建产物不许同批」 | [verify-chain](verify-chain/SKILL.md) |
| 一次断言失败就重跑整套烟测（约 4 次 × 15 s，且每次都要翻整条流程日志才知道挂在哪段） | 没有单段档位，也没有「先隔离、再全量」的判据 | [smoke-test-choice](smoke-test-choice/SKILL.md)、[round-discipline](round-discipline/SKILL.md) |
| 拿 `Get-Content` 切片当读文件工具；同一处信息分几次才问全 | 没说清 `read_files` / 递归 grep / `search_codebase` 各管什么 | [code-search](code-search/SKILL.md)、[round-discipline](round-discipline/SKILL.md) |
| 在 `search_codebase` 上反复试错（本仓库它**不索引 `Scripts/**`**，0 命中） | 工具的索引边界没有落档 | [code-search](code-search/SKILL.md) |
| 用 `[IO.File]` 脚本改 `Scripts/Run/RunSession.cs`，截断前 ~290 行 | 「改文本只用 editor」只在守则里一句，没有可执行流程 | [text-file-edit](text-file-edit/SKILL.md) |
| 烟测段尾没把世界收回地图态 → 后续段 `IsOnMap` 断言挂 | 段与段之间的污染没有纪律 | [smoke-test-choice](smoke-test-choice/SKILL.md) §五 |
| 差点把其他进程产生的漂移改动一起提交 | 提交口径只存在于会话记忆 | [commit-discipline](commit-discipline/SKILL.md) |

事故完整记录：[10 月施工文档 §17.5](../../施工文档/2026/2026.10/10月施工文档.md)、[问题分析与解决规范 §十四](../AgentOps/问题分析与解决规范.md)。

## 维护口径

- 每次**踩到新坑或查实新工具事实**，就地补进对应 skill（写「症状 → 规则 → 命令 → 判据」），不要把结论只留在会话里。
- 新增 skill 走 `Skill/<skill-name>/SKILL.md` + frontmatter（`name` 同名、`description` 写「何时用 + 关键词」），并登记进上方目录表（口径 [文档编写规则 §七](../AgentOps/文档编写规则.md)）。
- skill 只写**操作流程与判据**；规范 / 边界 / 安全策略的正文归 [AgentOps](../AgentOps/README.md)，这里只引用不抄写。

## 待办

- 本目录已是 **Agent Skills 形态**（目录 + `SKILL.md` + frontmatter）。仓库内**不自动加载**（Cline 走根目录 `.clinerules` 锚点）；若以后要用别的宿主（Claude Code / Cursor / Codex），把 `Skill/` 挂到它的扫描目录（如 `.claude/skills/`）或用 `npx skills add <repo> --skill <name>` 安装即可，正文无需改写。
- `README/Skill/` 下的 4 份（`build_check.md`、`编译验证规则.md`、`tscn_no_bom.md`、`tscn_utf8_safe_edit.md`）与本目录职能重叠，**待用户裁定**是否用 `git mv` 迁入本目录（迁入需同步 7 处引用：`AgentOps` 三份规范、[Godot运行环境.md](../Godot运行环境.md)、[跑测使用说明](../../功能说明文档/AI接口/跑测使用说明.md) 两处）。
- `README/Skill/tscn_no_bom.md` 正文中文已损坏（显示为 `?`），迁入时需按原文重写。
