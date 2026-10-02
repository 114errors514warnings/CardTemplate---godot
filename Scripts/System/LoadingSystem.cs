using Godot;
using CardSimulator;
using System;
using System.Collections.Generic;

/// <summary>
/// 游戏加载系统总接口
/// 汇聚所有数据加载功能，可根据需要扩展（角色、敌人、关卡等）
/// </summary>
[GlobalClass]
public partial class LoadingSystem : Node
{
	private const string DefaultFilePathRegistryCsvPath = "res://DataBase/FilePathRegistry.csv";

	public const string CardCsvPathKey = "Data.Card.Common";
	public const string StateCsvPathKey = "Data.State.Common";
	public const string CharacterCsvPathKey = "Data.Unit.Character";
	public const string MonsterCsvPathKey = "Data.Unit.Monster";
	public const string CharacterDefaultDeckCsvPathKey = "Data.Unit.CharacterDefaultDeck";
	/// <summary>掉落物表路径 key（FilePathRegistry）。</summary>
	public const string DropTableCsvPathKey = "Data.Map.DropTable";
	/// <summary>角色卡池来源表路径 key（FilePathRegistry）。</summary>
	public const string CharacterRewardPoolCsvPathKey = "Data.Card.CharacterRewardPool";
	/// <summary>怪物意图价值表路径 key（FilePathRegistry）；奖励折损口径见 单位数值平衡标准 §2.4。</summary>
	public const string MonsterValueCsvPathKey = "Data.Balance.MonsterValue";

	// ── 物品四表（2026-10-02，P1-4 / P1-19）：材料 / 道具 / 食物 / 配方 ──
	public const string MaterialCsvPathKey = "Data.Item.Material";
	public const string ItemCsvPathKey = "Data.Item.Item";
	public const string FoodCsvPathKey = "Data.Item.Food";
	public const string FoodRecipeCsvPathKey = "Data.Item.FoodRecipe";

	// ── 装备与背包配置（2026-10-02 批 E，P0-17 背包界面 / P0-18 装备链）──
	/// <summary>武器表路径 key（局外侧视图：名字 / 手数 / 单件负荷）。</summary>
	public const string WeaponCsvPathKey = "Data.Equipment.Weapon";
	/// <summary>背包配置表路径 key（队伍负荷上限，背包系统交互案 §四）。</summary>
	public const string InventoryConfigCsvPathKey = "Data.Inventory.Config";
	/// <summary>Stage 配置根目录：不逐文件注册，按 <层>/<节点类型>.csv 读取。</summary>
	public const string StageRootDir = "res://DataBase/Stage/";

	private static Dictionary<string, string> filePathRegistryCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// 缓存已加载的卡牌数据，Key 为 CardId
	/// </summary>
	private static Dictionary<int, Card> cardCache = new Dictionary<int, Card>();

	/// <summary>
	/// 缓存已加载的角色数据，Key 为 id
	/// </summary>
	private static Dictionary<int, Character> characterCache = new Dictionary<int, Character>();

	/// <summary>
	/// 缓存已加载的怪物数据，Key 为 id
	/// </summary>
	private static Dictionary<int, Monster> monsterCache = new Dictionary<int, Monster>();

	/// <summary>
	/// 缓存角色默认卡组，Key 为角色ID，Value 为卡牌ID→数量字典
	/// </summary>
	private static Dictionary<int, Dictionary<int, int>> characterDefaultDeckCache = new Dictionary<int, Dictionary<int, int>>();

	/// <summary>
	/// 缓存掉落物表条目
	/// </summary>
	private static List<DropTableEntry> dropTableCache = new List<DropTableEntry>();

	/// <summary>
	/// 缓存角色卡池来源行（CharacterRewardPool.csv）
	/// </summary>
	private static List<CharacterRewardSource> characterRewardPoolCache = new List<CharacterRewardSource>();

	/// <summary>
	/// 缓存怪物意图价值表条目（MonsterValue.csv），奖励折损口径用
	/// </summary>
	private static List<MonsterValueEntry> monsterValueCache = new List<MonsterValueEntry>();

	// ── 物品四表缓存（2026-10-02）──
	/// <summary>材料表缓存（`LoadMaterialsByKey` 后可用）。</summary>
	private static Dictionary<int, MaterialDefinition> materialCache = new Dictionary<int, MaterialDefinition>();

	/// <summary>道具表缓存。</summary>
	private static Dictionary<int, ItemDefinition> itemCache = new Dictionary<int, ItemDefinition>();

	/// <summary>食物表缓存（食物系统 §三）。</summary>
	private static Dictionary<int, FoodDefinition> foodCache = new Dictionary<int, FoodDefinition>();

	/// <summary>篝火合成配方缓存（食物系统 §四）。</summary>
	private static Dictionary<int, FoodRecipeDefinition> foodRecipeCache = new Dictionary<int, FoodRecipeDefinition>();

	/// <summary>武器表缓存（局外侧视图，`LoadWeaponsByKey` 后可用）。</summary>
	private static Dictionary<int, WeaponDefinition> weaponCache = new Dictionary<int, WeaponDefinition>();

