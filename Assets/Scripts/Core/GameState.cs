using System.Collections.Generic;
using FWTCG.Data;

namespace FWTCG.Core
{
    /// <summary>
    /// 全局游戏状态，等价原版 G 对象。
    /// 纯 C# 类，可在测试中直接 new GameState() 实例化。
    /// </summary>
    public class GameState
    {
        // ── 常量（等价原版硬编码数值）──
        public const int WIN_SCORE    = 8;
        public const int MAX_HAND     = 7;
        public const int MAX_BF_UNITS = 2;   // 每侧战场最多2个单位
        public const int TIMER_SECONDS = 30;

        // ── 积分 ──
        public int pScore;
        public int eScore;

        // ── 区域 ──
        public List<CardInstance> pDeck     = new();
        public List<CardInstance> eDeck     = new();
        public List<CardInstance> pHand     = new();
        public List<CardInstance> eHand     = new();
        public List<CardInstance> pBase     = new();
        public List<CardInstance> eBase     = new();
        public List<CardInstance> pDiscard  = new();
        public List<CardInstance> eDiscard  = new();
        public List<RuneInstance> pRunes    = new();
        public List<RuneInstance> eRunes    = new();
        public List<RuneInstance> pRuneDeck = new();
        public List<RuneInstance> eRuneDeck = new();

        // ── 战场（2条，固定）──
        public BattlefieldState[] bf = new BattlefieldState[]
        {
            new() { id = 1 },
            new() { id = 2 }
        };

        // 战场选择池
        public List<CardInstance> pBFPool = new();
        public List<CardInstance> eBFPool = new();
        public int? bfSelIdx;

        // ── 法力 & 符能 ──
        public int pMana;
        public int eMana;
        public SchematicCounts pSch = new();
        public SchematicCounts eSch = new();

        // ── 传奇 & 英雄 ──
        public LegendInstance pLeg;
        public LegendInstance eLeg;
        public CardInstance pHero;
        public CardInstance eHero;
        public bool selCardFromHero;

        // ── 回合 & 阶段 ──
        public int round = 1;
        public Owner turn  = Owner.Player;
        public Owner first = Owner.Player;
        public GamePhase phase = GamePhase.Init;
        public bool pFirstTurnDone;
        public bool eFirstTurnDone;

        // ── 选牌 & 目标 ──
        public CardInstance selCard;
        public int selCardIdx = -1;
        public bool selCardIsHero;
        public int? selFailUid;
        public CardInstance pendingDeploy;
        public bool deployChoosing;
        public List<RuneInstance> pendingRunes = new();
        public PendingMove pendingMove;

        // ── 法术目标 ──
        public bool spellTargeting;
        public List<int> spellTargetPool = new();
        public int? selSpellTargetUid;

        // ── 交互锁 ──
        public bool prompting;

        // ── 统计追踪 ──
        public DamageDealt dmgDealt = new();
        public int cardsPlayedThisTurn;
        public Owner? cardLockTarget;
        public bool extraTurnPending;
        public bool pRallyActive;
        public bool eRallyActive;
        public int pNextAllyBuff;
        public int eNextAllyBuff;
        public int pAllyDmgDealt;
        public int eAllyDmgDealt;
        public List<int> bfScoredThisTurn    = new();
        public List<int> bfConqueredThisTurn = new();
        public int lastPlayerSpellCost;

        // ── 对决 ──
        public bool duelActive;
        public int? duelBf;
        public Owner? duelAttacker;
        public Owner? duelTurn;
        public int duelSkips;

        // ── 反应窗口 ──
        public bool reactionWindowOpen;
        public Owner? reactionWindowFor;

        // ── 计时器 ──
        public int turnTimerSeconds = TIMER_SECONDS;

        // ── 游戏结束 ──
        public bool gameOver;

        // ── Mulligan ──
        public List<CardInstance> mulSel = new();

