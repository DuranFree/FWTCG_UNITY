using NUnit.Framework;
using FWTCG.Core;
using FWTCG.Data;

namespace FWTCG.Tests
{
    /// <summary>
    /// P2 行为验证测试 — RuneController
    /// 对照原版：hint.js tapRune / tapAllRunes / recycleRune / cancelRunes / confirmRunes
    /// </summary>
    public class RuneControllerTests
    {
        private GameState _g;
        private RuneController _rc;

        [SetUp]
        public void SetUp()
        {
            _g  = new GameState();
            _rc = new RuneController(_g);
            _g.turn  = Owner.Player;
            _g.phase = GamePhase.Action;
        }

        // ─────────────────────────────────────────
        // TapRune
        // ─────────────────────────────────────────

        [Test]
        public void TapRune_AddsToPending()
        {
            _g.pRunes.Add(RuneInstance.Create(RuneType.Blazing));
            _rc.TapRune(0);

            Assert.AreEqual(1, _g.pendingRunes.Count);
            Assert.AreEqual(PendingRuneAction.Tap, _g.pendingRunes[0].action);
            Assert.AreEqual(0, _g.pendingRunes[0].idx);
        }

        [Test]
        public void TapRune_Toggle_RemovesIfAlreadyPending()
        {
            _g.pRunes.Add(RuneInstance.Create(RuneType.Blazing));
            _rc.TapRune(0); // add
            _rc.TapRune(0); // remove

            Assert.AreEqual(0, _g.pendingRunes.Count);
        }

        [Test]
        public void TapRune_Ignores_AlreadyTappedRune()
        {
            var r = RuneInstance.Create(RuneType.Blazing);
            r.tapped = true;
            _g.pRunes.Add(r);
            _rc.TapRune(0);

            Assert.AreEqual(0, _g.pendingRunes.Count);
        }

        [Test]
        public void TapRune_Ignores_OutOfRangeIndex()
        {
            _rc.TapRune(5); // no runes in field
            Assert.AreEqual(0, _g.pendingRunes.Count);
        }

        // ─────────────────────────────────────────
        // TapAllRunes
        // ─────────────────────────────────────────

        [Test]
        public void TapAllRunes_AddsAllUntapped()
        {
            _g.pRunes.Add(RuneInstance.Create(RuneType.Blazing));
            _g.pRunes.Add(RuneInstance.Create(RuneType.Radiant));
            _g.pRunes.Add(RuneInstance.Create(RuneType.Verdant));

            _rc.TapAllRunes();

            Assert.AreEqual(3, _g.pendingRunes.Count);
        }

        [Test]
        public void TapAllRunes_SkipsAlreadyTapped()
        {
            var r0 = RuneInstance.Create(RuneType.Blazing);
            r0.tapped = true;
            _g.pRunes.Add(r0);
            _g.pRunes.Add(RuneInstance.Create(RuneType.Radiant));

            _rc.TapAllRunes();

            Assert.AreEqual(1, _g.pendingRunes.Count);
            Assert.AreEqual(1, _g.pendingRunes[0].idx);
        }

        [Test]
        public void TapAllRunes_DoesNotDuplicate_ExistingPending()
        {
            _g.pRunes.Add(RuneInstance.Create(RuneType.Blazing));
            _rc.TapRune(0);       // 已加入
            _rc.TapAllRunes();    // 应跳过已在队列中的

            Assert.AreEqual(1, _g.pendingRunes.Count);
        }

        // ─────────────────────────────────────────
        // RecycleRune
        // ─────────────────────────────────────────

        [Test]
        public void RecycleRune_AddsToPending()
        {
            _g.pRunes.Add(RuneInstance.Create(RuneType.Blazing));
            _rc.RecycleRune(0);

            Assert.AreEqual(1, _g.pendingRunes.Count);
            Assert.AreEqual(PendingRuneAction.Recycle, _g.pendingRunes[0].action);
        }

        [Test]
        public void RecycleRune_Toggle_RemovesIfAlreadyPending()
        {
            _g.pRunes.Add(RuneInstance.Create(RuneType.Blazing));
            _rc.RecycleRune(0);
            _rc.RecycleRune(0);

            Assert.AreEqual(0, _g.pendingRunes.Count);
        }

        [Test]
        public void RecycleRune_AllowsTapped_Rune()
        {
            var r = RuneInstance.Create(RuneType.Blazing);
            r.tapped = true;
            _g.pRunes.Add(r);
            _rc.RecycleRune(0);

            Assert.AreEqual(1, _g.pendingRunes.Count, "已横置的符文也可以回收");
        }

        // ─────────────────────────────────────────
        // CancelRunes
        // ─────────────────────────────────────────

        [Test]
        public void CancelRunes_ClearsAllPending()
        {
            _g.pRunes.Add(RuneInstance.Create(RuneType.Blazing));
            _g.pRunes.Add(RuneInstance.Create(RuneType.Radiant));
            _rc.TapRune(0);
            _rc.RecycleRune(1);

            _rc.CancelRunes();

            Assert.AreEqual(0, _g.pendingRunes.Count);
        }