	/// <summary>背包配置缓存（队伍负荷上限）；未配表时保持 null，读数走 <see cref="InventoryCapacity"/> 的兜底。</summary>
	private static InventoryConfigDefinition inventoryConfigCache;

	/// <summary>
	/// 缓存 Stage 遭遇配置：key = 层目录名（第一层…），value = 类型 → 行列表
	/// </summary>
	private static Dictionary<string, Dictionary<MapNodeType, List<StageEncounterRow>>> stageEncounterCache =
		new Dictionary<string, Dictionary<MapNodeType, List<StageEncounterRow>>>();

	/// <summary>
	/// 缓存状态配置，Key 为 StateType
	/// </summary>
	private static Dictionary<StateType, StateDefinition> stateCache = new Dictionary<StateType, StateDefinition>();

	public static Dictionary<string, string> FilePathRegistry
	{
		get { return filePathRegistryCache; }
	}

	/// <summary>
	/// 公开的卡牌字典访问器
	/// </summary>
	public static Dictionary<int, Card> CardDictionary
	{
		get { return cardCache; }
	}

	/// <summary>
	/// 公开的角色字典访问器
	/// </summary>
	public static Dictionary<int, Character> CharacterDictionary
	{
		get { return characterCache; }
	}

	/// <summary>
	/// 公开的怪物字典访问器
	/// </summary>
	public static Dictionary<int, Monster> MonsterDictionary
	{
		get { return monsterCache; }
	}

	/// <summary>
	/// 公开的角色默认卡组字典访问器，Key 为角色ID，Value 为卡牌ID→数量字典
	/// </summary>
	public static Dictionary<int, Dictionary<int, int>> CharacterDefaultDeckDictionary
	{
		get { return characterDefaultDeckCache; }
	}

	public static Dictionary<StateType, StateDefinition> StateDictionary
	{
		get { return stateCache; }
	}

	/// <summary>掉落物表条目（LoadDropTablesByKey 后可用）。</summary>
	public static List<DropTableEntry> DropTableEntries
	{
		get { return dropTableCache; }
	}

	/// <summary>角色卡池来源行（LoadCharacterRewardPoolByKey 后可用）。</summary>
	public static List<CharacterRewardSource> CharacterRewardSources
	{
		get { return characterRewardPoolCache; }
	}

	/// <summary>怪物意图价值行（LoadMonsterValueByKey 后可用）。</summary>
	public static List<MonsterValueEntry> MonsterValueEntries
	{
		get { return monsterValueCache; }
	}

	/// <summary>材料表（LoadMaterialsByKey 后可用）。</summary>
	public static Dictionary<int, MaterialDefinition> MaterialDictionary
	{
		get { return materialCache; }
	}

	/// <summary>道具表（LoadItemsByKey 后可用）。</summary>
	public static Dictionary<int, ItemDefinition> ItemDictionary
	{
		get { return itemCache; }
	}

	/// <summary>食物表（LoadFoodsByKey 后可用）。</summary>
	public static Dictionary<int, FoodDefinition> FoodDictionary
	{
		get { return foodCache; }
	}

	/// <summary>篝火合成配方表（LoadFoodRecipesByKey 后可用）。</summary>
	public static Dictionary<int, FoodRecipeDefinition> FoodRecipeDictionary
	{
		get { return foodRecipeCache; }
	}

	/// <summary>武器表（LoadWeaponsByKey 后可用）；ID = `Weapon.csv` 的 `WeaponId`。</summary>
	public static Dictionary<int, WeaponDefinition> WeaponDictionary
	{
		get { return weaponCache; }
	}

	/// <summary>背包配置（LoadInventoryConfigByKey 后可用）；未配表时为 null。</summary>
	public static InventoryConfigDefinition InventoryConfig
	{
		get { return inventoryConfigCache; }
	}

	/// <summary>队伍负荷上限（背包系统交互案 §四）：表已加载就用表里的 `Global` 行，否则用 `ItemNameResolver` 的兜底。</summary>
	public static float InventoryCapacity =>
		inventoryConfigCache?.GlobalCapacity ?? ItemNameResolver.DefaultInventoryCapacity;

	public override void _Ready()
	{
		OnInit();
	}

	/// <summary>
	/// 初始化加载系统
	/// </summary>
	public void OnInit()
	{
		EnsureFilePathRegistryLoaded();
		EnsureAllCardsLoaded();
		Dictionary<StateType, StateDefinition> states = LoadStatesByKey(StateCsvPathKey, true);

		// 移除打印，由调用者处理
	}

	private static void EnsureAllCardsLoaded()
	{
		if (cardCache.Count > 0)
		{
			return;
		}

		LoadAllCardsFromFolder("res://DataBase/Card/");

		// 注册"按本局失去生命次数降费"的死亡之舞 (CardId=11002010)：
		// 实际费用 = max(0, EnergyCost - loseHpTimes)
		Card.RegisterCostOverrideFactory(11002010, player =>
		{
			int loseHpTimes = BattleSytem.Current?.GetBattleHpLossEventCount(player) ?? 0;
			int baseCost = 3;
			return System.Math.Max(0, baseCost - loseHpTimes);
		});
	}

