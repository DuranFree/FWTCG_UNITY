namespace FWTCG.Core
{
    /// <summary>
    /// 符文操作控制器，等价原版 hint.js 的
    /// tapRune / tapAllRunes / recycleRune / cancelRunes / confirmRunes。
    ///
    /// 采用「暂存→确认」两步机制：
    ///   1. TapRune / RecycleRune / TapAllRunes — 向 G.pendingRunes 添加待确认项（或取消已有项）
    ///   2. ConfirmRunes — 按 idx 降序执行所有待确认项，确保 splice 不破坏前面的下标
    ///
    /// 纯 C# 类，不依赖 MonoBehaviour，可在 EditMode 测试中直接实例化。
    /// </summary>
    public class RuneController
    {
        public readonly GameState G;

        public RuneController(GameState g) { G = g; }

        // ── TapRune: 切换单张符文的横置待确认状态 ──
        /// <summary>
        /// 若该符文已在 pendingRunes 中则移除（撤销），否则添加横置意图。
        /// 已横置的符文忽略。
        /// </summary>
        public void TapRune(int idx)
        {
            if (G.gameOver) return;
            if (idx < 0 || idx >= G.pRunes.Count) return;
            var r = G.pRunes[idx];
            if (r.tapped) return;

            int existing = G.pendingRunes.FindIndex(p => p.idx == idx);
            if (existing != -1)
                G.pendingRunes.RemoveAt(existing);
            else
                G.pendingRunes.Add(new PendingRune { idx = idx, action = PendingRuneAction.Tap });
        }

        // ── TapAllRunes: 将所有未横置符文加入横置队列 ──
        public void TapAllRunes()
        {
            if (G.gameOver) return;
            for (int i = 0; i < G.pRunes.Count; i++)
            {
                var r = G.pRunes[i];
                if (!r.tapped && G.pendingRunes.FindIndex(p => p.idx == i) == -1)
                    G.pendingRunes.Add(new PendingRune { idx = i, action = PendingRuneAction.Tap });
            }
        }

        // ── RecycleRune: 切换单张符文的回收待确认状态 ──
        /// <summary>
        /// 若该符文已在 pendingRunes 中则移除（撤销），否则添加回收意图。
        /// 已横置或未横置均可回收。
        /// </summary>
        public void RecycleRune(int idx)
        {
            if (G.gameOver) return;
            if (idx < 0 || idx >= G.pRunes.Count) return;

            int existing = G.pendingRunes.FindIndex(p => p.idx == idx);
            if (existing != -1)
                G.pendingRunes.RemoveAt(existing);
            else
                G.pendingRunes.Add(new PendingRune { idx = idx, action = PendingRuneAction.Recycle });
        }

        // ── CancelRunes: 清空所有待确认操作 ──
        public void CancelRunes()
        {
            G.pendingRunes.Clear();
        }

        // ── ConfirmRunes: 执行所有待确认操作 ──
        /// <summary>
        /// 按 idx 降序排列后逐项执行：
        ///   Tap    → r.tapped = true, G.pMana++
        ///   Recycle → 从 pRunes 移除，插入 pRuneDeck 头部，G.pSch += 1
        /// 降序处理确保 RemoveAt 不破坏前面项的下标（等价原版 sort desc by idx）。
        /// </summary>
        public void ConfirmRunes()
        {
            if (G.pendingRunes.Count == 0) return;

            G.pendingRunes.Sort((a, b) => b.idx - a.idx);

            foreach (var pending in G.pendingRunes)
            {
                if (pending.idx < 0 || pending.idx >= G.pRunes.Count) continue;
                var r = G.pRunes[pending.idx];

                if (pending.action == PendingRuneAction.Tap)
                {
                    if (r.tapped) continue;
                    r.tapped = true;
                    G.pMana++;
                }
                else // Recycle
                {
                    G.pRunes.RemoveAt(pending.idx);
                    r.tapped = false;
                    G.pRuneDeck.Insert(0, r);  // 回到符文牌堆底部（等价原版 unshift）
                    G.pSch.Add(r.runeType);
                }
            }

            G.pendingRunes.Clear();
        }
    }
}
