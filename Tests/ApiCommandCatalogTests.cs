// ApiCommandCatalogTests.cs
// 本机 AI 接口的**通道口径**单测（2026-10-02）：读四条指令表（纯静态元数据，不 new 任何 Godot 对象），
// 断言「玩家范围」与「超出玩家范围」两条通道在类型名前缀、元数据、跨表唯一性三方面都不混。
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

public class ApiCommandCatalogTests
{
	/// <summary>四条通道表：两个玩家类 + 两个调试类 + 服务级引导域。</summary>
	private static IEnumerable<(string Owner, ApiCommandInfo[] Table)> AllTables()
	{
		yield return ("PlayerApiBattle", PlayerApiBattle.Table);
		yield return ("PlayerApiRun", PlayerApiRun.Table);
		yield return ("DebugApiBattle", DebugApiBattle.Table);
		yield return ("DebugApiRun", DebugApiRun.Table);
		yield return ("DebugApiBootstrap", DebugApiBootstrap.Table);
	}

	[Fact]
	public void Lane_IsDerivedFromTypePrefix()
	{
		Assert.Equal(ApiLane.Debug, ApiLanes.LaneOf("debug.run.select_level"));
		Assert.Equal(ApiLane.Debug, ApiLanes.LaneOf("debug.battle.spawn_enemy"));
		Assert.Equal(ApiLane.Player, ApiLanes.LaneOf("run.bag.drag"));
		Assert.Equal(ApiLane.Player, ApiLanes.LaneOf("battle.state"));
		Assert.Equal("调试", ApiLanes.Describe(ApiLane.Debug));
		Assert.Equal("玩家", ApiLanes.Describe(ApiLane.Player));
	}

	[Fact]
	public void PlayerClasses_DeclareNoDebugCommand_AndDebugClasses_DeclareOnlyDebugCommands()
	{
		foreach ((string owner, ApiCommandInfo[] table) in AllTables())
		{
			foreach (ApiCommandInfo command in table)
			{
				ApiLane expected = owner.StartsWith("Debug", StringComparison.Ordinal) ? ApiLane.Debug : ApiLane.Player;
				Assert.True(expected == command.Lane,
					$"{owner} 的 {command.Type} 声明了 {command.Lane}，与类归属不符（玩家范围与越权操作必须分开）。");
				Assert.True(ApiLanes.LaneOf(command.Type) == command.Lane,
					$"{command.Type} 的前缀判出的通道与声明不符：前缀判据是唯一真相。");
			}
		}
	}

	[Fact]
	public void EveryCommand_HasUniqueType_AndNonEmptySummary()
	{
		var seen = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach ((string owner, ApiCommandInfo[] table) in AllTables())
		{
			foreach (ApiCommandInfo command in table)
			{
				Assert.False(seen.ContainsKey(command.Type), $"类型名重复：{command.Type}（{seen.GetValueOrDefault(command.Type)} 与 {owner}）。");
				seen[command.Type] = owner;
				Assert.False(string.IsNullOrWhiteSpace(command.Summary), $"{command.Type} 缺少用途说明。");
			}
		}
	}

	[Fact]
	public void EveryCommand_LivesInItsOwnDomainPrefix()
	{
		foreach ((string owner, ApiCommandInfo[] table) in AllTables())
		{
			foreach (ApiCommandInfo command in table)
			{
				bool ok = command.Type.StartsWith("battle.", StringComparison.Ordinal)
					|| command.Type.StartsWith("run.", StringComparison.Ordinal)
					|| command.Type.StartsWith("api.", StringComparison.Ordinal)
					|| command.Type.StartsWith("debug.game.", StringComparison.Ordinal)
					|| command.Type.StartsWith("debug.run.", StringComparison.Ordinal)
					|| command.Type.StartsWith("debug.battle.", StringComparison.Ordinal)
					|| command.Type == "debug.capture";
				Assert.True(ok, $"{owner} 的 {command.Type} 不在任何已知域前缀下。");
			}
		}
	}

	[Fact]
	public void RequestedFeatureCommands_ExistOnTheCorrectLane()
	{
		// 用户点名要的「新功能配 API」：时间点 / 夜间 UI / 背包（玩家范围）。
		AssertCommand(ApiLane.Player, "run.end_day");
		AssertCommand(ApiLane.Player, "run.camp.state");
		AssertCommand(ApiLane.Player, "run.camp.set_watch");
		AssertCommand(ApiLane.Player, "run.camp.rest");
		AssertCommand(ApiLane.Player, "run.bag.open");
		AssertCommand(ApiLane.Player, "run.bag.state");
		AssertCommand(ApiLane.Player, "run.bag.drag");
		// 用户点名要的越权能力：选关 / 一键跳关（调试通道）。
		AssertCommand(ApiLane.Debug, "debug.run.select_level");
		AssertCommand(ApiLane.Debug, "debug.run.next_combat");
		// 地点场景（村庄）与结算领取 / 关闭（2026-10-05，P3-18）：都是玩家在界面上真能点的那一下。
		AssertCommand(ApiLane.Player, "run.village.state");
		AssertCommand(ApiLane.Player, "run.village.move");
		AssertCommand(ApiLane.Player, "run.village.tips_accept");
		AssertCommand(ApiLane.Player, "run.village.smithy_craft");
		AssertCommand(ApiLane.Player, "run.village.restaurant_order");
		AssertCommand(ApiLane.Player, "run.settlement.claim");
		AssertCommand(ApiLane.Player, "run.settlement.claim_card");
		AssertCommand(ApiLane.Player, "run.settlement.close_panel");
		// 用户点名要的「完成关卡」（仅限战斗关卡；事件不能跳过）：调试通道。
		AssertCommand(ApiLane.Debug, "debug.run.complete_level");
		AssertCommand(ApiLane.Debug, "debug.battle.jump_level");
		AssertCommand(ApiLane.Debug, "debug.game.new_run");
	}

	[Fact]
	public void ReadOnlyFlags_MatchPlayerExpectations()
	{
		Assert.True(Find("battle.state").ReadOnly);
		Assert.True(Find("battle.legal_actions").ReadOnly);
		Assert.False(Find("battle.move").ReadOnly);
		Assert.False(Find("battle.end_turn").ReadOnly);
		Assert.True(Find("run.bag.state").ReadOnly);
		Assert.False(Find("run.bag.drag").ReadOnly);
		Assert.True(Find("run.settlement.state").ReadOnly);
		Assert.True(Find("run.village.state").ReadOnly);
		Assert.False(Find("run.village.move").ReadOnly);
		Assert.False(Find("run.settlement.claim").ReadOnly);
	}

	private static ApiCommandInfo Find(string type)
	{
		foreach ((string owner, ApiCommandInfo[] table) in AllTables())
		{
			ApiCommandInfo found = table.FirstOrDefault(x => string.Equals(x.Type, type, StringComparison.Ordinal));
			if (found != null)
			{
				return found;
			}
		}

		throw new InvalidOperationException($"指令表里没有 {type}。");
	}

	private static void AssertCommand(ApiLane lane, string type)
	{
		ApiCommandInfo command = Find(type);
		Assert.True(lane == command.Lane, $"{type} 应在 {lane} 通道，实际 {command.Lane}。");
	}
}
