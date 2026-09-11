using Godot;

namespace CardSimulator.Battlefield;

/// <summary>Single tuning point for battlefield presentation speeds, in cells per second.</summary>
public partial class BattlePresentationConfig : Resource
{
    [Export] public float PlayerMoveCellsPerSecond { get; set; } = 5f;
    [Export] public float MonsterMoveCellsPerSecond { get; set; } = 4f;
    [Export] public float TraceExtendCellsPerSecond { get; set; } = 10f;
    [Export] public float TraceChaseCellsPerSecond { get; set; } = 18f;
    [Export] public float ThrowArcCellsPerSecond { get; set; } = 8f;
}
