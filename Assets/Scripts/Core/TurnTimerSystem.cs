using System;

namespace FWTCG.Core
{
    /// <summary>
    /// 30 秒回合计时器逻辑。
    /// 纯 C# 类（无 MonoBehaviour），可在 EditMode 测试中直接驱动。
    /// 生产环境由 Unity Update() 每帧调用 Tick(Time.deltaTime)。
    /// </summary>
    public class TurnTimerSystem
    {
        // ── 当前剩余秒数（浮点，精度更高）──
        private float _remaining;

        // ── 是否正在计时 ──
        public bool IsRunning { get; private set; }

        // ── 剩余整秒（UI 展示用）──
        public int TimeRemaining => Math.Max(0, (int)Math.Ceiling(_remaining));

        // ── 超时回调（注入；默认空操作）──
        public Action OnTimeout = () => { };

        // ── 重置：将计时器恢复到 TIMER_SECONDS，不自动启动 ──
        public void Reset()
        {
            _remaining = GameState.TIMER_SECONDS;
            IsRunning  = false;
        }

        // ── 启动计时 ──
        public void Start()
        {
            IsRunning = true;
        }

        // ── 停止（回合结束 / 游戏暂停时调用）──
        public void Stop()
        {
            IsRunning = false;
        }

        /// <summary>
        /// 每帧驱动（Unity Update 或测试直接传入 delta）。
        /// 仅在 IsRunning 时递减；到 0 时触发 OnTimeout 并停止。
        /// </summary>
        public void Tick(float delta)
        {
            if (!IsRunning) return;

            _remaining -= delta;

            if (_remaining <= 0f)
            {
                _remaining = 0f;
                IsRunning  = false;
                OnTimeout?.Invoke();
            }
        }
    }
}
