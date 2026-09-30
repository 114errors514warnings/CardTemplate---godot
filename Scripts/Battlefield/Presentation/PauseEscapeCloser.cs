// PauseEscapeCloser.cs
// 暂停界面的 `Esc` 归口（交互案 §九：暂停 → 关闭暂停）。
// 暂停时整棵场景树停摆（ProcessMode 继承父级 = 可暂停），只有 `ProcessMode = Always` 的节点还能收到输入，
// 因此「Esc 关闭暂停」必须挂在这种节点上，而不是 HexBattleScene / RunFlowScene 自己。
using Godot;
using System;

public partial class PauseEscapeCloser : Node
{
	/// <summary>`Esc` 按下时的回调（关闭暂停）。</summary>
	public Action Escape { get; set; }

	public override void _UnhandledKeyInput(InputEvent inputEvent)
	{
		// 只在暂停中接管：非暂停时 Esc 由 HexBattleScene（取消施法 / 移动规划）与宿主（结算逐层关闭）按层级处理。
		if (!GetTree().Paused)
		{
			return;
		}

		if (inputEvent is not InputEventKey key || !key.Pressed || key.Echo || key.Keycode != Key.Escape)
		{
			return;
		}

		Escape?.Invoke();
		GetViewport()?.SetInputAsHandled();
	}
}
