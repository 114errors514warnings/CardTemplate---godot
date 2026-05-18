using CardSimulator;

public sealed class StateDefinition
{
	public StateType Type { get; }
	public string Name { get; }
	public bool IsStackable { get; }
	public bool IsPermanent { get; }
	public bool IsDebuff { get; }
	public bool IsElite { get; }
	public int TurnStartDecayAmount { get; }

	public StateDefinition(StateType type, string name, bool isStackable, bool isPermanent, int turnStartDecayAmount, bool isDebuff = false, bool isElite = false)
	{
		Type = type;
		Name = name ?? string.Empty;
		IsStackable = isStackable;
		IsPermanent = isPermanent;
		IsDebuff = isDebuff;
		IsElite = isElite;
		TurnStartDecayAmount = turnStartDecayAmount < 0 ? 0 : turnStartDecayAmount;
	}
}