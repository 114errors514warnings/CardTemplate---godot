---
name: commit-discipline
description: "当要 git add、commit 或 push 时使用（Use when committing or pushing）。核心口径是只提交 AI 自己改过的内容：逐路径 git add、禁止 add -A 与 commit -am、遇到工作区漂移先报告并等用户裁定、按代码与文档与配置分批提交、提交前五项清单、已推历史不 rewrite。关键词：提交、漂移、分批、git add。"
---


# 提交纪律

> 用户 2026-10-02 明确口径（本文件是其落档）。**核心：提交 / 推送只包含 AI 自己修改过的内容。**

## 一、只提交自己改过的内容

| ❌ 禁止 | ✅ 正确 |
|---|---|
| `git add -A` / `git add .` 通吃暂存 | 逐路径 `git add -- <文件1> <文件2> …`，只列本次真正编辑过的文件 |
| 把会话中途凭空出现在工作区的改动当成「同源补丁」顺手提交 | 先分辨来源，非自己产生的**先报告**（见 §二） |
| `git commit -am`（会带上所有已跟踪改动） | `git commit` 后跑 `git show --stat HEAD` 复核文件清单 |

## 二、遇到工作区漂移怎么办

漂移 = 其他 Agent / Godot 编辑器 / 用户手改产生的改动（典型：某文件凭空 +1 行、整套资源被删、别的模块的 `.cs` 被改）。

1. **先列出路径 + `git --no-pager diff --stat -- <路径>`** 给用户看一眼；
2. 由**用户裁定**去留，不要自行入库、也不要顺手还原；
3. 如果它导致 build 失败（如删了被引用的文件），**报告并等结论**，不要为了「让 build 绿」去改别人的文件。

## 三、分批提交

- 按性质分批：**代码批**（`Scripts/**`、`Tests/**`）/ **文档批**（`README/**`）/ **配置批**（`DataBase/**`）；不同批次的功能不要混在一个 commit 里，便于回滚与复查。
- 同一批里的文件必须能自证「同一次改动」——把无关文件或漂移混进去就破坏了这个性质。

## 四、提交前清单

| # | 检查 |
|---|---|
| 1 | 代码批：`dotnet build` 0 警告 0 错误 + `dotnet test` 失败 0 |
| 2 | 收尾门：相关烟测 PASS（整套 `RUN_FLOW_UI_SMOKE_PASS` / 单段 / `--battlefield-smoke`），见 [烟测选择](../smoke-test-choice/SKILL.md) |
| 3 | 体检过：diff 抽查、链接体检、临时产物清理，见 [改动收尾体检](../change-final-check/SKILL.md) |
| 4 | `git status --short` 里**只剩待提交的本批文件** + 已知漂移（漂移不 add） |
| 5 | commit message 写**事实**（改了什么 / 验证结论），不要估时、不要「自言自语」，见 [文档编写规则 §一、§二](../../AgentOps/文档编写规则.md) |

## 五、已推历史不 rewrite

已经推送出去的非本人改动，用户口径是「**已经推了就不用撤了，之后注意**」——不 `revert`、不 `force-push` 改写历史。

## 六、命令模板

```powershell
git --no-pager status --short                       # 看清哪些是自己的、哪些是漂移
git add -- Scripts/Run/RunFlowScene.cs Tests/EventChoiceBattleFlagTests.cs
git --no-pager diff --cached --stat                 # 复核暂存内容
git commit -m "…"
git --no-pager show --stat HEAD                     # 复核提交清单
git push
```
