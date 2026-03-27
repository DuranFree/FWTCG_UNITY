using System.Collections.Generic;
using FWTCG.Data;

namespace FWTCG.Core
{
    /// <summary>
    /// 运行时单位实例，等价原版 mk(card) 的返回值。
    /// 纯 C# 类，不依赖 MonoBehaviour，可在 EditMode 测试中直接实例化。
    /// </summary>
    public class CardInstance
    {
        // ── 静态 UID 计数器（等价原版 let uid = 0）──
        private static int _uidCounter = 0;
        public static void ResetUidCounter() => _uidCounter = 0;

        // ── 来自 CardData 的基础字段（深拷贝）──
        public string id;
        public string cardName;
        public CardRegion region;
        public CardType type;
        public int cost;
        public int atk;          // 基础战力（永久 buff 会修改；cards.js 里的 atk 字段）
        public int hp;           // champion 专用；普通随从不用
        public List<string> keywords;
        public string text;
        public string emoji;
        public string effect;
        public int schCost;
        public RuneType schType;
        public int schCost2;
        public RuneType schType2;
        public int echoSchCost;
        public RuneType echoSchType;
        public int echoManaCost;
        public int equipSchCost;
        public RuneType equipSchType;
        public bool isHero;
        public int strongAtkBonus;
        public int guardBonus;
        public bool canMoveToBase;
        public string imgPath;

        // ── 运行时字段（等价原版 mk() 追加的字段）──
        public int uid;
        public int currentHp;    // 当前生命 = currentAtk，受伤后减少，战斗后重置
        public int currentAtk;   // 当前战力（不含回合临时 buff）
        public bool exhausted;
        public bool stunned;
        public TurnBuffs tb;
        public bool buffToken;
        public List<CardInstance> attachedEquipments;

        // ── 特殊运行时标记 ──
        public bool trinityEquipped;
        public int level = 1;    // champion 专用
        public bool evolved;     // 卡莎进化标记

        // ── 有效战力（唯一正确战斗计算入口，等价原版 atk(u)）──
        public int EffectiveAtk => System.Math.Max(1, currentAtk + tb.atk);

        // ── 工厂方法，等价原版 mk(card) ──
        public static CardInstance From(CardData data)
        {
            var inst = new CardInstance();
            inst.uid            = ++_uidCounter;
            inst.id             = data.id;
            inst.cardName       = data.cardName;
            inst.region         = data.region;
            inst.type           = data.type;
            inst.cost           = data.cost;
            inst.atk            = data.atk;
            inst.hp             = data.hp;
            inst.keywords       = new List<string>(data.keywords);
            inst.text           = data.text;
            inst.emoji          = data.emoji;
            inst.effect         = data.effect;
            inst.schCost        = data.schCost;
            inst.schType        = data.schType;
            inst.schCost2       = data.schCost2;
            inst.schType2       = data.schType2;
            inst.echoSchCost    = data.echoSchCost;
            inst.echoSchType    = data.echoSchType;
            inst.echoManaCost   = data.echoManaCost;
            inst.equipSchCost   = data.equipSchCost;
            inst.equipSchType   = data.equipSchType;
            inst.isHero         = data.isHero;
            inst.strongAtkBonus = data.strongAtkBonus > 0 ? data.strongAtkBonus : 1;
            inst.guardBonus     = data.guardBonus     > 0 ? data.guardBonus     : 1;
            inst.canMoveToBase  = data.canMoveToBase;
            inst.imgPath        = data.imgPath;

            // ── 运行时初始值 ──
            // 注意：普通随从 currentHp = atk（atk即HP，无独立生命值）
            // champion 由 GameState 单独初始化，不走此路径
            inst.currentAtk = data.atk;
            inst.currentHp  = data.atk;   // 等价原版：currentHp = a（a = t.atk ?? 0）
            inst.exhausted  = false;
            inst.stunned    = false;
            inst.tb         = new TurnBuffs { atk = 0 };
            inst.buffToken  = false;
            inst.attachedEquipments = new List<CardInstance>();

            return inst;
        }

        public bool IsPowerful => EffectiveAtk >= 5;

        /// <summary>
        /// 等价原版 mk(c)：以此实例为模板，创建新的部署实例（新 UID，重置运行时状态）。
        /// 用于从手牌/模板部署单位到场上，不改变原始实例。
        /// </summary>
        public CardInstance Mk()
        {
            var inst = new CardInstance();
            inst.uid            = ++_uidCounter;
            inst.id             = id;
            inst.cardName       = cardName;
            inst.region         = region;
            inst.type           = type;
            inst.cost           = cost;
            inst.atk            = atk;
            inst.hp             = hp;
            inst.keywords       = new List<string>(keywords ?? new List<string>());
            inst.text           = text;
            inst.emoji          = emoji;
            inst.effect         = effect;
            inst.schCost        = schCost;
            inst.schType        = schType;
            inst.schCost2       = schCost2;
            inst.schType2       = schType2;
            inst.echoSchCost    = echoSchCost;
            inst.echoSchType    = echoSchType;
            inst.echoManaCost   = echoManaCost;
            inst.equipSchCost   = equipSchCost;
            inst.equipSchType   = equipSchType;
            inst.isHero         = isHero;
            inst.strongAtkBonus = strongAtkBonus > 0 ? strongAtkBonus : 1;
            inst.guardBonus     = guardBonus     > 0 ? guardBonus     : 1;
            inst.canMoveToBase  = canMoveToBase;
            inst.imgPath        = imgPath;
            // 重置运行时状态（等价原版 mk() 追加字段）
            inst.currentAtk = atk;
            inst.currentHp  = atk;   // atk=HP for followers
            inst.exhausted  = false;
            inst.stunned    = false;
            inst.tb         = new TurnBuffs { atk = 0 };
            inst.buffToken  = false;
            inst.attachedEquipments = new List<CardInstance>();
            return inst;
        }

        /// <summary>
        /// 分配一个新 UID（用于运行时临时创建不来自 CardData 的卡牌，如碎片法术）。
        /// </summary>
        public static int AllocUid() => ++_uidCounter;

        public override string ToString() => $"[{uid}]{cardName}({currentAtk}/{currentHp})";
    }

    /// <summary>
    /// 回合临时 buff（等价原版 tb 对象）。每回合结束时归零。
    /// </summary>
    public class TurnBuffs
    {
        public int atk;  // 本回合临时战力加成
    }

    /// <summary>
    /// 场上符文实例（等价原版 mkRune() 返回值）。
    /// </summary>
    public class RuneInstance
    {
        private static int _uidCounter = 0;
        public static void ResetUidCounter() => _uidCounter = 0;

        public int uid;
        public bool tapped;
        public RuneType runeType;

        public static RuneInstance Create(RuneType type)
        {
            return new RuneInstance
            {
                uid      = ++_uidCounter,
                tapped   = false,
                runeType = type
            };
        }
    }
}
