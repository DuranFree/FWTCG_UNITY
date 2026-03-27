using NUnit.Framework;
using FWTCG.UI;

namespace FWTCG.Tests.EditMode
{
    /// <summary>
    /// UISelectionState 行为测试。
    /// 测试状态机的全部转换路径，不依赖任何 MonoBehaviour。
    /// </summary>
    public class UISelectionStateTests
    {
        private UISelectionState _sel;

        [SetUp]
        public void SetUp() => _sel = new UISelectionState();

        // ─────────────────────────────────────────────
        // 初始状态
        // ─────────────────────────────────────────────

        [Test]
        public void InitialState_IsIdle()
        {
            Assert.AreEqual(UIPhase.Idle, _sel.Phase);
            Assert.AreEqual(-1, _sel.SelectedUid);
            Assert.IsTrue(_sel.IsIdle);
            Assert.IsFalse(_sel.IsCardSelected);
            Assert.IsFalse(_sel.IsUnitSelected);
        }

        // ─────────────────────────────────────────────
        // ToggleCard
        // ─────────────────────────────────────────────

        [Test]
        public void ToggleCard_FromIdle_EntersCardSelected()
        {
            _sel.ToggleCard(42);
            Assert.AreEqual(UIPhase.CardSelected, _sel.Phase);
            Assert.AreEqual(42, _sel.SelectedUid);
            Assert.IsTrue(_sel.IsCardSelected);
        }

        [Test]
        public void ToggleCard_SameUid_ClearsToIdle()
        {
            _sel.ToggleCard(42);
            _sel.ToggleCard(42);    // 再次点击同一张牌 → 取消
            Assert.AreEqual(UIPhase.Idle, _sel.Phase);
            Assert.AreEqual(-1, _sel.SelectedUid);
        }

        [Test]
        public void ToggleCard_DifferentUid_SwitchesSelection()
        {
            _sel.ToggleCard(10);
            _sel.ToggleCard(20);    // 切换到另一张牌
            Assert.AreEqual(UIPhase.CardSelected, _sel.Phase);
            Assert.AreEqual(20, _sel.SelectedUid);
        }

        [Test]
        public void ToggleCard_FromUnitSelected_SwitchesToCardSelected()
        {
            _sel.ToggleUnit(99);
            _sel.ToggleCard(42);    // 从单位选中切换到手牌
            Assert.AreEqual(UIPhase.CardSelected, _sel.Phase);
            Assert.AreEqual(42, _sel.SelectedUid);
        }

        // ─────────────────────────────────────────────
        // ToggleUnit
        // ─────────────────────────────────────────────

        [Test]
        public void ToggleUnit_FromIdle_EntersUnitSelected()
        {
            _sel.ToggleUnit(7);
            Assert.AreEqual(UIPhase.UnitSelected, _sel.Phase);
            Assert.AreEqual(7, _sel.SelectedUid);
            Assert.IsTrue(_sel.IsUnitSelected);
        }

        [Test]
        public void ToggleUnit_SameUid_ClearsToIdle()
        {
            _sel.ToggleUnit(7);
            _sel.ToggleUnit(7);
            Assert.AreEqual(UIPhase.Idle, _sel.Phase);
            Assert.AreEqual(-1, _sel.SelectedUid);
        }

        [Test]
        public void ToggleUnit_DifferentUid_SwitchesUnit()
        {
            _sel.ToggleUnit(7);
            _sel.ToggleUnit(8);
            Assert.AreEqual(UIPhase.UnitSelected, _sel.Phase);
            Assert.AreEqual(8, _sel.SelectedUid);
        }

        [Test]
        public void ToggleUnit_FromCardSelected_SwitchesToUnitSelected()
        {
            _sel.ToggleCard(42);
            _sel.ToggleUnit(7);    // 从手牌切换到单位
            Assert.AreEqual(UIPhase.UnitSelected, _sel.Phase);
            Assert.AreEqual(7, _sel.SelectedUid);
        }

        // ─────────────────────────────────────────────
        // Clear
        // ─────────────────────────────────────────────

        [Test]
        public void Clear_FromCardSelected_ResetsToIdle()
        {
            _sel.ToggleCard(42);
            _sel.Clear();
            Assert.AreEqual(UIPhase.Idle, _sel.Phase);
            Assert.AreEqual(-1, _sel.SelectedUid);
        }

        [Test]
        public void Clear_FromUnitSelected_ResetsToIdle()
        {
            _sel.ToggleUnit(7);
            _sel.Clear();
            Assert.IsTrue(_sel.IsIdle);
        }

        [Test]
        public void Clear_FromIdle_RemainsIdle()
        {
            _sel.Clear();  // 空 Clear 不应报错
            Assert.IsTrue(_sel.IsIdle);
        }

        // ─────────────────────────────────────────────
        // 便捷属性
        // ─────────────────────────────────────────────

        [Test]
        public void IsCardSelected_OnlyTrueInCardSelectedPhase()
        {
            Assert.IsFalse(_sel.IsCardSelected);
            _sel.ToggleCard(1);
            Assert.IsTrue(_sel.IsCardSelected);
            Assert.IsFalse(_sel.IsUnitSelected);
            Assert.IsFalse(_sel.IsIdle);
        }

        [Test]
        public void IsUnitSelected_OnlyTrueInUnitSelectedPhase()
        {
            Assert.IsFalse(_sel.IsUnitSelected);
            _sel.ToggleUnit(5);
            Assert.IsTrue(_sel.IsUnitSelected);
            Assert.IsFalse(_sel.IsCardSelected);
            Assert.IsFalse(_sel.IsIdle);
        }
    }
}
