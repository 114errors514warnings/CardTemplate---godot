/// <summary>运行局 UI 的共享几何（跨场景只认这里的比例）。
///
/// 与 `RunUiLayers`（层级表）同口径：**常驻顶部按钮栏由 `RunFlowScene` 持有、内容场景各自另有顶部面板**，
/// 两处若各写一套数字，就会出现「战场面板压住常驻栏」这类跨场景压盖（2026-10-01 用户实测：
/// 战场「关卡目标」面板的顶边 0.02 与时间点行 0.025–0.08 重叠）。
/// 因此常驻栏两行的高度带、右侧按钮组容量与战场 HUD 的最高可用比例统一收在这里。
///
/// 只描述**几何**，不描述内容：谁放在哪个带里由各场景自己决定。</summary>
public static class RunUiLayout
{
    /// <summary>常驻栏第一行（左：时间点 + 结束当天；右：通用按钮）的上沿：**贴屏幕顶边**，
    /// 因为这一带同时是顶栏底板的铺底范围（底板把战斗 / 世界地图挡在身后）。</summary>
    public const float TopRowTop = 0f;
    /// <summary>常驻栏第一行（= 顶栏底板带）的下沿：按钮与文案**纵向居中**于该带；
    /// 战场 HUD 与结算浮窗都必须让开到这里。</summary>
    public const float TopRowBottom = 0.09f;

    /// <summary>顶栏底板带的下沿（= `TopRowBottom`）：独立名字用于「这一带是给不透明底板用的」语义。</summary>
    public const float TopBarBackdropBottom = TopRowBottom;

    /// <summary>常驻栏第二行（剧情专属：Log / 隐藏 / Auto / 跳过）的上沿。无剧情时该带为空。</summary>
    public const float StoryRowTop = TopRowBottom;
    /// <summary>常驻栏第二行的下沿。</summary>
    public const float StoryRowBottom = 0.145f;

    /// <summary>战场 HUD 的最高可用比例：让开常驻栏**两行**（第二行虽多为空，但剧情一开就会占满 0.09–0.145）。</summary>
    public const float BattleHudTop = 0.155f;

    /// <summary>常驻栏按钮的标准尺寸（`RunFlowScene.AddTopButton`）。</summary>
    public const float TopButtonWidth = 88f;
    public const float TopButtonHeight = 38f;
    /// <summary>「定位当前角色」这类长文案按钮的宽度。</summary>
    public const float WideTopButtonWidth = 120f;
    /// <summary>按钮间隔。</summary>
    public const float TopButtonSeparation = 8f;

    /// <summary>右侧通用按钮组的左沿：比「现有 4 个按钮」更宽，为 `装备` 等后续入口预留容量
    /// （`背包` 已于 2026-10-02 按用户口径落在**时间点行**，右组容量口径保持不变：容量断言是「装备 + 余量」）。</summary>
    public const float GeneralRowLeft = 0.54f;
    /// <summary>右侧按钮组的右沿（与剧情「跳过」行共用同一右缘）。</summary>
    public const float GeneralRowRight = 0.98f;
    /// <summary>左侧时间点行的左沿。</summary>
    public const float TimeRowLeft = 0.02f;
    /// <summary>左侧时间点行的右沿：在右侧按钮组左沿之前收住，两组永不重叠。</summary>
    public const float TimeRowRight = GeneralRowLeft - 0.02f;

    /// <summary>现有通用按钮：地图 / 定位当前角色 / 调试 / 暂停。</summary>
    public const int TopBarExistingButtons = 4;
    /// <summary>预留入口：`装备`（交互案已定：排在同一行的 `地图` 之前，见[装备系统交互案]）。
    /// `背包` 已于 2026-10-02 落在时间点行（用户口径），这里保留的容量不再要求容纳它。</summary>
    public const int TopBarReservedButtons = 2;
    /// <summary>现有按钮里只有「定位当前角色」是宽按钮。</summary>
    public const int TopBarExistingWideButtons = 1;

    /// <summary>右组容纳 `standard` 个标准按钮 + `wide` 个宽按钮所需的宽度（含间隔）。</summary>
    public static float ClusterWidth(int standard, int wide)
        => standard * TopButtonWidth + wide * WideTopButtonWidth + (standard + wide - 1) * TopButtonSeparation;

    /// <summary>右组必须容纳的宽度：现有 4 个 + 预留 2 个。</summary>
    public static float ReservedClusterWidth
        => ClusterWidth(TopBarExistingButtons + TopBarReservedButtons - TopBarExistingWideButtons, TopBarExistingWideButtons);

    /// <summary>右组实际可用的宽度（按视口宽度换算）。</summary>
    public static float ClusterBandWidth(float viewportWidth)
        => (GeneralRowRight - GeneralRowLeft) * viewportWidth;

    /// <summary>右组是否容得下「现有 + 预留」全部按钮。</summary>
    public static bool FitsReservedCluster(float viewportWidth)
        => ReservedClusterWidth <= ClusterBandWidth(viewportWidth) + 0.5f;
}
