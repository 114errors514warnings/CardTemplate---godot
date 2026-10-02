// Scripts/Api/ApiContracts.cs
// 本机 AI 接口的传输层契约：请求 / 响应 / 通道划分 / 指令元数据 / 场景句柄。
//
// 口径（2026-10-02 用户指令）：**玩家在界面上真能做的操作**与**超出玩家范围的调试操作**
// 分成两条通道、两组类（`PlayerApi*` = `ApiLane.Player`，`DebugApi*` = `ApiLane.Debug`），
// 类型名前缀 `debug.` 是**唯一判据**（见 `ApiLanes.LaneOf`）。文档、单测与运行时自检都读这条口径。
//
// 本文件不引用 Godot：纯契约，可被纯 .NET 单测直接引用（不 new Godot 对象）。

using System;
using System.Collections.Generic;

/// <summary>API 通道：`Player` = 玩家范围内；`Debug` = 超出玩家范围（只有测试 / 调试会做）。</summary>
public enum ApiLane { Player, Debug }

/// <summary>通道判据与命名口径（唯一真相）：`debug.` 前缀 = 调试通道，其余 = 玩家通道。</summary>
public static class ApiLanes
{
    public const string DebugPrefix = "debug.";
    public const string BattlePrefix = "battle.";
    public const string RunPrefix = "run.";
    public const string MetaPrefix = "api.";

    /// <summary>按类型名前缀判通道。空 / 未知前缀一律算玩家通道（由 `api.catalog` 自检兜住拼写错）。</summary>
    public static ApiLane LaneOf(string type) =>
        !string.IsNullOrWhiteSpace(type) && type.StartsWith(DebugPrefix, StringComparison.Ordinal)
            ? ApiLane.Debug
            : ApiLane.Player;

    public static string Describe(ApiLane lane) => lane == ApiLane.Debug ? "调试" : "玩家";

    /// <summary>取域名（`battle` / `run` / `api` / `debug.battle` / `debug.run`）。</summary>
    public static string DomainOf(string type)
    {
        if (string.IsNullOrWhiteSpace(type)) return string.Empty;
        int separator = type.LastIndexOf('.');
        return separator <= 0 ? type : type.Substring(0, separator);
    }
}

/// <summary>一次 API 请求（各指令字段的并集；用不到的字段留空即可）。</summary>
public sealed class ApiRequest
{
    // —— 通用 ——
    public string Type { get; set; }
    /// <summary>`none` = 不回快照；`summary`（默认）= 摘要；`full` = 完整快照。</summary>
    public string ResponseMode { get; set; }
    public string Detail { get; set; }

    // —— 战斗（沿用 2026-09 起的既有字段名，外部脚本不用改） ——
    public int UnitId { get; set; }
    public int CardId { get; set; }
    public int Q { get; set; }
    public int R { get; set; }
    public bool HasTarget { get; set; }
    public List<ApiHex> Path { get; set; }
    public string InstanceId { get; set; }
    public int Slot { get; set; }
    public int SlotIndex { get; set; }
    public int Index { get; set; }
    public string Hand { get; set; }
    public bool FromCurrentCell { get; set; }
    public bool IsEquipment { get; set; }
    public int Radius { get; set; }
    public string DefinitionId { get; set; }
    public int DefinitionKey { get; set; }
    public int Pile { get; set; }
    public int Count { get; set; }
    public int Amount { get; set; }
    public int Hp { get; set; }
    public int MaxHp { get; set; }
    public int StateType { get; set; }
    public int Stacks { get; set; }

    // —— 截图 / 事件 ——
    public string Name { get; set; }
    public string CaptureMode { get; set; }
    public long AfterEventId { get; set; }
    public int Limit { get; set; }

    // —— 运行局（本局 / 地图 / 背包 / 营地 / 结算） ——
    public string LevelId { get; set; }
    public string EventId { get; set; }
    public int NodeId { get; set; }
    public string Category { get; set; }
    public string Tab { get; set; }
    public int Page { get; set; }
    public string FromCell { get; set; }
    public string ToCell { get; set; }
    public string ClaimKey { get; set; }
    public int RecipeId { get; set; }
    public string Mode { get; set; }
    public int WatcherSlot { get; set; }
    public float Value { get; set; }
    public int Day { get; set; }
    public bool Suppress { get; set; }
    public string Reason { get; set; }
    public List<int> CharacterIds { get; set; }
    public int Seed { get; set; }
}

