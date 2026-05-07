using CardSimulator;

public sealed class StateDefinition
{
	public StateType Type { get; }
	public string Name { get; }
	public bool IsStackable { get; }
	public bool IsPermanent { get; }
	public int TurnStartDecayAmount { get; }

	public StateDefinition(StateType type, string name, bool isStackable, bool isPermanent, int turnStartDecayAmount)
	{
		Type = type;
		Name = name ?? string.Empty;
		IsStackable = isStackable;
		IsPermanent = isPermanent;
		TurnStartDecayAmount = turnStartDecayAmount < 0 ? 0 : turnStartDecayAmount;
	}
}