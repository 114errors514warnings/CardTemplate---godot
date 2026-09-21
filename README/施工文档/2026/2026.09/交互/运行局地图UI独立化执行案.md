# 运行局地图 UI 独立化执行案

> 目标：地图不再作为独立场景往返切换，而是成为运行局常驻 UI。战斗、事件、结算都在同一运行局根场景的内容宿主中切换；地图可随时查看，但只有处于选点状态时才能点击节点。

## 完成情况（2026-09-21 整理）

- 已实现：运行局根场景 + 内容宿主、世界地图常驻覆盖层（`WorldMapLayer=30`）、常驻顶部按钮栏（`GlobalButtonLayer=50`；两行布局——通用行 + 剧情专属带（左 `Log`/`隐藏`/`Auto`、右 `跳过`），地图打开时剧情带隐藏）、只读/可选地图开关、内容完成后地图以「已打开」覆盖层叠加（内容不销毁、按钮栏保持内容形态）、事件剧情 UI 让位与战斗 HUD 隐藏、调试面板唯一实例与模态输入、暂停界面独占最高层（`RunUiLayers.Pause=60`）、读档重进结算的接管重试上限。
- 部分实现（与本文方案的差异）：地图状态未做成 `WorldMapInteractionMode` 枚举（用 `RunFlowScene.mapSelectable` + `MapScene.SetReadOnly()` 表达）；`IRunContent` / `RunContentResult` 未引入（改用 `ContentFinished` 等事件回传）；`WorldMapController` / `WorldMapOverlay` 未拆分（`MapScene` 以 `EmbeddedMode` 兼任）；地图按钮不再灰显，改为「未完成时打开只读地图」。
- 未实现：存档 `FlowState`；纯净模式组队后与旧调试入口仍走 `ChangeSceneToFile` 独立场景；节点悬停详情、不可达原因、当天剩余时间点与摘要入口。
- 记录：实现细节与两条瑕疵修复见 [9月施工文档 §18](../9月施工文档.md)。

## 一、范围与不做项

本次实现：

- 新建运行局根场景，常驻持有地图 UI、内容宿主、全局 UI 与调试面板。
- 将当前 `MapScene` 的地图生成、绘制、可达判断、节点点击逻辑拆为地图控制器和地图 UI。
- 地图 UI 仅显示地图本体、路径、节点图标与当前路径高亮。
- 战斗、事件、结算时可打开只读地图；结算完成后恢复可选地图。
- 地图按钮在战斗、事件、结算 UI 中可见。
- 关卡、事件、调试跳转统一通过运行局根场景启动内容。

本次不做：

- 节点悬停详情（只预留只读悬停输入）。
- 地图缩放、地图旋转。
- 地图上显示金币、钥匙、时间、角色信息或任何局内 HUD。
- 重做地图美术与节点图标；视觉稿仅作为布局参考。

## 二、目标场景结构

```text
RunFlowScene
├─ ContentHost
│  ├─ HexBattleScene
│  ├─ RunEventContent
│  └─ SettlementOverlay
├─ WorldMapOverlay
├─ GlobalHud
└─ GlobalDebugPanel
```

- `RunFlowScene` 是新局、继续游戏后的唯一运行局入口。
- `ContentHost` 同时最多有一个内容实例。
- `WorldMapOverlay` 不负责金币、角色、暂停、调试或内容按钮；这些信息继续由当前内容的局内 UI 显示。
- 地图视觉参考：`Images/UI/Map/world-map-ui-reference.png`。

## 三、地图 UI 状态

```csharp
public enum WorldMapInteractionMode
{
    Selectable,
    ReadOnly,
    Hidden,
}
```

| 运行局内容状态 | 地图模式 | 节点左键 | 右键拖动 | 悬停 |
|---|---|---|---|---|
| 等待选择下一节点 | `Selectable` | 允许可达节点 | 允许 | 允许 |
| 战斗进行中 | `ReadOnly` | 禁止 | 允许 | 允许，详情后续实现 |
| 事件进行中 | `ReadOnly` | 禁止 | 允许 | 允许，详情后续实现 |
| 结算未确认 | `ReadOnly` | 禁止 | 允许 | 允许 |
| 场景/内容转场 | `Hidden` 或 `ReadOnly` | 禁止 | 禁止 | 禁止 |

`Selectable` 是唯一允许改变 `CurrentNodeId`、写入节点访问状态、解析并启动下一内容的模式。不能仅通过按钮灰显限制输入，地图控制器必须在节点点击入口再次校验模式。

## 四、地图按钮与局内 UI

### 4.1 战斗

右上控制栏：

```text
定位当前角色 | 地图 | 调试 | 暂停
```

- 正在拖拽卡牌、道具、装备，移动规划中，或单位表现尚未完成时，地图按钮禁用。
- 点击地图后打开 `WorldMapOverlay(ReadOnly)`，内容输入锁定。
- 关闭地图后恢复战斗输入与原有镜头状态。

