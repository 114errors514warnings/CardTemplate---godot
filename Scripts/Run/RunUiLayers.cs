/// <summary>运行局 UI 的 CanvasLayer 层表。
/// 跨子树的层级只认这里的编号，禁止用 ZIndex 跨层抢占；
/// 暂停界面必须压过常驻按钮栏与所有内容 UI，因此单独占用最高层。</summary>
public static class RunUiLayers
{
    /// <summary>内容层：战斗、事件、剧情浮层与内容自持的结算界面。</summary>
    public const int Content = 10;
    /// <summary>世界地图覆盖层。</summary>
    public const int WorldMap = 30;
    /// <summary>常驻顶栏的**底板**（不透明横条，把战场 / 世界地图挡在它后面）。
    /// 压过内容层与世界地图、**低于**结算浮标与一切模态，所以打开结算 / 背包 / 装备 / 营地时不会被它压住；
    /// 顶栏的按钮与文案本体仍在 `GlobalButton` 层（在底板之上）。</summary>
    public const int TopBarBackdrop = 32;
    /// <summary>结算待领取浮窗：高于世界地图、低于结算模态 → 地图打开时可见可点。</summary>
    public const int SettlementBadge = 35;
    /// <summary>模态层：结算面板、调试面板等弹窗。</summary>
    public const int Modal = 40;
    /// <summary>营地（篝火休息界面）：全屏独占，压过结算模态、低于放弃确认与常驻按钮栏。</summary>
    public const int Camp = 42;
    /// <summary>放弃确认弹窗：压过结算模态与浮窗、低于常驻按钮栏。</summary>
    public const int AbandonConfirm = 45;
    /// <summary>运行局常驻顶部按钮栏。</summary>
    public const int GlobalButton = 50;
    /// <summary>暂停界面：全局最高层。</summary>
    public const int Pause = 60;
}
