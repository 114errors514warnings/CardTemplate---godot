// Scripts/Api/ApiService.cs
// 本机 AI 接口的唯一服务（autoload）：持有**唯一监听端口**、主线程队列与域注册表。
//
// 架构（2026-10-02，替换原 `Scripts/Battlefield/Api/BattleApiHost` 的单场景服务）：
//   HTTP 线程 → ApiHttpServer → ApiService.pending → Godot 主线程（本节点 `_Process`）
//   → 域注册表（battle / run）→ PlayerApi* 或 DebugApi*（两条通道，不同类）
// 监听**懒启动**：第一次有域注册才占端口（主菜单里不再白占 17880）。

using Godot;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public partial class ApiService : Node
{
    /// <summary>默认端口（与 2026-09 起的战斗指令 API 保持一致）。</summary>
    public const int DefaultPort = 17880;

    public static ApiService Instance { get; private set; }

    /// <summary>是否启用调试通道（`debug.*`）与服务级引导域（开局 / 切场景 / 退出）。关掉只剩玩家通道。</summary>
    [Export] public bool EnableDebugApi = true;

    /// <summary>启动端口（服务级引导域在游戏启动时就监听；场景侧的 `ApiPort` / `CommandApiPort` 只在更早登记时才生效）。</summary>
    [Export] public int StartupPort = DefaultPort;

    private sealed class Pending
    {
        public ApiRequest Request;
        public TaskCompletionSource<ApiResult> Completion;
    }

    /// <summary>域登记项：同一个域可能被「旧场景」与「新场景」同时登记（换场景时旧场景是延迟释放的），
    /// 因此按 **owner（场景实例）** 记账 —— 注销只摘自己那一条，不会误摘新场景刚登记的。</summary>
    private sealed class Entry
    {
        public IApiDomain Domain;
        public object Owner;
    }

    private readonly ConcurrentQueue<Pending> pending = new();
    private readonly List<Entry> domains = new();
    private ApiHttpServer server;
    private long stateVersion;

    /// <summary>当前监听端口（未启动 = 0）。</summary>
    public int Port { get; private set; }

    public bool IsListening => server != null;

    /// <summary>战斗事件日志（战斗域注册时挂上；无战斗时为 null）。</summary>
    public ApiEventJournal Journal { get; private set; }

    private object journalOwner;

    public override void _EnterTree()
    {
        Instance = this;
        // 服务级引导域（`debug.game.*`）：任何场景都能开局 / 切场景 / 截图 / 退出。
        // 这是「按模块点打验证」的入口 —— 测试不必先从主菜单手点进本局。
        if (EnableDebugApi) Register(new DebugApiBootstrap(this), StartupPort <= 0 ? DefaultPort : StartupPort, this);
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
        server?.Dispose();
        server = null;
        Journal?.Dispose();
        Journal = null;
    }

    public override void _Process(double delta) => ProcessPending();

    // ── 生命周期 ───────────────────────────────────────────────

    /// <summary>懒启动监听。端口已被别的值占用时返回 false（沿用先到者的端口，日志说明）。</summary>
    public bool EnsureStarted(int port)
    {
        if (IsListening)
        {
            if (port != 0 && port != Port)
                GD.PrintErr($"[API] 端口已是 {Port}，忽略新请求的端口 {port}（同一进程只监听一个端口）。");
            return port == 0 || port == Port;
        }

        int actual = port <= 0 ? DefaultPort : port;
        try
        {
            server = new ApiHttpServer(Enqueue, actual);
            server.Start();
            Port = actual;
            GD.Print($"[API] 已启动：http://127.0.0.1:{Port}/api/game/（GET 读全局摘要，POST 提交指令，清单见 api.catalog）。");
            return true;
        }
        catch (Exception ex)
        {
            server?.Dispose();
            server = null;
            GD.PrintErr("[API] 启动失败：" + ex.Message);
            return false;
        }
    }

    // ── 域注册（按 owner 记账：换场景时旧场景延迟释放，不会误摘新场景刚登记的域） ──

    public void Register(IApiDomain domain, int port, object owner)
    {
        if (domain == null) return;
        if (!EnsureStarted(port)) return;
        domains.RemoveAll(x => string.Equals(x.Domain.DomainName, domain.DomainName, StringComparison.Ordinal)
            && (owner == null || x.Owner == null || ReferenceEquals(x.Owner, owner)));
        domains.Add(new Entry { Domain = domain, Owner = owner });
        string[] missing = domain.BoundTypes
            .Where(t => domain.Catalog.All(c => !string.Equals(c.Type, t, StringComparison.Ordinal))).ToArray();
        if (missing.Length > 0) GD.PrintErr($"[API] {domain.DomainName} 域处理器缺少元数据：{string.Join(", ", missing)}");
    }

    /// <summary>`owner` 为空 = 强制摘除该域名的全部登记；否则只摘自己那份。</summary>
    public void Unregister(string domainName, object owner = null)
    {
        if (string.IsNullOrEmpty(domainName)) return;
        domains.RemoveAll(x => string.Equals(x.Domain.DomainName, domainName, StringComparison.Ordinal)
            && (owner == null || x.Owner == null || ReferenceEquals(x.Owner, owner)));
    }

    /// <summary>登记战斗域（`HexBattleScene` 调用；端口取该场景的 `CommandApiPort`，owner = 场景实例）。</summary>
    public static void RegisterBattle(ApiBattleContext context, int port, object owner)
    {
        if (context?.Session == null || Instance == null) return;
        Instance.Register(new PlayerApiBattle(context), port, owner);
        Instance.Register(new DebugApiBattle(context), port, owner);
        Instance.Journal = new ApiEventJournal(context.Session);
        Instance.journalOwner = owner;
    }

    public static void UnregisterBattle(object owner)
    {
        if (Instance == null) return;
        Instance.Unregister(PlayerApiBattle.Domain, owner);
        Instance.Unregister(DebugApiBattle.Domain, owner);
        if (Instance.journalOwner == null || ReferenceEquals(Instance.journalOwner, owner))
        {
            Instance.Journal?.Dispose();
            Instance.Journal = null;
            Instance.journalOwner = null;
        }
    }

    /// <summary>登记运行局域（`RunFlowScene` 调用；端口取该场景的 `ApiPort`，owner = 场景实例）。</summary>
    public static void RegisterRun(ApiRunContext context, int port)
    {
        if (context?.Scene == null || Instance == null) return;
        Instance.Register(new PlayerApiRun(context), port, context.Scene);
        Instance.Register(new DebugApiRun(context), port, context.Scene);
    }

    public static void UnregisterRun(object owner)
    {
        if (Instance == null) return;
        Instance.Unregister(PlayerApiRun.Domain, owner);
        Instance.Unregister(DebugApiRun.Domain, owner);
    }

    // ── 派发（主线程） ─────────────────────────────────────────

    public ApiResult Execute(ApiRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Type))
            return ApiResult.Fail("", ApiLane.Player, "INVALID_REQUEST", "缺少 type（字段说明见 api.catalog）。");

        // 服务自带元指令：与场景无关，供「AI 发现有哪些指令、分别属于哪条通道」。
        if (request.Type.StartsWith(ApiLanes.MetaPrefix, StringComparison.Ordinal))
            return ExecuteMeta(request);

        // 从后往前找：同一域名可能有「新场景 + 未释放的旧场景」两条登记，新的优先。
        for (int i = domains.Count - 1; i >= 0; i--)
        {
            ApiResult result = domains[i].Domain.Execute(request);
            if (result == null) continue;
            stateVersion++;
            result.StateVersion = stateVersion;
            return result;
        }

        string registered = domains.Count == 0 ? "无（尚未进入战斗 / 运行局）" : string.Join("/", domains.Select(x => x.Domain.DomainName));
        return ApiResult.Fail(request.Type, ApiLanes.LaneOf(request.Type), "UNKNOWN_COMMAND",
            $"未知指令 {request.Type}（已注册域：{registered}；清单见 api.catalog）。");
    }

    private ApiResult ExecuteMeta(ApiRequest request)
    {
        switch (request.Type)
        {
            case "api.read":
                return ApiResult.Success("api.read", ApiLane.Player, "全局摘要读取成功。", GlobalRead());
            case "api.catalog":
            case "api.lanes":
                return ApiResult.Success(request.Type, ApiLane.Player, "指令清单读取成功。", Catalog());
            case "api.events":
                return ApiResult.Success("api.events", ApiLane.Player, "事件读取成功。",
                    Journal?.Read(request.AfterEventId, request.Limit) ?? Array.Empty<ApiBattleEvent>());
            default:
                return ApiResult.Fail(request.Type, ApiLane.Player, "UNKNOWN_META", $"未知元指令 {request.Type}。");
        }
    }

    private object GlobalRead() => new
    {
        port = Port,
        domains = domains.Select(x => x.Domain.DomainName),
        battle = Call("battle.state", new ApiRequest { Type = "battle.state" }),
        run = Call("run.state", new ApiRequest { Type = "run.state" }),
    };

    private object Call(string type, ApiRequest request)
    {
        for (int i = domains.Count - 1; i >= 0; i--)
        {
            ApiResult result = domains[i].Domain.Execute(request);
            if (result == null) continue;
            return result.Ok ? result.Data : new { error = result.Message };
        }
        return null;
    }

    private object Catalog() => new
    {
        laneGuide = new
        {
            player = "玩家通道：界面上真能做的操作（`battle.*` / `run.*` / `api.*`）。",
            debug = "调试通道：超出玩家范围（`debug.*`：选关 / 跳关 / 生成单位 / 改数值 / 改时间点 / 清档）。",
            rule = "类型名前缀 `debug.` = 调试通道，其余 = 玩家通道；响应里的 permission 字段自证。",
        },
        port = Port,
        commands = domains.SelectMany(entry => entry.Domain.Catalog.Select(c => new
        {
            c.Type,
            domain = entry.Domain.DomainName,
            permission = ApiLanes.Describe(c.Lane),
            lane = c.Lane.ToString(),
            readOnly = c.ReadOnly,
            c.Summary,
            c.Request,
        })),
        integrity = Integrity(),
    };

    /// <summary>自检：类型名重复 / 前缀与声明通道不符 / 处理器与元数据不齐（问题进 issues，不抛异常）。</summary>
    private object Integrity()
    {
        var issues = new List<string>();
        var all = domains.SelectMany(entry => entry.Domain.Catalog.Select(c => new { entry.Domain.DomainName, Command = c })).ToList();
        foreach (var group in all.GroupBy(x => x.Command.Type))
            if (group.Count() > 1) issues.Add($"类型名重复：{group.Key}");
        foreach (var item in all)
            if (ApiLanes.LaneOf(item.Command.Type) != item.Command.Lane)
                issues.Add($"通道与前缀不符：{item.Command.Type} 声明 {item.Command.Lane}");
        foreach (Entry entry in domains)
            foreach (string bound in entry.Domain.BoundTypes)
                if (entry.Domain.Catalog.All(c => !string.Equals(c.Type, bound, StringComparison.Ordinal)))
                    issues.Add($"处理器没有元数据：{bound}");
        return new { ok = issues.Count == 0, issues };
    }

    // ── 主线程泵 ───────────────────────────────────────────────

    private void ProcessPending()
    {
        while (pending.TryDequeue(out Pending item))
        {
            try { item.Completion.TrySetResult(Execute(item.Request)); }
            catch (Exception ex) { item.Completion.TrySetResult(ApiResult.Fail(item.Request?.Type, ApiLane.Player, "INTERNAL", ex.Message)); }
        }
    }

    private Task<ApiResult> Enqueue(ApiRequest request)
    {
        var completion = new TaskCompletionSource<ApiResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending.Enqueue(new Pending { Request = request, Completion = completion });
        return completion.Task;
    }
}