	private static void EnsureFilePathRegistryLoaded()
	{
		if (filePathRegistryCache.Count > 0)
		{
			return;
		}

		LoadFilePathRegistry(DefaultFilePathRegistryCsvPath, true);
	}

	public static Dictionary<string, string> LoadFilePathRegistry(string filePath, bool useCache = true)
	{
		if (useCache && filePathRegistryCache.Count > 0)
		{
			return filePathRegistryCache;
		}

		string[] dataLines = LoadCsv.LoadCSVDataLines(filePath);
		Dictionary<string, string> pathDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		foreach (string line in dataLines)
		{
			if (string.IsNullOrWhiteSpace(line))
			{
				continue;
			}

			string[] fields = LoadCsv.ParseCSVFields(line);
			if (fields.Length < 2)
			{
				GD.PrintErr($"文件路径注册表CSV格式错误，期望至少2列，实际 {fields.Length} 列：{line}");
				continue;
			}

			string key = fields[0]?.Trim() ?? string.Empty;
			string path = fields[1]?.Trim() ?? string.Empty;
			if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(path))
			{
				GD.PrintErr($"文件路径注册表CSV行缺少有效 key 或 path：{line}");
				continue;
			}

			pathDict[key] = path;
		}

		if (useCache)
		{
			filePathRegistryCache = pathDict;
		}

