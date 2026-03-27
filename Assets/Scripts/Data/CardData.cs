using System.Collections.Generic;
using UnityEngine;

namespace FWTCG.Data
{
    public enum CardType { Follower, Spell, Equipment, Champion, Battlefield }
    public enum CardRegion { Void, Noxus, Ionia, Bandle, Shadow, Order, Neutral }
    public enum RuneType { Blazing, Radiant, Verdant, Crushing, Chaos, Order }

    [CreateAssetMenu(fileName = "CardData", menuName = "FWTCG/CardData")]
    public class CardData : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] public string id;
        [SerializeField] public string cardName;
        [SerializeField] public CardRegion region;
        [SerializeField] public CardType type;

        [Header("Stats")]
        [SerializeField] public int cost;
        [SerializeField] public int atk;
        [SerializeField] public int hp;          // champion 专用；普通随从无意义，不用于战斗计算

        [Header("Keywords")]
        [SerializeField] public List<string> keywords = new();

        [Header("Text")]
        [SerializeField] [TextArea] public string text;
        [SerializeField] public string emoji;
        [SerializeField] [TextArea] public string lore;

        [Header("Effect")]
        [SerializeField] public string effect;   // effect dispatch ID，对应 spell.js switch

        [Header("Rune Cost")]
        [SerializeField] public int schCost;
        [SerializeField] public RuneType schType;
        [SerializeField] public int schCost2;
        [SerializeField] public RuneType schType2;

        [Header("Echo Cost (回响)")]
        [SerializeField] public int echoSchCost;
        [SerializeField] public RuneType echoSchType;
        [SerializeField] public int echoManaCost;    // 回响额外法力费用（如扑咚！=2）

        [Header("Equipment Equip Cost (装备专用)")]
        [SerializeField] public int equipSchCost;
        [SerializeField] public RuneType equipSchType;

        [Header("Special Flags")]
        [SerializeField] public bool isHero;             // 英雄卡（驻留英雄区）
        [SerializeField] public int strongAtkBonus = 1;  // 强攻加成（默认1）
        [SerializeField] public int guardBonus     = 1;  // 坚守加成（默认1）
        [SerializeField] public bool canMoveToBase = true;

        [Header("Art")]
        [SerializeField] public Sprite artwork;          // 卡牌图片
        [SerializeField] public string imgPath;          // 原版 img 路径（导入参考用）

        // ── 未来卡组构筑扩展 ──
        [Header("Deck Builder (Future)")]
        [SerializeField] public string cardPool = "default";  // 所属卡池标签
    }
}
