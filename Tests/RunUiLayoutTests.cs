// RunUiLayoutTests.cs
// 常驻顶部按钮栏与战场 HUD 的共享几何（`Scripts/Run/RunUiLayout.cs`）纯逻辑单测：
// 锁定三条不变量 ——「为背包 / 装备预留的右组容量」「左右两组不重叠」「战场 HUD 让开顶栏两行」，
// 对应 2026-10-01 用户实测的两处压盖问题（时间点行压住战场「关卡目标」）。
using Xunit;

public class RunUiLayoutTests
{
    [Fact]
    public void ReservedCluster_FitsInBandAtReferenceResolution()
    {
        // 右组最终要容纳：现有 4 个（地图 / 定位当前角色 / 调试 / 暂停，其中「定位当前角色」是宽按钮）
        // + 预留 2 个（背包 / 装备）= 5 个标准 + 1 个宽按钮。
        Assert.Equal(600f, RunUiLayout.ReservedClusterWidth, 3);
        Assert.True(RunUiLayout.FitsReservedCluster(1600f), "参考分辨率 1600 宽必须容得下预留的两项。");
        Assert.True(RunUiLayout.FitsReservedCluster(1920f), "更宽的窗口必须容得下。");
        Assert.False(RunUiLayout.FitsReservedCluster(1280f),
            "1280 宽下右组不够（预留项只能靠缩窄按钮或减按钮），本口径在此分辨率下不成立。");
    }

    [Fact]
    public void ClusterWidth_CountsSeparationBetweenButtons()
    {
        Assert.Equal(88f, RunUiLayout.ClusterWidth(1, 0), 3);   // 单按钮无间隔
        Assert.Equal(280f, RunUiLayout.ClusterWidth(3, 0), 3);  // 3×88 + 2×8
        Assert.Equal(120f, RunUiLayout.ClusterWidth(0, 1), 3);
        Assert.Equal(408f, RunUiLayout.ClusterWidth(3, 1), 3);  // 现有 4 个按钮的实际占用：3×88 + 120 + 3×8
        Assert.Equal(600f, RunUiLayout.ClusterWidth(5, 1), 3);  // 再加「背包 / 装备」两个后的需求宽度
    }

    [Fact]
    public void TopRowBands_DoNotOverlap()
    {
        Assert.True(RunUiLayout.TopRowTop < RunUiLayout.TopRowBottom);
        Assert.True(RunUiLayout.TopRowBottom <= RunUiLayout.StoryRowTop); // 剧情行不压住通用行
        Assert.True(RunUiLayout.StoryRowTop < RunUiLayout.StoryRowBottom);
        Assert.True(RunUiLayout.TimeRowLeft < RunUiLayout.TimeRowRight);
        Assert.True(RunUiLayout.TimeRowRight <= RunUiLayout.GeneralRowLeft); // 左右两组不相交
        Assert.True(RunUiLayout.GeneralRowLeft < RunUiLayout.GeneralRowRight);
    }

    [Fact]
    public void TopBarBackdrop_CoversTheFirstRowFromTheScreenEdge()
    {
        // 底板带 = 第一行那一带：贴屏幕顶边、下沿与第一行一致，按钮纵向居中于它（中心 = 带高的一半）。
        Assert.Equal(0f, RunUiLayout.TopRowTop, 5);
        Assert.Equal(RunUiLayout.TopRowBottom, RunUiLayout.TopBarBackdropBottom, 5);
        Assert.True(RunUiLayout.TopBarBackdropBottom <= RunUiLayout.StoryRowTop);
        float centerRatio = (RunUiLayout.TopRowTop + RunUiLayout.TopBarBackdropBottom) / 2f;
        Assert.Equal(0.045f, centerRatio, 5);          // 900 高视口下按钮中心落在 40.5 px
        Assert.True(RunUiLayout.TopBarBackdropBottom > 0f);
    }

    [Fact]
    public void BattleHudTop_ClearsBothTopRows()
    {
        // 战场「关卡目标」等面板的最高可用比例必须低于**两行**常驻栏的下沿：
        // 剧情一开第二行就会占满 0.09–0.145，只让开第一行仍会压盖。
        Assert.True(RunUiLayout.BattleHudTop >= RunUiLayout.StoryRowBottom);
    }
}