### 4.2 事件

剧情 UI 右上控制栏（上：通用行；下：剧情专属带，左 Log / 隐藏 / Auto，右 跳过）：

```text
地图 | 调试 | 暂停
Log | 隐藏 | Auto: 关闭          跳过
```

- 打开地图时，剧情推进、Auto、选项与普通地图点击全部锁定，同时剧情专属带整带隐藏。
- 关闭地图后恢复原有台词、Auto 状态与选项状态。

### 4.3 结算

- 结算未确认时地图按钮可打开只读地图。
- 奖励确认完成后，自动切换至 `Selectable` 地图；可选自动展开地图，最终由交互验收决定。

## 五、内容生命周期

新增接口：

```csharp
public interface IRunContent
{
    event Action<RunContentResult> Completed;
    void SetInputLocked(bool locked);
}
```

```csharp
public sealed record RunContentResult(
    RunContentResultType Type,
    string ContentId = "",
    int SourceNodeId = -1);
```

基础结果：

```text
BattleVictory
BattleDefeat
EventClosed
StartLevel
StartEvent
SettlementConfirmed
ReturnToMap
```

流程：

```text
WorldMapOverlay 点击节点
→ RunFlowScene.ResolveNodeContent
→ ContentHost 创建战斗或事件
→ WorldMapOverlay = ReadOnly
→ 内容完成回调
→ 结算或 MapSelectable
```

事件选项 `next.type = Battle`：

```text
事件内容完成
→ 不回地图
→ ContentHost 移除事件内容
→ 启动指定 LevelId
→ 地图保持 ReadOnly
```

## 六、存档迁移

保留并完善：

```text
PendingContentType
PendingContentId
PendingSourceNodeId
```

新增：

```text
FlowState
```

建议枚举：

```text
MapSelectable
BattleActive
EventActive
Settlement
Transitioning
```

新局与继续游戏均先进入 `RunFlowScene`。根场景按 `FlowState + PendingContentType` 恢复地图可选状态、战斗、事件或结算；不再由主菜单直接选择 `MapScene`、`RunBattleScene`、`RunEventScene`。

旧 `PendingEncounter*` 字段暂保留兼容读档；待 `WorldMapContentResolver` 与全部节点内容迁移稳定后统一删除。

## 七、地图控制器拆分

现有 `MapScene` 拆分为：

```text
WorldMapController
  - 版图生成
  - 节点可达判断
  - 节点内容解析
  - 访问状态与存档写入

WorldMapOverlay
  - 节点绘制
  - 路径绘制
  - 鼠标输入与只读拦截
  - 当前路径高亮
  - 地图平移
```

节点内容继续通过：

```text
NormalCombatRule.csv
LevelPool.csv
EventPool.csv
FixedNode.csv
```

解析器返回 `ContentType + ContentId`；地图 UI 不直接切换 Godot 场景。

## 八、调试

- 地图、战斗、事件都使用同一调试面板实例/命令定义。
- 通用命令：跳转关卡、跳转事件、给指定角色装备装备。
- 局外命令：运行局资源、旗标、地图状态。
- 局内命令：手牌、能量、移动、单位状态、物品、陷阱、敌人、回合。
- 调试跳转必须调用 `RunFlowScene.StartLevel/StartEvent`，不得 `ChangeSceneToFile`。

## 九、实施顺序

1. 新建 `RunFlowScene` 与 `ContentHost`，不改内容逻辑。
2. 提取 `WorldMapController`，让当前地图生成和节点解析不依赖 `MapScene`。
3. 创建 `WorldMapOverlay`，实现 `Selectable` 与 `ReadOnly` 输入模式。
4. 将主菜单新局、继续游戏入口改为 `RunFlowScene`。
5. 将 `RunBattleScene` 改为可嵌入内容，输出完成回调。
6. 将 `RunEventScene` 改为可嵌入内容，输出完成回调。
7. 接入战斗、事件、结算的地图按钮与输入锁定。
8. 接入统一调试命令与局外装备确认弹窗。
9. 完成存档 `FlowState` 迁移与旧存档兼容。
10. 删除旧场景往返和直接 `ChangeSceneToFile` 内容跳转。

## 十、验收

- 新局进入根场景并显示可选地图。
- 战斗中地图按钮打开只读地图，点击节点不改变位置或启动内容。
- 事件中地图按钮打开只读地图，关闭后事件台词和选项状态保持。
- 战斗胜利并领取奖励后，地图恢复可选，能点击下一个可达节点。
- 事件 `next=Battle` 连续进入战斗，期间地图始终只读。
- 退出到主菜单后继续游戏，恢复正确的根场景内容与地图模式。
- 地图本体不显示任何金币、钥匙、角色、按钮、说明或调试信息。
- 调试跳转关卡/事件不清档，且走根场景内容宿主。
