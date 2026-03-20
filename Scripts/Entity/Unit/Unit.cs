using Godot;
using System;

// 角色/怪物通用基类（作为数据模板，继承RefCounted更轻量）
public partial class Unit : RefCounted
{
	// 私有字段存储核心属性值
	private int _iniMaxHp = 100;
	private int _iniAttack = 10;
	private int _iniDefense = 5;

	// 公共属性（带封装，确保数值非负）
	/// <summary>
	/// 单位最大生命值
	/// </summary>
	[Export] // 标记为可导出，支持在编辑器中修改
	public int MaxHp
	{
		get => _iniMaxHp;
		set => _iniMaxHp = Math.Max(value, 0); // 确保HP不小于0
	}

	/// <summary>
	/// 单位默认攻击力
	/// </summary>
	[Export]
	public int DefaultAttack
	{
		get => _iniAttack;
		set => _iniAttack = Math.Max(value, 0); // 确保攻击力不小于0
	}

	/// <summary>
	/// 单位默认防御力
	/// </summary>
	[Export]
	public int DefaultDefense
	{
		get => _iniDefense;
		set => _iniDefense = Math.Max(value, 0); // 确保防御力不小于0
	}

	// 无参构造函数（必须保留，兼容Godot编辑器实例化）
	public Unit()
	{
		// 使用默认值初始化
	}

	// 带参数的构造函数：传入三项核心属性的初始值
	/// <summary>
	/// 初始化单位核心属性
	/// </summary>
	/// <param name="maxHp">最大生命值</param>
	/// <param name="defaultAttack">默认攻击力</param>
	/// <param name="defaultDefense">默认防御力</param>
	public Unit(int maxHp, int defaultAttack, int defaultDefense)
	{
		// 直接赋值给属性（利用属性的setter确保数值非负）
		MaxHp = maxHp;
		DefaultAttack = defaultAttack;
		DefaultDefense = defaultDefense;
	}

	/// <summary>
	/// 获取单位基础属性字典（便于调试/UI展示/战斗计算）
	/// </summary>
	/// <returns>包含核心属性的Dictionary</returns>
	public Godot.Collections.Dictionary<string, int> GetBaseStats()
	{
		return new Godot.Collections.Dictionary<string, int>()
		{
			{ "max_hp", MaxHp },
			{ "default_attack", DefaultAttack },
			{ "default_defense", DefaultDefense }
		};
	}

	// 预留生命周期方法（为后续继承UnitInstance接口做准备）
	/// <summary>
	/// 初始化方法（对应Godot的_ready）
	/// </summary>
	public virtual void Initialize()
	{
		// 子类可重写此方法实现初始化逻辑
	}
}