        // ─────────────────────────────────────────
        // ConfirmRunes — Tap
        // ─────────────────────────────────────────

        [Test]
        public void ConfirmRunes_Tap_SetsTappedAndIncrementsMana()
        {
            _g.pRunes.Add(RuneInstance.Create(RuneType.Blazing));
            _rc.TapRune(0);
            _rc.ConfirmRunes();

            Assert.IsTrue(_g.pRunes[0].tapped);
            Assert.AreEqual(1, _g.pMana);
        }

        [Test]
        public void ConfirmRunes_TapAll_IncrementsManaForEach()
        {
            _g.pRunes.Add(RuneInstance.Create(RuneType.Blazing));
            _g.pRunes.Add(RuneInstance.Create(RuneType.Radiant));
            _g.pRunes.Add(RuneInstance.Create(RuneType.Verdant));
            _rc.TapAllRunes();
            _rc.ConfirmRunes();

            Assert.AreEqual(3, _g.pMana);
        }

        [Test]
        public void ConfirmRunes_Tap_SkipsAlreadyTapped()
        {
            var r = RuneInstance.Create(RuneType.Blazing);
            r.tapped = true;
            _g.pRunes.Add(r);
            // 手动添加非法的 tap pending（模拟边界情况）
            _g.pendingRunes.Add(new PendingRune { idx = 0, action = PendingRuneAction.Tap });
            _rc.ConfirmRunes();

            Assert.AreEqual(0, _g.pMana, "已横置符文不应再增加法力");
        }

        // ─────────────────────────────────────────
        // ConfirmRunes — Recycle
        // ─────────────────────────────────────────

        [Test]
        public void ConfirmRunes_Recycle_RemovesFromField()
        {
            _g.pRunes.Add(RuneInstance.Create(RuneType.Blazing));
            _rc.RecycleRune(0);
            _rc.ConfirmRunes();

            Assert.AreEqual(0, _g.pRunes.Count, "符文已从场上移除");
        }

        [Test]
        public void ConfirmRunes_Recycle_AddsToRuneDeckFront()
        {
            var r = RuneInstance.Create(RuneType.Blazing);
            _g.pRunes.Add(r);
            _g.pRuneDeck.Add(RuneInstance.Create(RuneType.Radiant)); // 原有1张在底部
            _rc.RecycleRune(0);
            _rc.ConfirmRunes();

            Assert.AreEqual(2, _g.pRuneDeck.Count);
            Assert.AreEqual(r, _g.pRuneDeck[0], "回收符文应插入牌堆最前面（头部）");
        }

        [Test]
        public void ConfirmRunes_Recycle_AddsSch()
        {
            var r = RuneInstance.Create(RuneType.Blazing);
            _g.pRunes.Add(r);
            _rc.RecycleRune(0);
            _rc.ConfirmRunes();

            Assert.AreEqual(1, _g.pSch.Get(RuneType.Blazing));
        }

        [Test]
        public void ConfirmRunes_Recycle_UntapsRecycledRune()
        {
            var r = RuneInstance.Create(RuneType.Blazing);
            r.tapped = true;
            _g.pRunes.Add(r);
            _rc.RecycleRune(0);
            _rc.ConfirmRunes();

            Assert.IsFalse(r.tapped, "回收到牌堆的符文应重置为未横置");
        }

        [Test]
        public void ConfirmRunes_Recycle_SortDescByIdx_PreservesIndices()
        {
            // 三张符文，回收中间和最后 → 降序处理确保索引不乱
            _g.pRunes.Add(RuneInstance.Create(RuneType.Blazing));  // idx 0
            _g.pRunes.Add(RuneInstance.Create(RuneType.Radiant));  // idx 1
            _g.pRunes.Add(RuneInstance.Create(RuneType.Verdant));  // idx 2

            _rc.RecycleRune(1);
            _rc.RecycleRune(2);
            _rc.ConfirmRunes();

            // 只剩 idx 0 的 Blazing
            Assert.AreEqual(1, _g.pRunes.Count);
            Assert.AreEqual(RuneType.Blazing, _g.pRunes[0].runeType);
            Assert.AreEqual(2, _g.pRuneDeck.Count);
            Assert.AreEqual(2, _g.pSch.Total());
        }

        [Test]
        public void ConfirmRunes_ClearsPendingAfterExecution()
        {
            _g.pRunes.Add(RuneInstance.Create(RuneType.Blazing));
            _rc.TapRune(0);
            _rc.ConfirmRunes();

            Assert.AreEqual(0, _g.pendingRunes.Count);
        }

        [Test]
        public void ConfirmRunes_NoOp_WhenPendingEmpty()
        {
            _rc.ConfirmRunes(); // 不应抛异常
            Assert.AreEqual(0, _g.pMana);
        }
    }
}
