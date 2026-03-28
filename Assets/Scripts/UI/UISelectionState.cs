using FWTCG.Data;

namespace FWTCG.UI
{
    /// <summary>
    /// 玩家当前 UI 交互阶段。
    /// </summary>
    public enum UIPhase
    {
        /// <summary>无选中，等待玩家第一次点击。</summary>
        Idle,
        /// <summary>已选中一张手牌，等待玩家点击目标区域出牌。</summary>
        CardSelected,
        /// <summary>已选中基地/战场上的己方单位，等待玩家点击目标位置移动。</summary>
        UnitSelected,
    }

    /// <summary>
    /// 纯 C# 选牌/选单位状态机，不含任何 MonoBehaviour 依赖。
    /// GameUI 持有此对象，在每次点击时推进状态，在 Refresh 后可读取当前选中情况。
    /// </summary>
    public class UISelectionState
    {
        // ─────────────────────────────────────────────
        // 公共状态（只读）
        // ─────────────────────────────────────────────

        public UIPhase Phase       { get; private set; } = UIPhase.Idle;
        public int     SelectedUid { get; private set; } = -1;

        public bool IsIdle         => Phase == UIPhase.Idle;
        public bool IsCardSelected => Phase == UIPhase.CardSelected;
        public bool IsUnitSelected => Phase == UIPhase.UnitSelected;

        // ─────────────────────────────────────────────
        // 状态推进
        // ─────────────────────────────────────────────

        /// <summary>
        /// 玩家点击手牌 uid。
        /// 若该牌已选中则取消选中（toggle）；否则切换到 CardSelected。
        /// </summary>
        public void ToggleCard(int uid)
        {
            if (IsCardSelected && SelectedUid == uid)
                Clear();
            else
            {
                Phase       = UIPhase.CardSelected;
                SelectedUid = uid;
            }
        }

        /// <summary>
        /// 玩家点击基地/战场上的己方单位 uid。
        /// 已选中同一单位则取消（toggle）；否则切换到 UnitSelected。
        /// </summary>
        public void ToggleUnit(int uid)
        {
            if (IsUnitSelected && SelectedUid == uid)
                Clear();
            else
            {
                Phase       = UIPhase.UnitSelected;
                SelectedUid = uid;
            }
        }

        /// <summary>
        /// 强制选中手牌（不 toggle），用于拖拽放置前确保状态正确。
        /// </summary>
        public void SelectCard(int uid)
        {
            Phase       = UIPhase.CardSelected;
            SelectedUid = uid;
        }

        /// <summary>
        /// 出牌或移动动作已提交（不论成功与否），清空选中状态。
        /// </summary>
        public void Clear()
        {
            Phase       = UIPhase.Idle;
            SelectedUid = -1;
        }
    }
}
