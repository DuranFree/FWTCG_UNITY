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
        public void TapRune_ThenConfirm_TapsRuneAndAddsMana()
        {
            _g.pRunes.Add(RuneInstance.Create(RuneType.Blazing));
            _rc.TapRune(0);
            _rc.ConfirmRunes();

            Assert.IsTrue(_g.pRunes[0].tapped);
            Assert.AreEqual(1, _g.pMana);
        }

        [Test]
        public void TapRune_DoubleToggle_ThenConfirm_NoEffect()
        {
            _g.pRunes.Add(RuneInstance.Create(RuneType.Blazing));
            _rc.TapRune(0); // stage
            _rc.TapRune(0); // unstage
            _rc.ConfirmRunes();

            Assert.IsFalse(_g.pRunes[0].tapped);
            Assert.AreEqual(0, _g.pMana);
        }

        [Test]
        public void TapRune_AlreadyTapped_ThenConfirm_NoExtraMana()
        {
            var r = RuneInstance.Create(RuneType.Blazing);
            r.tapped = true;
            _g.pRunes.Add(r);
            _rc.TapRune(0); // should be ignored
            _rc.ConfirmRunes();

            Assert.AreEqual(0, _g.pMana);
        }

        [Test]
        public void TapRune_OutOfRange_ThenConfirm_NoEffect()
        {
            _rc.TapRune(5); // no runes at all
            _rc.ConfirmRunes();
            Assert.AreEqual(0, _g.pMana);
        }

        // ─────────────────────────────────────────
        // TapAllRunes
        // ─────────────────────────────────────────

        [Test]
        public void TapAllRunes_ThenConfirm_AddsManaForEachUntapped()
        {
            _g.pRunes.Add(RuneInstance.Create(RuneType.Blazing));
            _g.pRunes.Add(RuneInstance.Create(RuneType.Radiant));
            _g.pRunes.Add(RuneInstance.Create(RuneType.Verdant));

            _rc.TapAllRunes();
            _rc.ConfirmRunes();

            Assert.AreEqual(3, _g.pMana);
        }

        [Test]
        public void TapAllRunes_WithOneTappedOneUntapped_GivesOneMana()
        {
            var r0 = RuneInstance.Create(RuneType.Blazing);
            r0.tapped = true;
            _g.pRunes.Add(r0);
            _g.pRunes.Add(RuneInstance.Create(RuneType.Radiant));

            _rc.TapAllRunes();
            _rc.ConfirmRunes();

            Assert.AreEqual(1, _g.pMana); // 只有未横置的那张贡献法力
        }

        [Test]
        public void TapAllRunes_AfterManualTap_NoDuplicate()
        {
            _g.pRunes.Add(RuneInstance.Create(RuneType.Blazing));
            _rc.TapRune(0);      // 手动暂存
            _rc.TapAllRunes();   // 不应重复添加
            _rc.ConfirmRunes();

            Assert.AreEqual(1, _g.pMana); // 只获得1点法力，而非2
        }

        // ─────────────────────────────────────────
        // RecycleRune
        // ─────────────────────────────────────────

        [Test]
        public void RecycleRune_ThenConfirm_RemovesAndAddsSch()
        {
            _g.pRunes.Add(RuneInstance.Create(RuneType.Blazing));
            _rc.RecycleRune(0);
            _rc.ConfirmRunes();

            Assert.AreEqual(0, _g.pRunes.Count, "符文已从场上移除");
            Assert.AreEqual(1, _g.pSch.Get(RuneType.Blazing), "获得炽烈符能");
        }

        [Test]
        public void RecycleRune_DoubleToggle_ThenConfirm_NoEffect()
        {
            _g.pRunes.Add(RuneInstance.Create(RuneType.Blazing));
            _rc.RecycleRune(0); // 暂存
            _rc.RecycleRune(0); // 取消
            _rc.ConfirmRunes();

            Assert.AreEqual(1, _g.pRunes.Count, "符文留在场上");
            Assert.AreEqual(0, _g.pSch.Get(RuneType.Blazing), "未获得符能");
        }

        [Test]
        public void RecycleRune_TappedRune_ThenConfirm_AddsSch()
        {
            var r = RuneInstance.Create(RuneType.Blazing);
            r.tapped = true;
            _g.pRunes.Add(r);
            _rc.RecycleRune(0);
            _rc.ConfirmRunes();

            Assert.AreEqual(1, _g.pSch.Get(RuneType.Blazing), "横置符文回收后应获得符能");
        }

        // ─────────────────────────────────────────
        // CancelRunes
        // ─────────────────────────────────────────

        [Test]
        public void CancelRunes_ThenConfirm_NoEffect()
        {
            _g.pRunes.Add(RuneInstance.Create(RuneType.Blazing));
            _g.pRunes.Add(RuneInstance.Create(RuneType.Radiant));
            _rc.TapRune(0);
            _rc.RecycleRune(1);

            _rc.CancelRunes();
            _rc.ConfirmRunes(); // 取消后确认应无效

            Assert.AreEqual(0, _g.pMana, "取消后不应获得法力");
            Assert.AreEqual(2, _g.pRunes.Count, "取消后符文应仍在场上");
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
