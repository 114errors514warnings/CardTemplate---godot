// Abstractions/IBattleConsole.cs
// 控制台输出端口 —— 替代 BattleSytem.AppendPanelConsoleInfo/Error
// 战斗内任何"信息"和"错误"都通过这个口子出去，便于单测替换为内存收集器。

public interface IBattleConsole
{
    void Info(string message);
    void Error(string message);
}
