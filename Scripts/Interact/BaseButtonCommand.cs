using Godot;

/// <summary>
/// 按钮命令基类 - 提供所有按钮脚本共用的错误检查和辅助方法
/// </summary>
public abstract partial class BaseButtonCommand : Button
{
	/// <summary>
	/// 获取该命令的参数格式说明（由子类提供）
	/// </summary>
	protected abstract string ParameterFormat { get; }

	/// <summary>
	/// 查找参数输入框
	/// </summary>
	protected LineEdit FindLineEdit()
	{
		Node scene = GetTree().CurrentScene;
		if (scene == null)
		{
			return null;
		}

		LineEdit lineEdit = scene.GetNodeOrNull<LineEdit>("操作面板/参数框/LineEdit");
		if (lineEdit != null)
		{
			return lineEdit;
		}

		return scene.GetNodeOrNull<LineEdit>("UI_Main/操作面板/参数框/LineEdit");
	}

	/// <summary>
	/// 查找战斗系统节点
	/// </summary>
	protected BattleSytem FindBattleSystem()
	{
		Node scene = GetTree().CurrentScene;
		if (scene == null)
		{
			return null;
		}

		BattleSytem direct = scene.GetNodeOrNull<BattleSytem>("BattleSytem");
		if (direct != null)
		{
			return direct;
		}

		return FindNodeRecursive<BattleSytem>(scene);
	}

	/// <summary>
	/// 递归查找指定类型的节点
	/// </summary>
	protected T FindNodeRecursive<T>(Node root) where T : Node
	{
		if (root is T found)
		{
			return found;
		}

		foreach (Node child in root.GetChildren())
		{
			T childFound = FindNodeRecursive<T>(child);
			if (childFound != null)
			{
				return childFound;
			}
		}

		return null;
	}

	/// <summary>
	/// 解析参数字符串为数组（支持多种分隔符）
	/// </summary>
	protected string[] ParseArguments(string raw)
	{
		return raw.Split(new char[] { ' ', '\t', ',', '，', ';', '；', '|' }, System.StringSplitOptions.RemoveEmptyEntries);
	}

	/// <summary>
	/// 在控制台输出错误信息
	/// </summary>
	protected void AppendConsoleError(string message)
	{
		AppendConsole("[错误] " + message);
		GD.PrintErr(message);
	}

	/// <summary>
	/// 在控制台输出信息
	/// </summary>
	protected void AppendConsoleInfo(string message)
	{
		AppendConsole("[信息] " + message);
	}

	/// <summary>
	/// 在控制台输出消息（不带前缀）
	/// </summary>
	protected void AppendConsole(string message)
	{
		Node scene = GetTree().CurrentScene;
		if (scene == null)
		{
			return;
		}

		RichTextLabel console = scene.GetNodeOrNull<RichTextLabel>("ConsoleContainer/Console");
		if (console == null)
		{
			console = scene.GetNodeOrNull<RichTextLabel>("UI_Main/ConsoleContainer/Console");
		}

		if (console == null)
		{
			return;
		}

		if (!string.IsNullOrEmpty(console.Text))
		{
			console.Text += "\n";
		}

		console.Text += message;
	}
}
