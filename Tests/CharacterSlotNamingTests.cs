// CharacterSlotNamingTests.cs
// 覆盖交互案 §3.1 的角色显示名规则：同名按「出现次序」编号，未解析出名字的角色不编号。
using System.Collections.Generic;
using Xunit;

public class CharacterSlotNamingTests
{
    private static readonly Dictionary<int, string> Names = new Dictionary<int, string>
    {
        [1002] = "重剑手",
        [1003] = "精灵",
        [1004] = "法师",
        [1005] = "伊瑟拉",
    };

    private static string Resolve(int characterId) => Names.TryGetValue(characterId, out string name) ? name : string.Empty;

    [Fact]
    public void BuildDisplayNames_NoDuplicate_UsesCharacterNamesVerbatim()
    {
        List<string> names = CharacterSlotNaming.BuildDisplayNames(new List<int> { 1002, 1003, 1004 }, Resolve);

        Assert.Equal(new List<string> { "重剑手", "精灵", "法师" }, names);
    }

    [Fact]
    public void BuildDisplayNames_SameCharacterTwice_NumbersByOccurrenceNotSlotIndex()
    {
        // 槽位 0 / 1 / 2 = 重剑手 / 伊瑟拉 / 重剑手 → 重剑手 / 伊瑟拉 / 重剑手2（伊瑟拉不带编号，编号不是槽位号 + 1）
        List<string> names = CharacterSlotNaming.BuildDisplayNames(new List<int> { 1002, 1005, 1002 }, Resolve);

        Assert.Equal(new List<string> { "重剑手", "伊瑟拉", "重剑手2" }, names);
    }

    [Fact]
    public void BuildDisplayNames_SameCharacterThreeTimes_NumbersFromTwo()
    {
        List<string> names = CharacterSlotNaming.BuildDisplayNames(new List<int> { 1002, 1002, 1002 }, Resolve);

        Assert.Equal(new List<string> { "重剑手", "重剑手2", "重剑手3" }, names);
    }

    [Fact]
    public void GetDisplayName_UnresolvedCharacterOrSlot_FallsBackWithoutNumbering()
    {
        Assert.Equal("角色 9999", CharacterSlotNaming.GetDisplayName(new List<int> { 9999, 9999 }, 1, Resolve));
        Assert.Equal("角色 ?", CharacterSlotNaming.GetDisplayName(new List<int> { 1002 }, 5, Resolve));
        Assert.Equal("重剑手", CharacterSlotNaming.GetDisplayName(new List<int> { 1002 }, 0, Resolve));
    }

    [Fact]
    public void BuildDisplayNames_NullInput_ReturnsEmptyList()
    {
        Assert.Empty(CharacterSlotNaming.BuildDisplayNames(null, Resolve));
    }
}
