using Xunit;

public class BattleRewardApplyTests
{
    [Fact]
    public void ApplyRewardEntryToRun_StoresEveryNonCardCategory()
    {
        RunSaveData run = new RunSaveData();
        BattleRewardPresenter.ApplyRewardEntryToRun(new DropTableEntry { Category = DropCategory.Gold, Amount = 50 }, run);
        BattleRewardPresenter.ApplyRewardEntryToRun(new DropTableEntry { Category = DropCategory.Key, Amount = 2 }, run);
        BattleRewardPresenter.ApplyRewardEntryToRun(new DropTableEntry { Category = DropCategory.Material, RewardParam = 101, Amount = 3 }, run);
        BattleRewardPresenter.ApplyRewardEntryToRun(new DropTableEntry { Category = DropCategory.Item, RewardParam = 201, Amount = 4 }, run);
        BattleRewardPresenter.ApplyRewardEntryToRun(new DropTableEntry { Category = DropCategory.Equipment, RewardParam = 301, Amount = 1 }, run);

        Assert.Equal(50, run.Gold);
        Assert.Equal(2, run.Keys);
        Assert.Equal(3, run.Materials[101]);
        Assert.Equal(4, run.Items[201]);
        Assert.Equal(1, run.Equipment[301]);
    }
}