		return pathDict;
	}

	public static string GetFilePathByKey(string key)
	{
		EnsureFilePathRegistryLoaded();
		if (string.IsNullOrWhiteSpace(key))
		{
			GD.PrintErr("文件路径 key 为空。") ;
			return string.Empty;
		}

		if (!filePathRegistryCache.TryGetValue(key, out string filePath) || string.IsNullOrWhiteSpace(filePath))
		{
			GD.PrintErr($"未在文件路径注册表中找到 key={key} 对应的路径。") ;
			return string.Empty;
		}

		return filePath;
	}

	public static Dictionary<int, Card> LoadCardsByKey(string pathKey = CardCsvPathKey, bool useCache = true)
	{
		if (string.Equals(pathKey, CardCsvPathKey, StringComparison.OrdinalIgnoreCase))
		{
			EnsureAllCardsLoaded();
			return cardCache;
		}

		return LoadCards(GetFilePathByKey(pathKey), useCache);
	}

	public static Dictionary<StateType, StateDefinition> LoadStatesByKey(string pathKey = StateCsvPathKey, bool useCache = true)
	{
		return LoadStates(GetFilePathByKey(pathKey), useCache);
	}

	/// <summary>
	/// 强制重读状态 CSV 并覆盖缓存。CardBattleScene 启动时调用，避开静态缓存的"启动时一次性读"陷阱。
	/// </summary>
	public static Dictionary<StateType, StateDefinition> ReloadStates()
	{
		stateCache.Clear();
		return LoadStatesByKey(useCache: true);
	}

	public static Dictionary<int, Character> LoadCharactersByKey(string pathKey = CharacterCsvPathKey, bool useCache = true)
	{
		return LoadCharacters(GetFilePathByKey(pathKey), useCache);
	}

	public static Dictionary<int, Monster> LoadMonstersByKey(string pathKey = MonsterCsvPathKey, bool useCache = true)
	{
		return LoadMonsters(GetFilePathByKey(pathKey), useCache);
	}

	public static Dictionary<int, Dictionary<int, int>> LoadCharacterDefaultDecksByKey(string pathKey = CharacterDefaultDeckCsvPathKey, bool useCache = true)
	{
		return LoadCharacterDefaultDecks(GetFilePathByKey(pathKey), useCache);
	}

	public static List<int> GetCharacterDefaultCardIdListByKey(int characterId, string pathKey = CharacterDefaultDeckCsvPathKey, bool useCache = true)
	{
		return GetCharacterDefaultCardIdList(characterId, GetFilePathByKey(pathKey), useCache);
	}

	/// <summary>
	/// 扫描指定文件夹（含子文件夹）下的所有 CSV 文件，将卡牌数据合并加载到卡牌缓存中。
	/// </summary>
	public static void LoadAllCardsFromFolder(string folderPath)
	{
		if (string.IsNullOrWhiteSpace(folderPath))
		{
			GD.PrintErr("卡牌文件夹路径为空，无法加载卡牌。");
			return;
		}

		cardCache.Clear();
		List<string> csvPaths = new List<string>();
		CollectCsvFiles(folderPath, csvPaths);
		foreach (string path in csvPaths)
		{
			MergeCardsFromCsvIntoCache(path);
		}
	}

	private static void CollectCsvFiles(string folderPath, List<string> result)
	{
		DirAccess dir = DirAccess.Open(folderPath);
		if (dir == null)
		{
			GD.PrintErr($"无法打开卡牌文件夹：{folderPath}");
			return;
		}

		dir.ListDirBegin();
		string entry = dir.GetNext();
		while (!string.IsNullOrEmpty(entry))
		{
			string fullPath = folderPath.TrimEnd('/') + "/" + entry;
			if (dir.CurrentIsDir())
			{
				CollectCsvFiles(fullPath, result);
			}
			else if (entry.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
			{
				result.Add(fullPath);
			}
			entry = dir.GetNext();
		}
		dir.ListDirEnd();
	}

	private static void MergeCardsFromCsvIntoCache(string filePath)
	{
		using (FileAccess headerFile = FileAccess.Open(filePath, FileAccess.ModeFlags.Read))
		{
			string header = headerFile?.GetLine() ?? string.Empty;
			string[] columns = LoadCsv.ParseCSVFields(header);
			if (columns.Length == 0 || !string.Equals(columns[0], "CardId", StringComparison.OrdinalIgnoreCase))
			{
				return;
			}
		}

		Card[] cards = LoadCardCsv.LoadCardsFromCSV(filePath);
		if (cards == null)
		{
			return;
		}

		foreach (Card card in cards)
		{
			if (card == null)
			{
				continue;
			}

			if (!cardCache.ContainsKey(card.CardId))
			{
				cardCache[card.CardId] = card;
			}
			else
			{
				GD.PrintErr($"加载卡牌时发现重复 CardId（来源：{filePath}）：{card.CardId}，已跳过。");
			}
		}
	}

	/// <summary>
	/// 加载卡牌数据（支持缓存）
	/// </summary>
	/// <param name="filePath">CSV文件路径</param>
	/// <param name="useCache">是否使用缓存</param>
	/// <returns>卡牌字典，Key 为 CardId</returns>
	public static Dictionary<int, Card> LoadCards(string filePath, bool useCache = true)
	{
		if (useCache && cardCache.Count > 0)
		{
			return cardCache;
		}

		Card[] cards = LoadCardCsv.LoadCardsFromCSV(filePath);
		Dictionary<int, Card> cardDict = new Dictionary<int, Card>();

		foreach (Card card in cards)
		{
			if (card == null)
				continue;

			if (!cardDict.ContainsKey(card.CardId))
			{
				cardDict[card.CardId] = card;
			}
			else
			{
				GD.PrintErr($"Duplicate CardId detected while loading cards: {card.CardId}. Ignoring later entry.");
			}
		}

		if (useCache)
		{
			cardCache = cardDict;
		}

		return cardDict;
	}

	/// <summary>
	/// 清空卡牌缓存
	/// </summary>
	public static void ClearCardCache(string filePath = null)
	{
		cardCache.Clear();
		GD.Print("Cleared card cache");
	}

	public static Dictionary<StateType, StateDefinition> LoadStates(string filePath, bool useCache = true)
	{
		if (useCache && stateCache.Count > 0)
		{
			return stateCache;
		}

		StateDefinition[] states = LoadStateCsv.LoadStatesFromCSV(filePath);
		Dictionary<StateType, StateDefinition> stateDict = new Dictionary<StateType, StateDefinition>();
		foreach (StateDefinition state in states)
		{
			if (state == null)
			{
				continue;
			}

			if (!stateDict.ContainsKey(state.Type))
			{
				stateDict[state.Type] = state;
			}
			else
			{
				GD.PrintErr($"Duplicate StateType detected while loading states: {state.Type}. Ignoring later entry.");
			}
		}

		if (useCache)
		{
			stateCache = stateDict;
		}

		return stateDict;
	}

	/// <summary>
	/// 根据类别加载卡牌
	/// </summary>
	public static Dictionary<int, Card> LoadCardsByCategory(string filePath, CardCategory category, bool useCache = true)
	{
		Dictionary<int, Card> allCards = LoadCards(filePath, useCache);
		Dictionary<int, Card> filtered = new Dictionary<int, Card>();

		foreach (Card card in allCards.Values)
		{
			if (card.Category == category)
			{
				filtered[card.CardId] = card;
			}
		}

		return filtered;
	}

	/// <summary>
	/// 根据模板ID加载卡牌
	/// </summary>
	public static Card LoadCardByTemplateId(string filePath, int templateId, bool useCache = true)
	{
		Dictionary<int, Card> allCards = LoadCards(filePath, useCache);
		return allCards.TryGetValue(templateId, out Card card) ? card : null;
	}

	// ========== 后续可扩展的功能 ==========

	/// <summary>
	/// 加载角色数据（支持缓存）
	/// </summary>
	/// <param name="filePath">CSV文件路径</param>
	/// <param name="useCache">是否使用缓存</param>
	/// <returns>角色字典，Key 为 id</returns>
	public static Dictionary<int, Character> LoadCharacters(string filePath, bool useCache = true)
	{
		if (useCache && characterCache.Count > 0)
		{
			return characterCache;
		}

		Character[] characters = LoadCharacterCsv.LoadCharactersFromCSV(filePath);
		Dictionary<int, Character> characterDict = new Dictionary<int, Character>();

		foreach (Character character in characters)
		{
			if (character == null)
				continue;

			if (!characterDict.ContainsKey(character.id))
			{
				characterDict[character.id] = character;
			}
			else
			{
				GD.PrintErr($"Duplicate Character id detected while loading characters: {character.id}. Ignoring later entry.");
			}
		}

		if (useCache)
		{
			characterCache = characterDict;
		}

		return characterDict;
	}

	/// <summary>
	/// 加载怪物数据（支持缓存）
	/// </summary>
	/// <param name="filePath">CSV文件路径</param>
	/// <param name="useCache">是否使用缓存</param>
	/// <returns>怪物字典，Key 为 id</returns>
	public static Dictionary<int, Monster> LoadMonsters(string filePath, bool useCache = true)
	{
		if (useCache && monsterCache.Count > 0)
		{
			return monsterCache;
		}

		Monster[] monsters = LoadMonsterCsv.LoadMonstersFromCSV(filePath);
		Dictionary<int, Monster> monsterDict = new Dictionary<int, Monster>();

		foreach (Monster monster in monsters)
		{
			if (monster == null)
				continue;

			if (!monsterDict.ContainsKey(monster.id))
			{
				monsterDict[monster.id] = monster;
			}
			else
			{
				GD.PrintErr($"Duplicate Monster id detected while loading monsters: {monster.id}. Ignoring later entry.");
			}
		}

		if (useCache)
		{
			monsterCache = monsterDict;
		}

		return monsterDict;
	}

	/// <summary>
	/// 加载关卡数据（示例，待实现）
	/// </summary>
	public static void LoadLevels(string filePath)
	{
		GD.Print("Level loading feature coming soon");
		// 可在此处调用 LoadLevelCsv.LoadLevelsFromCSV(filePath)
	}

	/// <summary>
	/// 加载角色默认卡组配置（支持缓存）
	/// CSV 格式：CharacterId,CardId,Count
	/// </summary>
	public static Dictionary<int, Dictionary<int, int>> LoadCharacterDefaultDecks(string filePath, bool useCache = true)
	{
		if (useCache && characterDefaultDeckCache.Count > 0)
		{
			return characterDefaultDeckCache;
		}

		string[] dataLines = LoadCsv.LoadCSVDataLines(filePath);
		Dictionary<int, Dictionary<int, int>> result = new Dictionary<int, Dictionary<int, int>>();

		foreach (string line in dataLines)
		{
			if (string.IsNullOrWhiteSpace(line))
				continue;

			string[] fields = LoadCsv.ParseCSVFields(line);
			if (fields.Length < 3)
			{
				GD.PrintErr($"角色默认卡组CSV格式错误，期望3列，实际 {fields.Length} 列：{line}");
				continue;
			}

			if (!int.TryParse(fields[0], out int characterId) ||
				!int.TryParse(fields[1], out int cardId) ||
				!int.TryParse(fields[2], out int count) ||
				count <= 0)
			{
				GD.PrintErr($"角色默认卡组CSV行解析失败：{line}");
				continue;
			}

			if (!result.ContainsKey(characterId))
			{
				result[characterId] = new Dictionary<int, int>();
			}

			result[characterId][cardId] = result[characterId].TryGetValue(cardId, out int existing) ? existing + count : count;
		}

		if (useCache)
		{
			characterDefaultDeckCache = result;
		}

		return result;
	}

	/// <summary>
	/// 获取指定角色的默认卡牌ID列表（展开为重复项，直接用于实例化）
	/// </summary>
	public static List<int> GetCharacterDefaultCardIdList(int characterId, string filePath, bool useCache = true)
	{
		Dictionary<int, Dictionary<int, int>> allDecks = LoadCharacterDefaultDecks(filePath, useCache);
		List<int> cardIds = new List<int>();

		if (!allDecks.TryGetValue(characterId, out Dictionary<int, int> deck))
		{
			return cardIds;
		}

		List<int> sortedCardIds = new List<int>(deck.Keys);
		sortedCardIds.Sort();
		foreach (int cardId in sortedCardIds)
		{
			for (int i = 0; i < deck[cardId]; i++)
			{
				cardIds.Add(cardId);
			}
		}

		return cardIds;
	}

	// ─────────────────────────────────────────────────────────────
	// P0#9：Stage 遭遇 / 掉落表 / 角色卡池（方案见 2026.08/P0#9 施工文档）
	// ─────────────────────────────────────────────────────────────

	/// <summary>加载掉落物表（FilePathRegistry: Data.Map.DropTable）。</summary>
	public static List<DropTableEntry> LoadDropTablesByKey(string pathKey = DropTableCsvPathKey, bool useCache = true)
	{
		if (useCache && dropTableCache.Count > 0)
		{
			return dropTableCache;
		}

		string path = GetFilePathByKey(pathKey);
		dropTableCache = string.IsNullOrWhiteSpace(path) ? new List<DropTableEntry>() : LoadDropTableCsv.LoadEntriesFromCSV(path);
		return dropTableCache;
	}

	/// <summary>加载角色卡池来源表（FilePathRegistry: Data.Card.CharacterRewardPool）。</summary>
	public static List<CharacterRewardSource> LoadCharacterRewardPoolByKey(string pathKey = CharacterRewardPoolCsvPathKey, bool useCache = true)
	{
		if (useCache && characterRewardPoolCache.Count > 0)
		{
			return characterRewardPoolCache;
		}

		string path = GetFilePathByKey(pathKey);
		characterRewardPoolCache = string.IsNullOrWhiteSpace(path)
			? new List<CharacterRewardSource>()
			: LoadCharacterRewardPoolCsv.LoadSourcesFromCSV(path);
		return characterRewardPoolCache;
	}

	/// <summary>加载怪物意图价值表（FilePathRegistry: Data.Balance.MonsterValue）。</summary>
	public static List<MonsterValueEntry> LoadMonsterValueByKey(string pathKey = MonsterValueCsvPathKey, bool useCache = true)
	{
		if (useCache && monsterValueCache.Count > 0)
		{
			return monsterValueCache;
		}

		string path = GetFilePathByKey(pathKey);
		monsterValueCache = string.IsNullOrWhiteSpace(path)
			? new List<MonsterValueEntry>()
			: LoadMonsterValueCsv.LoadEntriesFromCSV(path);
		return monsterValueCache;
	}

	/// <summary>按怪物 Id 取意图价值行；没有该行返回 null（调用方按「只有面板 HP」兜底）。</summary>
	public static MonsterValueEntry FindMonsterValueEntry(int monsterId)
	{
		if (monsterValueCache.Count == 0)
		{
			LoadMonsterValueByKey();
		}

		foreach (MonsterValueEntry entry in monsterValueCache)
		{
			if (entry != null && entry.MonsterId == monsterId)
			{
				return entry;
			}
		}

		return null;
	}

	/// <summary>
	/// 加载 Stage 三层 × 节点类型遭遇配置。文件缺失/仅表头 = 空行表（视为无配置）。
	/// 旧体系目录已于 2026-09-30 归档到 `_archive/DataBase-Stage-2026-09-30/`（见 9 月施工文档 §46）：
	/// 目录不存在时直接返回空表，不再逐文件打印「CSV file not found」。
	/// </summary>
	public static Dictionary<string, Dictionary<MapNodeType, List<StageEncounterRow>>> LoadStageEncounters(bool useCache = true)
	{
		if (useCache && stageEncounterCache.Count > 0)
		{
			return stageEncounterCache;
		}
		if (!DirAccess.DirExistsAbsolute(StageRootDir))
		{
			// 旧体系目录已于 2026-09-30 归档（见 9 月施工文档 §46）：目录不存在 = 无配置，
			// 不再逐文件打印「CSV file not found」。
			stageEncounterCache.Clear();
			return stageEncounterCache;
		}

		Dictionary<string, Dictionary<MapNodeType, List<StageEncounterRow>>> fresh =
			new Dictionary<string, Dictionary<MapNodeType, List<StageEncounterRow>>>();
		foreach (string layer in MapNodeTypeUtil.LayerNames)
		{
			Dictionary<MapNodeType, List<StageEncounterRow>> byType =
				new Dictionary<MapNodeType, List<StageEncounterRow>>();
			foreach (MapNodeType type in MapNodeTypeUtil.StageConfigTypes)
			{
				string fileName = MapNodeTypeUtil.GetStageConfigFileName(type);
				if (string.IsNullOrWhiteSpace(fileName))
				{
					continue;
				}

				string path = $"{StageRootDir}{layer}/{fileName}.csv";
				byType[type] = LoadStageEncounterCsv.LoadRowsFromCSV(path, layer, type);
			}

			fresh[layer] = byType;
		}

		stageEncounterCache = fresh;
		return stageEncounterCache;
	}

	/// <summary>按层取某类型全部 Stage 行（未加载或不存在返回空表）。</summary>
	public static List<StageEncounterRow> GetStageEncounterRows(string layer, MapNodeType nodeType)
	{
		if (stageEncounterCache.Count == 0)
		{
			LoadStageEncounters();
		}

		if (!string.IsNullOrWhiteSpace(layer)
			&& stageEncounterCache.TryGetValue(layer, out Dictionary<MapNodeType, List<StageEncounterRow>> byType)
			&& byType != null
			&& byType.TryGetValue(nodeType, out List<StageEncounterRow> rows))
		{
			return rows;
		}

		return new List<StageEncounterRow>();
	}

	/// <summary>
	/// 按选取规则从某层某类型挑一行遭遇；返回 null = 无配置（点击不触发）。
	/// 普通敌袭应传 ResolveNormalCombatDifficultyByEncounterCount 的结果。
	/// </summary>
	public static StageEncounterRow TryPickStageEncounter(string layer, MapNodeType nodeType, StageDifficulty? ruleDifficulty, System.Random rng)
	{
		List<StageEncounterRow> rows = GetStageEncounterRows(layer, nodeType);
		bool requireUsable = StageEncounterPicker.IsCombatLikeType(nodeType);
		return StageEncounterPicker.Pick(rows, ruleDifficulty, rng, requireUsable);
	}

	/// <summary>
	/// `Character.csv` 的角色显示名（交互案 §3.1 的统一取名口原料）：找不到或名字为空时返回空串，
	/// 由 `CharacterSlotNaming` 兜底成「角色 {id}」。
	/// </summary>
	public static string GetCharacterName(int characterId)
	{
		if (CharacterDictionary.TryGetValue(characterId, out Character character)
			&& character != null
			&& !string.IsNullOrWhiteSpace(character.Name))
		{
			return character.Name;
		}

		return string.Empty;
	}

	/// <summary>获取某角色可获得的卡牌模板 id 集合（通用 + 角色专属，来源 CharacterRewardPool.csv）。</summary>
	public static List<int> GetCharacterRewardCardIds(int characterId)
	{
		if (characterRewardPoolCache.Count == 0)
		{
			LoadCharacterRewardPoolByKey();
		}

		List<int> ids = new List<int>();
		foreach (CharacterRewardSource source in characterRewardPoolCache)
		{
			if (source == null || source.CharacterId != characterId || string.IsNullOrWhiteSpace(source.CardSource))
			{
				continue;
			}

			string path = "res://DataBase/Card/" + source.CardSource.TrimStart('/');
			Card[] cards = LoadCardCsv.LoadCardsFromCSV(path);
			foreach (Card card in cards)
			{
				if (card != null && !ids.Contains(card.CardId))
				{
					ids.Add(card.CardId);
				}
			}
		}

		return ids;
	}

	// ─────────────────────────────────────────────────────────────
	// 物品四表（2026-10-02，P1-4 / P1-19）：材料 / 道具 / 食物 / 配方
	// ─────────────────────────────────────────────────────────────

	/// <summary>
	/// 一次性加载物品四表并做跨表校验（配方结果食物、输入食物 / 材料必须已定义）。
	/// 表头 / 枚举 / ID 有误时抛 <see cref="FormatException"/>（P1-4 验收：不静默降级）。
	/// </summary>
	public static void LoadItemTablesByKey(bool useCache = true)
	{
		LoadMaterialsByKey(MaterialCsvPathKey, useCache);
		LoadItemsByKey(ItemCsvPathKey, useCache);
		LoadFoodsByKey(FoodCsvPathKey, useCache);
		LoadFoodRecipesByKey(FoodRecipeCsvPathKey, useCache);
		LoadFoodCsv.ValidateReferences(foodRecipeCache, foodCache, materialCache);
		ItemNameResolver.RegisterMaterials(materialCache);
		ItemNameResolver.RegisterItems(itemCache);
		ItemNameResolver.RegisterFoods(foodCache);
		ValidateDropTableItemReferences();
	}

	/// <summary>
	/// 掉落表 → 物品表的引用校验（P1-4 验收）：`Material / Item / Food` 行的 `RewardParam` 必须在对应表里存在，
	/// 否则抛 <see cref="FormatException"/> 并一次列出**全部**悬空引用（不静默显示裸编号）。
	/// `Equipment` 行只在校验器里做了"有注册名才校验"的弱校验 —— 装备表（P0-18）未落地前它没有名字来源。
	/// </summary>
	public static void ValidateDropTableItemReferences()
	{
		List<DropTableEntry> entries = LoadDropTablesByKey();
		List<string> missing = new List<string>();
		foreach (DropTableEntry entry in entries)
		{
			if (entry == null)
			{
				continue;
			}

			switch (entry.Category)
			{
				case DropCategory.Material:
					if (!materialCache.ContainsKey(entry.RewardParam))
					{
						missing.Add($"Material {entry.RewardParam}（掉落表 {entry.DropTableId}）");
					}
					break;
				case DropCategory.Item:
					if (!itemCache.ContainsKey(entry.RewardParam))
					{
						missing.Add($"Item {entry.RewardParam}（掉落表 {entry.DropTableId}）");
					}
					break;
				case DropCategory.Food:
					if (!foodCache.ContainsKey(entry.RewardParam))
					{
						missing.Add($"Food {entry.RewardParam}（掉落表 {entry.DropTableId}）");
					}
					break;
			}
		}

		if (missing.Count > 0)
		{
			throw new FormatException("[DropTable] 引用了未定义的物品（请在 DataBase/Item 下补定义）：" + string.Join("；", missing));
		}
	}

	/// <summary>加载材料表（FilePathRegistry: Data.Item.Material）。</summary>
	public static Dictionary<int, MaterialDefinition> LoadMaterialsByKey(string pathKey = MaterialCsvPathKey, bool useCache = true)
	{
		if (useCache && materialCache.Count > 0)
		{
			return materialCache;
		}

		string path = GetFilePathByKey(pathKey);
		materialCache = string.IsNullOrWhiteSpace(path)
			? new Dictionary<int, MaterialDefinition>()
			: LoadMaterialCsv.LoadFromCSV(path);
		return materialCache;
	}

	/// <summary>加载道具表（FilePathRegistry: Data.Item.Item）。</summary>
	public static Dictionary<int, ItemDefinition> LoadItemsByKey(string pathKey = ItemCsvPathKey, bool useCache = true)
	{
		if (useCache && itemCache.Count > 0)
		{
			return itemCache;
		}

		string path = GetFilePathByKey(pathKey);
		itemCache = string.IsNullOrWhiteSpace(path)
			? new Dictionary<int, ItemDefinition>()
			: LoadItemCsv.LoadFromCSV(path);
		return itemCache;
	}

	/// <summary>加载食物表（FilePathRegistry: Data.Item.Food）。</summary>
	public static Dictionary<int, FoodDefinition> LoadFoodsByKey(string pathKey = FoodCsvPathKey, bool useCache = true)
	{
		if (useCache && foodCache.Count > 0)
		{
			return foodCache;
		}

		string path = GetFilePathByKey(pathKey);
		foodCache = string.IsNullOrWhiteSpace(path)
			? new Dictionary<int, FoodDefinition>()
			: LoadFoodCsv.LoadFoodsFromCSV(path);
		return foodCache;
	}

	/// <summary>加载篝火合成配方表（FilePathRegistry: Data.Item.FoodRecipe）。</summary>
	public static Dictionary<int, FoodRecipeDefinition> LoadFoodRecipesByKey(string pathKey = FoodRecipeCsvPathKey, bool useCache = true)
	{
		if (useCache && foodRecipeCache.Count > 0)
		{
			return foodRecipeCache;
		}

		string path = GetFilePathByKey(pathKey);
		foodRecipeCache = string.IsNullOrWhiteSpace(path)
			? new Dictionary<int, FoodRecipeDefinition>()
			: LoadFoodCsv.LoadRecipesFromCSV(path);
		return foodRecipeCache;
	}

	/// <summary>
	/// 装备表 + 背包配置表（2026-10-02 批 E）：载入 `Weapon.csv` 的局外侧视图与 `InventoryConfig.csv`，
	/// 并把「装备名 / 名字反查 / 占用手数 / 单件负荷」与「队伍负荷上限」注册进 <see cref="ItemNameResolver"/> ——
	/// 背包 / 装备界面与纯逻辑模块（`RunBagSystem` / `RunEquipmentSystem`）只读那份注册表，不各自读配表。
	/// </summary>
	public static void LoadEquipmentTablesByKey(bool useCache = true)
	{
		LoadWeaponsByKey(WeaponCsvPathKey, useCache);
		LoadInventoryConfigByKey(InventoryConfigCsvPathKey, useCache);
		ItemNameResolver.RegisterWeapons(weaponCache);
		ItemNameResolver.SetInventoryCapacity(InventoryCapacity);
	}

	/// <summary>加载武器表（FilePathRegistry: Data.Equipment.Weapon）。</summary>
	public static Dictionary<int, WeaponDefinition> LoadWeaponsByKey(string pathKey = WeaponCsvPathKey, bool useCache = true)
	{
		if (useCache && weaponCache.Count > 0)
		{
			return weaponCache;
		}

		string path = GetFilePathByKey(pathKey);
		weaponCache = string.IsNullOrWhiteSpace(path)
			? new Dictionary<int, WeaponDefinition>()
			: LoadWeaponCsv.LoadFromCSV(path);
		return weaponCache;
	}

	/// <summary>加载背包配置表（FilePathRegistry: Data.Inventory.Config）。</summary>
	public static InventoryConfigDefinition LoadInventoryConfigByKey(string pathKey = InventoryConfigCsvPathKey, bool useCache = true)
	{
		if (useCache && inventoryConfigCache != null)
		{
			return inventoryConfigCache;
		}

		string path = GetFilePathByKey(pathKey);
		inventoryConfigCache = string.IsNullOrWhiteSpace(path)
			? null
			: LoadInventoryCsv.LoadFromCSV(path);
		return inventoryConfigCache;
	}

	/// <summary>武器定义（局外侧视图）；未定义返回 null。</summary>
	public static WeaponDefinition GetWeapon(int weaponId) =>
		LoadWeaponsByKey().TryGetValue(weaponId, out WeaponDefinition definition) ? definition : null;

	/// <summary>材料定义；未定义返回 null（调用方按「未定义材料(ID)」显示并报错）。</summary>
	public static MaterialDefinition GetMaterial(int materialId) =>
		LoadMaterialsByKey().TryGetValue(materialId, out MaterialDefinition definition) ? definition : null;

	/// <summary>道具定义；未定义返回 null。</summary>
	public static ItemDefinition GetItem(int itemId) =>
		LoadItemsByKey().TryGetValue(itemId, out ItemDefinition definition) ? definition : null;

	/// <summary>食物定义；未定义返回 null。</summary>
	public static FoodDefinition GetFood(int foodId) =>
		LoadFoodsByKey().TryGetValue(foodId, out FoodDefinition definition) ? definition : null;

	/// <summary>材料显示名；未定义返回空串。</summary>
	public static string GetMaterialName(int materialId) => GetMaterial(materialId)?.DefinitionId ?? string.Empty;

	/// <summary>道具显示名；未定义返回空串。</summary>
	public static string GetItemName(int itemId) => GetItem(itemId)?.DefinitionId ?? string.Empty;

	/// <summary>食物显示名；未定义返回空串。</summary>
	public static string GetFoodName(int foodId) => GetFood(foodId)?.DefinitionId ?? string.Empty;

	/// <summary>主流程入口统一预热（主界面 _Ready 调用，避免首次访问缓存为空）。</summary>
	public static void EnsureAllDataLoaded()
	{
		EnsureFilePathRegistryLoaded();
		EnsureAllCardsLoaded();
		LoadCharactersByKey();
		LoadMonstersByKey();
		LoadCharacterDefaultDecksByKey();
		LoadStatesByKey();
		LoadDropTablesByKey();
		LoadCharacterRewardPoolByKey();
		LoadMonsterValueByKey();
		LoadStageEncounters();
		LoadItemTablesByKey();
		LoadEquipmentTablesByKey();
	}
}