/// <summary>轴坐标（战场格点）。</summary>
public sealed class ApiHex { public int Q { get; set; } public int R { get; set; } }

/// <summary>一条指令的元数据（`api.catalog` 与纯 .NET 单测读这里，不依赖场景实例）。</summary>
public sealed class ApiCommandInfo
{
    public string Type { get; }
    public ApiLane Lane { get; }
    public string Summary { get; }
    public bool ReadOnly { get; }
    /// <summary>用到的请求字段（给人 / AI 看的提示，不参与校验）。</summary>
    public string Request { get; }

    public ApiCommandInfo(string type, ApiLane lane, string summary, bool readOnly = false, string request = "")
    {
        Type = type;
        Lane = lane;
        Summary = summary;
        ReadOnly = readOnly;
        Request = request;
    }
}

/// <summary>API 响应。`Permission` 自证这条指令属于哪条通道（玩家脚本可据此拒绝越权指令）。</summary>
public sealed class ApiResult
{
    public bool Ok { get; set; }
    public string ErrorCode { get; set; }
    public string Message { get; set; }
    public object Data { get; set; }
    public string Permission { get; set; }
    public string Type { get; set; }
    public long StateVersion { get; set; }

    public static ApiResult Success(string type, ApiLane lane, string message, object data = null) =>
        new() { Ok = true, Type = type, Permission = ApiLanes.Describe(lane), Message = message, Data = data };

    public static ApiResult Fail(string type, ApiLane lane, string code, string message, object data = null) =>
        new() { Ok = false, Type = type, Permission = ApiLanes.Describe(lane), ErrorCode = code, Message = message, Data = data };
}

/// <summary>一组指令（一个场景域 / 一条通道）。`ApiService` 只认这个接口，不认识 Godot 场景。</summary>
public interface IApiDomain
{
    /// <summary>域名（`battle` / `run`）。同域重复注册 → 新句柄覆盖旧的（内容重建 / 换场景）。</summary>
    string DomainName { get; }

    /// <summary>本域提供的指令元数据（静态表，单测直接读）。</summary>
    IReadOnlyList<ApiCommandInfo> Catalog { get; }

    /// <summary>本域实际接上处理器的类型集合（运行期与 `Catalog` 互检，防两处漂移）。</summary>
    IReadOnlyList<string> BoundTypes { get; }

    /// <summary>执行一条本域指令；不属于本域返回 null（交给别的域）。</summary>
    ApiResult Execute(ApiRequest request);
}

/// <summary>
/// 战斗场景交给 API 的句柄：**只有委托 + 会话对象**，不直接暴露场景节点 ——
/// 这样两条通道的元数据表可以在纯 .NET 单测里读取，不必 new Godot 对象。
/// </summary>
public sealed class ApiBattleContext
{
    /// <summary>当前战斗会话（`battle.*` 的规则入口；无战斗时为 null）。</summary>
    public CardSimulator.Battlefield.BattlefieldSession Session { get; set; }
    /// <summary>怪物回合队列（`battle.end_turn` 之后由宿主跑表现）。</summary>
    public Action RunMonsterQueue { get; set; }
    /// <summary>表现 / 队列是否空闲（`battle.wait_idle`）。</summary>
    public Func<bool> IsIdle { get; set; }
    /// <summary>截图：`(name, mode) -> res:// 相对路径`（失败返回 null）。</summary>
    public Func<string, string, string> Capture { get; set; }
    /// <summary>调试通道：跳转到指定关卡（换场景，超出玩家范围）。</summary>
    public Action<string> JumpLevel { get; set; }
    /// <summary>调试通道：跳转到指定事件（换场景，超出玩家范围）。</summary>
    public Action<string> JumpEvent { get; set; }

    public bool HasSession => Session != null;
}

/// <summary>运行局宿主（`RunFlowScene`）交给 API 的句柄：只暴露公开「访问面」，不摊私有字段。</summary>
public sealed class ApiRunContext
{
    public RunFlowScene Scene { get; set; }
    public bool HasScene => Scene != null && Godot.GodotObject.IsInstanceValid(Scene);
}
