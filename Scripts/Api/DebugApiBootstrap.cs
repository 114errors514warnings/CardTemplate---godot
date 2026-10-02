// Scripts/Api/DebugApiBootstrap.cs
// **调试 API** · 引导域（通道 `Debug`，服务自带）：让自动化在**任何场景**（含主菜单）都能开局与收尾，
// 不必先手点 UI。全部超出玩家范围：新建本局、切场景、任意场景截图、退出进程。

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>服务级调试路由（`debug.game.*`）：不依赖任何游戏场景，只要有进程就能用。</summary>
public sealed class DebugApiBootstrap : IApiDomain
{
    public const string Domain = "debug.game";

    private const string RunFlowScenePath = "res://Scenes/Run/RunFlowScene.tscn";
    private const string MainMenuScenePath = "res://Scenes/MainMenu/MainMenuScene.tscn";

    /// <summary>`debug.game.new_run` 不指定角色时的默认阵容（与 UI 烟测一致）。</summary>
    private static readonly List<int> DefaultCharacters = new() { 1002, 1003, 1004 };

    public static readonly ApiCommandInfo[] Table =
    {
        new("debug.game.read", ApiLane.Debug, "进程级只读：当前场景 / 是否在本局 / 存档是否存在 / 端口。", true),
        new("debug.game.new_run", ApiLane.Debug, "新建本局并切到运行局场景（**覆盖当前存档**）。", false, "characterIds,seed"),
        new("debug.game.enter_run", ApiLane.Debug, "切到运行局场景（沿用现有存档；没有存档时拒绝）。"),
        new("debug.game.enter_menu", ApiLane.Debug, "切回主菜单场景。"),
        new("debug.game.capture", ApiLane.Debug, "任意场景截图到 Tests/ApiCaptures/（captureMode=compressed 出 1280×720）。", true, "name,captureMode"),
        new("debug.game.quit", ApiLane.Debug, "退出进程（自动化收尾用；比外部杀进程干净，默认退出码 0）。", false, "amount"),
    };

    private readonly Node host;
    private readonly Dictionary<string, Func<ApiRequest, ApiResult>> bindings;

    public DebugApiBootstrap(Node host)
    {
        this.host = host;
        bindings = new Dictionary<string, Func<ApiRequest, ApiResult>>(StringComparer.Ordinal)
        {
            ["debug.game.read"] = Read,
            ["debug.game.new_run"] = NewRun,
            ["debug.game.enter_run"] = EnterRun,
            ["debug.game.enter_menu"] = EnterMenu,
            ["debug.game.capture"] = Capture,
            ["debug.game.quit"] = Quit,
        };
    }

    public string DomainName => Domain;
    public IReadOnlyList<ApiCommandInfo> Catalog => Table;
    public IReadOnlyList<string> BoundTypes => bindings.Keys.ToArray();

    public ApiResult Execute(ApiRequest request)
    {
        if (request?.Type == null || !bindings.TryGetValue(request.Type, out var handler)) return null;
        return handler(request);
    }

    private Node CurrentScene => host?.GetTree()?.CurrentScene;

    private ApiResult Read(ApiRequest request) => ApiResult.Success(request.Type, ApiLane.Debug, "进程状态读取成功。", new
    {
        scene = CurrentScene?.SceneFilePath ?? CurrentScene?.Name.ToString(),
        hasRun = RunSession.Instance?.HasActiveRun ?? false,
        hasSave = RunSession.HasSave(),
        gameMode = RunSession.Instance?.Current?.GameMode,
        port = ApiService.Instance?.Port ?? 0,
    });

    private ApiResult NewRun(ApiRequest request)
    {
        List<int> ids = request.CharacterIds != null && request.CharacterIds.Count > 0 ? request.CharacterIds : DefaultCharacters;
        LoadingSystem.EnsureAllDataLoaded();
        RunSession.Instance.StartNewRun(ids, request.Seed > 0 ? request.Seed : (int?)null);
        host.GetTree().ChangeSceneToFile(RunFlowScenePath);
        return ApiResult.Success(request.Type, ApiLane.Debug, "已新建本局并切到运行局场景。",
            new { characters = ids, seed = request.Seed, scene = RunFlowScenePath });
    }

    private ApiResult EnterRun(ApiRequest request)
    {
        if (!RunSession.HasSave() && RunSession.Instance?.Current == null)
            return ApiResult.Fail(request.Type, ApiLane.Debug, "NO_SAVE", "没有可续玩的存档（先 debug.game.new_run）。");
        host.GetTree().ChangeSceneToFile(RunFlowScenePath);
        return ApiResult.Success(request.Type, ApiLane.Debug, "已切到运行局场景。", new { scene = RunFlowScenePath });
    }

    private ApiResult EnterMenu(ApiRequest request)
    {
        host.GetTree().ChangeSceneToFile(MainMenuScenePath);
        return ApiResult.Success(request.Type, ApiLane.Debug, "已切回主菜单。", new { scene = MainMenuScenePath });
    }

    /// <summary>任意场景截图（与战斗 / 运行局两个域里的截图同一命名口径）。</summary>
    private ApiResult Capture(ApiRequest request)
    {
        string safeName = string.IsNullOrWhiteSpace(request.Name) ? "game" : string.Concat(request.Name.Where(char.IsLetterOrDigit));
        if (safeName.Length == 0) safeName = "game";
        string relative = $"res://Tests/ApiCaptures/{safeName}-{DateTime.Now:yyyyMMdd-HHmmss}.png";
        string absolute = ProjectSettings.GlobalizePath(relative);
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(absolute));
        Image image = host.GetViewport()?.GetTexture()?.GetImage();
        if (image == null) return ApiResult.Fail(request.Type, ApiLane.Debug, "CAPTURE_FAILED", "无窗口 / headless：视口不可读。");
        if (string.Equals(request.CaptureMode, "compressed", StringComparison.OrdinalIgnoreCase))
            image.Resize(1280, 720, Image.Interpolation.Lanczos);
        return image.SavePng(absolute) == Error.Ok
            ? ApiResult.Success(request.Type, ApiLane.Debug, "截图已保存。", new { path = relative })
            : ApiResult.Fail(request.Type, ApiLane.Debug, "CAPTURE_FAILED", "保存 PNG 失败：" + relative);
    }

    private ApiResult Quit(ApiRequest request)
    {
        int code = request.Amount;
        GD.Print($"[API] 收到 debug.game.quit，退出码 {code}。");
        host.GetTree().Quit(code);
        return ApiResult.Success(request.Type, ApiLane.Debug, $"已请求退出（{code}）。");
    }
}