        // ── 辅助：根据 owner 获取对应字段 ──
        public List<CardInstance> GetDeck(Owner o)    => o == Owner.Player ? pDeck    : eDeck;
        public List<CardInstance> GetHand(Owner o)    => o == Owner.Player ? pHand    : eHand;
        public List<CardInstance> GetBase(Owner o)    => o == Owner.Player ? pBase    : eBase;
        public List<CardInstance> GetDiscard(Owner o) => o == Owner.Player ? pDiscard : eDiscard;
        public List<RuneInstance> GetRunes(Owner o)   => o == Owner.Player ? pRunes   : eRunes;
        public List<RuneInstance> GetRuneDeck(Owner o)=> o == Owner.Player ? pRuneDeck: eRuneDeck;
        public int GetMana(Owner o)                   => o == Owner.Player ? pMana    : eMana;
        public void SetMana(Owner o, int v)           { if (o == Owner.Player) pMana = v; else eMana = v; }
        public SchematicCounts GetSch(Owner o)        => o == Owner.Player ? pSch     : eSch;
        public LegendInstance GetLeg(Owner o)         => o == Owner.Player ? pLeg     : eLeg;
        public Owner Opponent(Owner o)                => o == Owner.Player ? Owner.Enemy : Owner.Player;
    }

    // ────────────────────────────────────────────
    // 支撑数据结构
    // ────────────────────────────────────────────

    public enum Owner { Player, Enemy }

    public enum GamePhase { Init, Awaken, Start, Summon, Draw, Action, End }

    public class BattlefieldState
    {
        public int id;
        public List<CardInstance> pU      = new();   // 玩家战场单位
        public List<CardInstance> eU      = new();   // 敌方战场单位
        public Owner? ctrl                = null;    // 控制权
        public bool conqDone              = false;   // 本回合征服已触发
        public CardInstance standby       = null;    // AI 待命单位（face-down）
        public CardInstance card          = null;    // 战场牌实例
    }

    /// <summary>
    /// 六种符能计数，等价原版 pSch / eSch 对象。
    /// </summary>
    public class SchematicCounts
    {
        public int blazing;
        public int radiant;
        public int verdant;
        public int crushing;
        public int chaos;
        public int order;

        public int Get(RuneType t) => t switch
        {
            RuneType.Blazing  => blazing,
            RuneType.Radiant  => radiant,
            RuneType.Verdant  => verdant,
            RuneType.Crushing => crushing,
            RuneType.Chaos    => chaos,
            RuneType.Order    => order,
            _ => 0
        };

        public void Add(RuneType t, int n = 1)
        {
            switch (t)
            {
                case RuneType.Blazing:  blazing  += n; break;
                case RuneType.Radiant:  radiant  += n; break;
                case RuneType.Verdant:  verdant  += n; break;
                case RuneType.Crushing: crushing += n; break;
                case RuneType.Chaos:    chaos    += n; break;
                case RuneType.Order:    order    += n; break;
            }
        }

        public void Spend(RuneType t, int n = 1)
        {
            switch (t)
            {
                case RuneType.Blazing:  blazing  = System.Math.Max(0, blazing  - n); break;
                case RuneType.Radiant:  radiant  = System.Math.Max(0, radiant  - n); break;
                case RuneType.Verdant:  verdant  = System.Math.Max(0, verdant  - n); break;
                case RuneType.Crushing: crushing = System.Math.Max(0, crushing - n); break;
                case RuneType.Chaos:    chaos    = System.Math.Max(0, chaos    - n); break;
                case RuneType.Order:    order    = System.Math.Max(0, order    - n); break;
            }
        }

        public void Reset() { blazing = radiant = verdant = crushing = chaos = order = 0; }

        public int Total() => blazing + radiant + verdant + crushing + chaos + order;
    }

    /// <summary>
    /// 传奇实例（champion），不走 CardInstance.From()，HP 独立维护。
    /// </summary>
    public class LegendInstance
    {
        public CardData data;
        public int currentHp;    // 运行期唯一操作此字段，不操作 data.hp
        public int currentAtk;
        public bool exhausted;
        public int level = 1;
        public bool evolved;

        // 技能使用追踪（每回合重置）
        public System.Collections.Generic.Dictionary<string, bool> usedThisTurn = new();

        public static LegendInstance From(CardData data)
        {
            return new LegendInstance
            {
                data       = data,
                currentHp  = data.hp,    // 初始化时用 hp，之后只操作 currentHp
                currentAtk = data.atk,
                exhausted  = false,
                level      = 1,
                evolved    = false
            };
        }
    }

    public class PendingMove
    {
        public List<CardInstance> units = new();
        public int toBfId;
    }

    public class DamageDealt
    {
        public int p;
        public int e;
    }
}
