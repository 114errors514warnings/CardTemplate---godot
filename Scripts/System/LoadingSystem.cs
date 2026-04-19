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
    private const string DefaultCardCsvPath = "res://DataBase/Card/通用/通用.csv";

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

    public override void _Ready()
    {
        OnInit();
    }

    /// <summary>
    /// 初始化加载系统
    /// </summary>
    public void OnInit()
    {
        Dictionary<int, Card> cards = LoadCards(DefaultCardCsvPath, true);

        // 移除打印，由调用者处理
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
    /// 保存卡牌数据
    /// </summary>
    public static bool SaveCards(Dictionary<int, Card> cards, string filePath)
    {
        Card[] cardArray = new List<Card>(cards.Values).ToArray();
        bool success = LoadCardCsv.SaveCardsToCSV(cardArray, filePath);

        if (success)
        {
            cardCache = new Dictionary<int, Card>(cards);
        }

        return success;
    }

    /// <summary>
    /// 清空卡牌缓存
    /// </summary>
    public static void ClearCardCache(string filePath = null)
    {
        cardCache.Clear();
        GD.Print("Cleared card cache");
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
}